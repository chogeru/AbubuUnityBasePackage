using System;
using System.Collections.Generic;
using System.Threading;
using Abubu.Audio;
using Abubu.SceneFlow;
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
        /// ロード画面の表示中 (ISceneService.ShowLoadingScreen が true の間) にロードしたものは遷移先のシーンに紐づく
        /// (ロード画面での先読みがそのまま遷移先で使える)。
        /// フェードアウト中にロードしたものは、まだアクティブな旧シーンに紐づく (旧シーンと一緒に解放される)。
        /// ロード画面を出さない遷移の途中で遷移先向けに先読みしたい場合は Global を使い、使い終わったら Release すること
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
            /// <summary>このエントリを指す辞書キー (PreloadAsync の Object 型と LoadAsync&lt;T&gt; の型で同じエントリを共有するため複数になりうる)</summary>
            public readonly List<(string key, Type type)> Ids = new(1);
            /// <summary>Global なら GlobalHandle、遷移中なら PendingHandle、それ以外は紐づくシーンの handle</summary>
            public int SceneHandle;
        }

        private const int GlobalHandle = -1;
        private const int PendingHandle = -2;

        private readonly ISceneService _scene;
        private readonly Dictionary<(string key, Type type), Entry> _entries = new();
        private readonly List<Entry> _buffer = new();
        private readonly IDisposable _loadingSubscription;
        /// <summary>実際に保持しているハンドルの数 (別名キーは数えない)</summary>
        private int _liveCount;

        public int LoadedCount => _liveCount;

        [Preserve]
        public AssetService(ISceneService scene = null)
        {
            _scene = scene;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            // 遷移完了時に、ロード画面中に先読みしたものを新しいアクティブシーンへ紐づける
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
            if (_entries.TryGetValue((key, type), out var entry) || TryAliasPreloaded(key, type, out entry))
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
            entry.Ids.Add((key, type));
            _entries.Add((key, type), entry);
            _liveCount++;
            return entry;
        }

        /// <summary>
        /// PreloadAsync (Object 型) で読み込み済みのアセットが要求された型と一致すれば、同じエントリを別名で共有する。
        /// これがないと LoadAsync&lt;GameObject&gt; が別エントリになり、先読みしたものとは別の寿命で管理されてしまう。
        /// 先読みが未完了の場合は型が分からないため共有しない (Addressables 側で同じアセットとして扱われるので実害はない)。
        /// </summary>
        private bool TryAliasPreloaded(string key, Type type, out Entry entry)
        {
            entry = null;
            if (type == typeof(Object)) return false;
            if (!_entries.TryGetValue((key, typeof(Object)), out var preloaded)) return false;
            if (!preloaded.Handle.IsValid() || !preloaded.Handle.IsDone || preloaded.Handle.Status != AsyncOperationStatus.Succeeded) return false;
            if (!type.IsInstanceOfType(preloaded.Handle.Result)) return false;

            preloaded.Ids.Add((key, type));
            _entries.Add((key, type), preloaded);
            entry = preloaded;
            return true;
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

            Remove(entry);
        }

        private void ReleaseWhere(int sceneHandle)
        {
            _buffer.Clear();
            foreach (var entry in _entries.Values)
            {
                // 別名キーで同じエントリが複数回出てくるため重複を除く
                if (entry.SceneHandle == sceneHandle && !_buffer.Contains(entry)) _buffer.Add(entry);
            }

            foreach (var entry in _buffer) Remove(entry);
            _buffer.Clear();
        }

        /// <summary>エントリを別名キーも含めて辞書から外し、ハンドルを 1 回だけ解放する</summary>
        private void Remove(Entry entry)
        {
            foreach (var id in entry.Ids) _entries.Remove(id);
            entry.Ids.Clear();
            _liveCount--;
            if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
        }

        /// <summary>
        /// 新しく読み込むアセットを紐づけるシーン。
        /// ロード画面の表示中だけ「遷移先待ち」にする。フェードアウト中は旧シーンがまだ動いているため、
        /// そこで (旧シーンのオブジェクトが) 読み込んだものを遷移先に持ち越さないよう旧シーンに紐づける。
        /// </summary>
        private int CurrentSceneHandle() =>
            _scene != null && _scene.IsLoading.CurrentValue && _scene.ShowLoadingScreen.CurrentValue
                ? PendingHandle
                : SceneManager.GetActiveScene().handle;

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
            _buffer.Clear();
            foreach (var entry in _entries.Values)
            {
                if (!_buffer.Contains(entry)) _buffer.Add(entry);
            }
            foreach (var entry in _buffer)
            {
                if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
            }
            _buffer.Clear();
            _entries.Clear();
            _liveCount = 0;
        }
    }

    /// <summary>
    /// Addressables 導入時に AbubuInstaller から自動で呼ばれる。
    /// IAssetService と、SoundSettings.UseAddressableClips がオンなら AddressablesAudioClipProvider (IAudioClipProvider) を登録する。
    /// <code>
    /// // 自前の Installer から使う場合
    /// new AddressablesModuleInstaller().Install(Container);
    /// </code>
    /// </summary>
    [Preserve]
    public sealed class AddressablesModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public AddressablesModuleInstaller() { }

        public void Install(DiContainer container)
        {
            if (!container.HasBinding<IAssetService>()) container.BindInterfacesTo<AssetService>().AsSingle();
            // IAudioClipProvider を自前で登録済みならそちらを優先する。
            // SoundSettings はインストール中に Resolve できないため、実際に必要になった時点で設定を見て実装を選ぶ
            if (!container.HasBinding<IAudioClipProvider>())
            {
                container.Bind<IAudioClipProvider>().FromMethod(CreateClipProvider).AsSingle();
            }
        }

        private static IAudioClipProvider CreateClipProvider(InjectContext context)
        {
            var settings = context.Container.TryResolve<SoundSettings>();
            return settings != null && settings.UseAddressableClips
                ? context.Container.Instantiate<AddressablesAudioClipProvider>()
                : DirectAudioClipProvider.Instance;
        }
    }
}
