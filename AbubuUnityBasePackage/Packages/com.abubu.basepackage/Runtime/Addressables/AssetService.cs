using System;
using System.Collections.Generic;
using System.Threading;
using Abubu.Scene;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using Zenject;
using Object = UnityEngine.Object;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.Assets.AddressablesModuleInstaller))]

namespace Abubu.Assets
{
    public enum AssetScope
    {
        /// <summary>
        /// シーンに紐づけ、そのシーンがアンロードされたら自動解放。
        /// シーン遷移中 (ISceneService.IsLoading の間) にロードしたものは遷移先のシーンに紐づく
        /// (ロード画面での先読みがそのまま遷移先で使える)
        /// </summary>
        Scene,
        /// <summary>明示的に Release するまで保持 (共通 UI、BGM など)</summary>
        Global,
    }

    /// <summary>
    /// Addressables のラッパー。ハンドルを参照カウントで追跡し、スコープ単位で一括解放できる。
    /// <code>
    /// var enemy = await _assets.LoadAsync&lt;GameObject&gt;("Enemy/Slime");            // シーン終了で自動解放
    /// await _assets.PreloadAsync(new[] { "Stage1/Bgm", "Stage1/Boss" });            // ロード画面で先読み (遷移先シーンに紐づく)
    /// var icon = await _assets.LoadAsync&lt;Sprite&gt;("UI/Icon", AssetScope.Global);   // 明示的に解放するまで保持
    /// </code>
    /// アドレスの付与は SmartAddresser で自動化できる。
    /// </summary>
    public interface IAssetService
    {
        UniTask<T> LoadAsync<T>(string key, AssetScope scope = AssetScope.Scene, CancellationToken ct = default) where T : Object;
        UniTask<IList<T>> LoadByLabelAsync<T>(string label, AssetScope scope = AssetScope.Scene, CancellationToken ct = default) where T : Object;
        /// <summary>複数アセットを並列で先読みする。progress には 0〜1 が通知される</summary>
        UniTask PreloadAsync(IReadOnlyList<string> keys, AssetScope scope = AssetScope.Scene, IProgress<float> progress = null, CancellationToken ct = default);
        UniTask<GameObject> InstantiateAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null,
            AssetScope scope = AssetScope.Scene, CancellationToken ct = default);
        /// <summary>LoadAsync 1 回分の参照を解放する (参照が 0 になった時点で実際に解放)</summary>
        void Release<T>(string key) where T : Object;
        /// <summary>LoadByLabelAsync 1 回分の参照を解放する</summary>
        void ReleaseByLabel<T>(string label) where T : Object;
        /// <summary>Global スコープのアセットをすべて解放する</summary>
        void ReleaseGlobal();
        int LoadedCount { get; }
    }

    public sealed class AssetService : IAssetService, IDisposable
    {
        private sealed class Entry
        {
            public AsyncOperationHandle Handle;
            public int RefCount;
            /// <summary>Global なら GlobalHandle、遷移中なら PendingHandle、それ以外は紐づくシーンの handle</summary>
            public int SceneHandle;
        }

        private const int GlobalHandle = -1;
        private const int PendingHandle = -2;

        private readonly ISceneService _scene;
        private readonly Dictionary<(string key, Type type), Entry> _entries = new();
        private readonly List<(string, Type)> _buffer = new();
        private readonly IDisposable _loadingSubscription;

        public int LoadedCount => _entries.Count;

        public AssetService(ISceneService scene = null)
        {
            _scene = scene;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            // 遷移完了時に、遷移中に先読みしたものを新しいアクティブシーンへ紐づける
            _loadingSubscription = scene?.IsLoading.Where(static x => !x)
                .Subscribe(this, static (_, self) => self.AssignPendingToActiveScene());
        }

        public async UniTask<T> LoadAsync<T>(string key, AssetScope scope = AssetScope.Scene, CancellationToken ct = default) where T : Object
        {
            var entry = Acquire(key, typeof(T), scope, () => Addressables.LoadAssetAsync<T>(key));
            await WaitAsync(entry, key, typeof(T), ct);
            return (T)entry.Handle.Result;
        }

        public async UniTask<IList<T>> LoadByLabelAsync<T>(string label, AssetScope scope = AssetScope.Scene, CancellationToken ct = default) where T : Object
        {
            var entry = Acquire(label, typeof(IList<T>), scope, () => Addressables.LoadAssetsAsync<T>(label, null));
            await WaitAsync(entry, label, typeof(IList<T>), ct);
            return (IList<T>)entry.Handle.Result;
        }

        public async UniTask PreloadAsync(IReadOnlyList<string> keys, AssetScope scope = AssetScope.Scene, IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            if (keys.Count == 0)
            {
                progress?.Report(1f);
                return;
            }

            var completed = 0;
            var tasks = new UniTask[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                tasks[i] = LoadOneAsync(keys[i]);
            }
            await UniTask.WhenAll(tasks);

            async UniTask LoadOneAsync(string key)
            {
                await LoadAsync<Object>(key, scope, ct);
                progress?.Report((float)++completed / keys.Count);
            }
        }

        public async UniTask<GameObject> InstantiateAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null,
            AssetScope scope = AssetScope.Scene, CancellationToken ct = default)
        {
            // プレハブ自体をスコープで保持し、インスタンスは通常の Instantiate にする (解放漏れが起きにくい)
            var prefab = await LoadAsync<GameObject>(key, scope, ct);
            return Object.Instantiate(prefab, position, rotation, parent);
        }

        public void Release<T>(string key) where T : Object => ReleaseRef(key, typeof(T));

        public void ReleaseByLabel<T>(string label) where T : Object => ReleaseRef(label, typeof(IList<T>));

        public void ReleaseGlobal() => ReleaseWhere(GlobalHandle);

        private Entry Acquire(string key, Type type, AssetScope scope, Func<AsyncOperationHandle> load)
        {
            if (_entries.TryGetValue((key, type), out var entry))
            {
                entry.RefCount++;
                // Scene で持っていたものを Global でも要求されたら Global に昇格
                if (scope == AssetScope.Global) entry.SceneHandle = GlobalHandle;
                return entry;
            }

            entry = new Entry
            {
                Handle = load(),
                RefCount = 1,
                SceneHandle = scope == AssetScope.Global ? GlobalHandle : CurrentSceneHandle(),
            };
            _entries.Add((key, type), entry);
            return entry;
        }

        private async UniTask WaitAsync(Entry entry, string key, Type type, CancellationToken ct)
        {
            try
            {
                while (!entry.Handle.IsDone) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            catch (OperationCanceledException)
            {
                ReleaseRef(key, type);
                throw;
            }

            if (entry.Handle.Status != AsyncOperationStatus.Succeeded)
            {
                var exception = entry.Handle.OperationException;
                ReleaseRef(key, type);
                throw new InvalidOperationException($"[Abubu.Assets] \"{key}\" ({type.Name}) のロードに失敗しました。", exception);
            }
        }

        private void ReleaseRef(string key, Type type)
        {
            if (!_entries.TryGetValue((key, type), out var entry)) return;
            if (--entry.RefCount > 0) return;

            _entries.Remove((key, type));
            if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
        }

        private void ReleaseWhere(int sceneHandle)
        {
            _buffer.Clear();
            foreach (var pair in _entries)
            {
                if (pair.Value.SceneHandle == sceneHandle) _buffer.Add(pair.Key);
            }

            foreach (var id in _buffer)
            {
                var entry = _entries[id];
                _entries.Remove(id);
                if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
            }
        }

        private int CurrentSceneHandle() =>
            _scene != null && _scene.IsLoading.CurrentValue ? PendingHandle : SceneManager.GetActiveScene().handle;

        private void AssignPendingToActiveScene()
        {
            var active = SceneManager.GetActiveScene().handle;
            foreach (var entry in _entries.Values)
            {
                if (entry.SceneHandle == PendingHandle) entry.SceneHandle = active;
            }
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene) => ReleaseWhere(scene.handle);

        public void Dispose()
        {
            _loadingSubscription?.Dispose();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            foreach (var entry in _entries.Values)
            {
                if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
            }
            _entries.Clear();
        }
    }

    /// <summary>Addressables 導入時に AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class AddressablesModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public AddressablesModuleInstaller() { }

        public void Install(DiContainer container) => container.BindInterfacesTo<AssetService>().AsSingle();
    }
}
