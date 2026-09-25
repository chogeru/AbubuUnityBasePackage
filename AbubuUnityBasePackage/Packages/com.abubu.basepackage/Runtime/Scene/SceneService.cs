using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace Abubu.Scene
{
    /// <summary>
    /// シーン遷移の本体。ロード画面の表示は担当せず、IsLoading / Progress / ShowLoadingScreen を公開するだけ
    /// (表示は Presentation 層の SceneLoadingScreen が購読して行う)。
    /// </summary>
    public sealed class SceneService : ISceneService, IDisposable
    {
        private readonly SceneSettings _settings;
        private readonly ISceneTransition _transition;
        private readonly ReactiveProperty<bool> _isLoading = new(false);
        private readonly ReactiveProperty<bool> _showLoadingScreen = new(false);
        private readonly ReactiveProperty<float> _progress = new(0f);
        private readonly Subject<string> _onLoaded = new();
        private readonly Subject<Unit> _onBeforeSceneUnload = new();

        private object _payload;
        private Action<DiContainer> _payloadBindings;

        public ReadOnlyReactiveProperty<bool> IsLoading => _isLoading;
        public ReadOnlyReactiveProperty<bool> ShowLoadingScreen => _showLoadingScreen;
        public ReadOnlyReactiveProperty<float> Progress => _progress;
        public Observable<string> OnLoaded => _onLoaded;
        public Observable<Unit> OnBeforeSceneUnload => _onBeforeSceneUnload;
        public string CurrentSceneName => SceneManager.GetActiveScene().name;

        public SceneService(SceneSettings settings, ISceneTransition transition)
        {
            _settings = settings;
            _transition = transition;
        }

        public UniTask LoadAsync(string sceneName, SceneLoadOptions options = null, CancellationToken ct = default) =>
            LoadInternalAsync(sceneName, null, null, options, ct);

        public UniTask LoadWithPayloadAsync<TPayload>(string sceneName, TPayload payload, SceneLoadOptions options = null, CancellationToken ct = default) =>
            // 遷移先の SceneContext に payload をバインドし、[Inject] で受け取れるようにする
            LoadInternalAsync(sceneName, payload, c => c.Bind<TPayload>().FromInstance(payload).AsSingle(), options, ct);

        // 前回のデータとバインドをそのまま引き継ぐ (リロード後も [Inject] TPayload が解決できるように)
        public UniTask ReloadAsync(SceneLoadOptions options = null, CancellationToken ct = default) =>
            LoadInternalAsync(CurrentSceneName, _payload, _payloadBindings, options, ct);

        public async UniTask LoadAdditiveAsync(string sceneName, CancellationToken ct = default)
        {
            if (SceneManager.GetSceneByName(sceneName).isLoaded) return;
            await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive).ToUniTask(cancellationToken: ct);
        }

        public async UniTask UnloadAsync(string sceneName, CancellationToken ct = default)
        {
            if (!SceneManager.GetSceneByName(sceneName).isLoaded) return;
            await SceneManager.UnloadSceneAsync(sceneName).ToUniTask(cancellationToken: ct);
        }

        public bool TryGetPayload<T>(out T payload)
        {
            if (_payload is T typed)
            {
                payload = typed;
                return true;
            }

            payload = default;
            return false;
        }

        private async UniTask LoadInternalAsync(string sceneName, object payload, Action<DiContainer> payloadBindings,
            SceneLoadOptions options, CancellationToken ct)
        {
            if (_isLoading.Value)
            {
                Debug.LogWarning($"[Abubu.Scene] ロード中のため \"{sceneName}\" への遷移要求を無視しました。");
                return;
            }
            if (!CanLoad(sceneName))
            {
                Debug.LogError($"[Abubu.Scene] シーン \"{sceneName}\" が見つかりません。Build Profiles (Build Settings) に追加されているか確認してください。");
                return;
            }

            var resolved = SceneLoadOptions.Resolve(options, _settings);
            _isLoading.Value = true;
            _progress.Value = 0f;

            try
            {
                // キャンセルを受け付けるのはフェードアウト完了まで。ここで止まった場合は元のシーンに留まる
                await _transition.FadeOutAsync(resolved.FadeOutDuration, resolved.FadeColor, ct);
                if (ct.IsCancellationRequested) return;

                // ここから先は ct を渡さない。allowSceneActivation=false のまま放置すると
                // 以降の LoadSceneAsync がすべて詰まってしまうため、必ず最後まで流す
                _showLoadingScreen.Value = resolved.ShowLoadingView;
                var startTime = Time.realtimeSinceStartup;
                var operation = StartLoad(sceneName);
                operation.allowSceneActivation = false;

                while (operation.progress < 0.9f)
                {
                    _progress.Value = operation.progress / 0.9f;
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
                _progress.Value = 1f;

                var remaining = resolved.MinimumLoadingTime - (Time.realtimeSinceStartup - startTime);
                if (remaining > 0f) await UniTask.Delay(TimeSpan.FromSeconds(remaining), ignoreTimeScale: true);

                // 旧シーンのオブジェクトが親ごと破棄される前に通知する (プール回収など)
                _onBeforeSceneUnload.OnNext(Unit.Default);

                _payload = payload;
                _payloadBindings = payloadBindings;
                SceneContext.ExtraBindingsInstallMethod = payloadBindings;
                operation.allowSceneActivation = true;
                await operation.ToUniTask();
                // 遷移先に SceneContext が無かった場合に次のシーンへ持ち越さないようにする
                SceneContext.ExtraBindingsInstallMethod = null;

                // 遷移先の Start が走ってからフェードインする
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            finally
            {
                _showLoadingScreen.Value = false;
                await _transition.FadeInAsync(resolved.FadeInDuration, CancellationToken.None);
                _isLoading.Value = false;
            }

            _onLoaded.OnNext(sceneName);
        }

        private static bool CanLoad(string scene)
        {
#if UNITY_EDITOR
            if (IsEditorScenePath(scene)) return System.IO.File.Exists(scene);
#endif
            return Application.CanStreamedLevelBeLoaded(scene);
        }

        private static AsyncOperation StartLoad(string scene)
        {
#if UNITY_EDITOR
            // Build Settings 未登録のシーン (テスト用シーンなど) はパス指定でエディタ専用 API から開く
            if (IsEditorScenePath(scene))
            {
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    scene, new LoadSceneParameters(LoadSceneMode.Single));
            }
#endif
            return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
        }

#if UNITY_EDITOR
        private static bool IsEditorScenePath(string scene) => scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
#endif

        public void Dispose()
        {
            _onBeforeSceneUnload.Dispose();
            _isLoading.Dispose();
            _showLoadingScreen.Dispose();
            _progress.Dispose();
            _onLoaded.Dispose();
        }
    }
}
