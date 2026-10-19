using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.SceneFlow
{
    /// <summary>
    /// シーン遷移の本体。ロード画面の表示は担当せず、IsLoading / Progress / ShowLoadingScreen を公開するだけ
    /// (表示は Presentation 層の SceneLoadingScreen が購読して行う)。
    /// 遷移の順序・キャンセル・拒否の扱いは <see cref="ISceneService"/> を参照。
    /// </summary>
    /// <remarks>
    /// 遷移先へのデータ受け渡しには Zenject の SceneContext.ExtraBindingsInstallMethod (static) を使う。
    /// データがある遷移の間だけ設定し、終わったら (例外時も) 元の値に戻すので、ZenjectSceneLoader など他の利用者とは干渉しにくい。
    /// ただし同じタイミングで ZenjectSceneLoader によるロードが並行している場合は、そちらの設定を一時的に置き換える (警告を出す)。
    /// </remarks>
    public sealed class SceneService : ISceneService, IDisposable
    {
        private readonly SceneSettings _settings;
        private readonly ISceneTransition _transition;
        private readonly ISceneLoader _loader;
        private readonly List<ISceneTransitionHook> _hooks;
        private readonly ReactiveProperty<bool> _isLoading = new(false);
        private readonly ReactiveProperty<bool> _showLoadingScreen = new(false);
        private readonly ReactiveProperty<float> _progress = new(0f);
        private readonly Subject<string> _onLoaded = new();
        private readonly Subject<Unit> _onBeforeSceneUnload = new();
        private readonly CancellationTokenSource _disposeCts = new();

        /// <summary>追加読み込み / 破棄の処理中のシーン名 (同じシーンへの同時操作を拒否するため)</summary>
        private readonly HashSet<string> _pendingAdditive = new();

        private object _payload;
        private Action<DiContainer> _payloadBindings;
        private bool _disposed;

        public ReadOnlyReactiveProperty<bool> IsLoading => _isLoading;
        public ReadOnlyReactiveProperty<bool> ShowLoadingScreen => _showLoadingScreen;
        public ReadOnlyReactiveProperty<float> Progress => _progress;
        public Observable<string> OnLoaded => _onLoaded;
        public Observable<Unit> OnBeforeSceneUnload => _onBeforeSceneUnload;
        public string CurrentSceneName => _loader.ActiveSceneName;

        /// <param name="settings">設定</param>
        /// <param name="transition">遷移演出</param>
        /// <param name="hooks">遷移前後に await する処理 (登録が無ければ null で良い)</param>
        [Inject]
        public SceneService(SceneSettings settings, ISceneTransition transition, List<ISceneTransitionHook> hooks = null)
            : this(settings, transition, hooks, UnitySceneLoader.Instance)
        {
        }

        /// <summary>テスト用。SceneManager の代わりに loader を使う</summary>
        internal SceneService(SceneSettings settings, ISceneTransition transition, List<ISceneTransitionHook> hooks, ISceneLoader loader)
        {
            _settings = settings ?? new SceneSettings();
            _transition = transition ?? throw new ArgumentNullException(nameof(transition));
            _hooks = hooks ?? new List<ISceneTransitionHook>();
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public UniTask<bool> LoadAsync(string sceneName, SceneLoadOptions options = null, CancellationToken ct = default) =>
            LoadInternalAsync(sceneName, null, null, options, ct);

        public UniTask<bool> LoadWithPayloadAsync<TPayload>(string sceneName, TPayload payload, SceneLoadOptions options = null, CancellationToken ct = default) =>
            // 遷移先の SceneContext に payload をバインドし、[Inject] で受け取れるようにする
            LoadInternalAsync(sceneName, payload, c => c.Bind<TPayload>().FromInstance(payload).AsSingle(), options, ct);

        // 前回のデータとバインドをそのまま引き継ぐ (リロード後も [Inject] TPayload が解決できるように)
        public UniTask<bool> ReloadAsync(SceneLoadOptions options = null, CancellationToken ct = default) =>
            LoadInternalAsync(CurrentSceneName, _payload, _payloadBindings, options, ct);

        public async UniTask<bool> LoadAdditiveAsync(string sceneName, CancellationToken ct = default)
        {
            if (!CanStartAdditiveOperation(sceneName, "追加読み込み")) return false;
            if (_loader.IsLoaded(sceneName)) return true;
            if (!_loader.CanLoad(sceneName))
            {
                LogSceneNotFound(sceneName);
                return false;
            }

            var operation = _loader.LoadAdditive(sceneName);
            return await TrackAdditiveAsync(sceneName, operation, "追加読み込み", ct);
        }

        public async UniTask<bool> UnloadAsync(string sceneName, CancellationToken ct = default)
        {
            if (!CanStartAdditiveOperation(sceneName, "破棄")) return false;
            if (!_loader.IsLoaded(sceneName)) return true;

            var operation = _loader.Unload(sceneName);
            return await TrackAdditiveAsync(sceneName, operation, "破棄", ct);
        }

        /// <summary>追加読み込み / 破棄を始めてよいか。だめならログを出して false</summary>
        private bool CanStartAdditiveOperation(string sceneName, string what)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"[Abubu.Scene] シーン名が空のため{what}できません。");
                return false;
            }
            if (_isLoading.Value)
            {
                // シーン遷移中 (allowSceneActivation = false の間) に始めた非同期ロードは遷移が終わるまで進まないため受け付けない
                Debug.LogWarning($"[Abubu.Scene] シーン遷移中のため \"{sceneName}\" の{what}を受け付けませんでした。");
                return false;
            }
            if (_pendingAdditive.Contains(sceneName))
            {
                Debug.LogWarning($"[Abubu.Scene] \"{sceneName}\" は追加読み込み / 破棄の処理中のため、{what}を受け付けませんでした。");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 追加読み込み / 破棄の完了を待つ。処理中の印は ct でのキャンセルに関係なく、Unity 側の処理が終わった時点で外す
        /// </summary>
        private async UniTask<bool> TrackAdditiveAsync(string sceneName, ISceneOperation operation, string what, CancellationToken ct)
        {
            if (operation == null)
            {
                Debug.LogError($"[Abubu.Scene] \"{sceneName}\" の{what}を開始できませんでした (最後の 1 シーンは破棄できません)。");
                return false;
            }

            _pendingAdditive.Add(sceneName);
            operation.OnCompleted(() => _pendingAdditive.Remove(sceneName));
            await operation.WaitAsync(ct);
            return true;
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

        private async UniTask<bool> LoadInternalAsync(string sceneName, object payload, Action<DiContainer> payloadBindings,
            SceneLoadOptions options, CancellationToken ct)
        {
            if (_disposed) return false;
            if (_isLoading.Value)
            {
                Debug.LogWarning($"[Abubu.Scene] ロード中のため \"{sceneName}\" への遷移要求を受け付けませんでした。");
                return false;
            }
            if (string.IsNullOrEmpty(sceneName) || !_loader.CanLoad(sceneName))
            {
                LogSceneNotFound(sceneName);
                return false;
            }
            ct.ThrowIfCancellationRequested();

            var resolved = SceneLoadOptions.Resolve(options, _settings);
            var from = CurrentSceneName;
            var lifetime = _disposeCts.Token; // Dispose 後に Token を読むと例外になるため先に取っておく
            _isLoading.Value = true;
            _progress.Value = 0f;

            // 読み込み開始前の区間は、呼び出し側の ct に加えて SceneService の破棄でも中止する
            // (破棄後に新しいシーンを読み込み始めない)
            using var beforeLoadCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
            var beforeLoad = beforeLoadCts.Token;

            try
            {
                // キャンセルを受け付けるのは読み込み開始前まで。ここで止まった場合は元のシーンに留まる (finally でフェードイン)
                await _transition.FadeOutAsync(resolved.FadeOutDuration, resolved.FadeColor, beforeLoad);
                beforeLoad.ThrowIfCancellationRequested();
                await RunHooksAsync(from, sceneName, before: true, beforeLoad);
                beforeLoad.ThrowIfCancellationRequested();

                // ここから先は ct を渡さない。allowSceneActivation=false のまま放置すると
                // 以降の LoadSceneAsync がすべて詰まってしまうため、必ず最後まで流す
                // (途中で Dispose された場合も読み込みは最後まで進め、通知だけ止める)
                if (!_disposed) _showLoadingScreen.Value = resolved.ShowLoadingView;
                var startTime = Time.realtimeSinceStartup;
                var operation = _loader.LoadSingle(sceneName) ??
                                throw new InvalidOperationException($"[Abubu.Scene] \"{sceneName}\" の読み込みを開始できませんでした。");

                while (operation.Progress < 0.9f)
                {
                    if (!_disposed) _progress.Value = operation.Progress / 0.9f;
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
                if (!_disposed) _progress.Value = 1f;

                var remaining = resolved.MinimumLoadingTime - (Time.realtimeSinceStartup - startTime);
                if (remaining > 0f) await UniTask.Delay(TimeSpan.FromSeconds(remaining), ignoreTimeScale: true);

                // 旧シーンのオブジェクトが親ごと破棄される前に通知する (プール回収など)
                if (!_disposed) _onBeforeSceneUnload.OnNext(Unit.Default);

                _payload = payload;
                _payloadBindings = payloadBindings;
                await ActivateAsync(operation, payloadBindings);

                // 遷移先の Start が走ってからフェードインする
                await UniTask.Yield(PlayerLoopTiming.Update);
                await RunHooksAsync(from, sceneName, before: false, lifetime);
            }
            finally
            {
                if (!_disposed)
                {
                    _showLoadingScreen.Value = false;
                    try
                    {
                        await _transition.FadeInAsync(resolved.FadeInDuration, CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                    // フェードイン中に Dispose された場合に備えてもう一度確認する
                    if (!_disposed) _isLoading.Value = false;
                }
            }

            if (!_disposed) _onLoaded.OnNext(sceneName);
            return true;
        }

        /// <summary>
        /// シーンをアクティベートする。遷移先に渡すデータがある場合だけ SceneContext.ExtraBindingsInstallMethod を設定し、
        /// 終わったら (遷移先に SceneContext が無く使われなかった場合や例外時も) 元の値に戻す
        /// </summary>
        private static async UniTask ActivateAsync(ISceneOperation operation, Action<DiContainer> payloadBindings)
        {
            if (payloadBindings == null)
            {
                operation.AllowActivation();
                await operation.WaitAsync(CancellationToken.None);
                return;
            }

            var previous = SceneContext.ExtraBindingsInstallMethod;
            if (previous != null)
            {
                Debug.LogWarning("[Abubu.Scene] SceneContext.ExtraBindingsInstallMethod が他の処理 (ZenjectSceneLoader など) によって設定済みのため、" +
                                 "この遷移の間だけ置き換えます。");
            }
            SceneContext.ExtraBindingsInstallMethod = payloadBindings;
            try
            {
                operation.AllowActivation();
                await operation.WaitAsync(CancellationToken.None);
            }
            finally
            {
                // SceneContext が使うと null に戻している。まだ自分の値が残っていたら (使われなかったら) 元に戻す
                if (ReferenceEquals(SceneContext.ExtraBindingsInstallMethod, payloadBindings))
                {
                    SceneContext.ExtraBindingsInstallMethod = previous;
                }
            }
        }

        /// <summary>フックを登録順に await する。例外はログに出して続行 (遷移前のキャンセルだけは中止として投げ直す)</summary>
        private async UniTask RunHooksAsync(string from, string to, bool before, CancellationToken ct)
        {
            foreach (var hook in _hooks)
            {
                try
                {
                    if (before) await hook.OnBeforeUnloadAsync(from, to, ct);
                    else await hook.OnAfterLoadAsync(from, to, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    if (before) throw;
                    return; // 破棄中。残りのフックも呼ばない
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Abubu.Scene] {hook.GetType().Name}.{(before ? nameof(ISceneTransitionHook.OnBeforeUnloadAsync) : nameof(ISceneTransitionHook.OnAfterLoadAsync))} で例外が発生しました (遷移は続行します)。");
                    Debug.LogException(e);
                }
            }
        }

        private static void LogSceneNotFound(string sceneName) =>
            Debug.LogError($"[Abubu.Scene] シーン \"{sceneName}\" が見つかりません。Build Profiles (Build Settings) に追加されているか確認してください。");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disposeCts.Cancel();
            _disposeCts.Dispose();
            _onBeforeSceneUnload.Dispose();
            _isLoading.Dispose();
            _showLoadingScreen.Dispose();
            _progress.Dispose();
            _onLoaded.Dispose();
        }
    }
}
