using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Abubu.SceneFlow;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace Abubu.Boot
{
    [Serializable]
    public sealed class BootSettings
    {
        [Tooltip("任意。起動専用シーン (ロゴ・初期化用)。空ならどのシーンからでもそのまま始まる")]
        public string BootSceneName;
        [Tooltip("Boot シーンから通常起動した場合に、初期化後に遷移するシーン (タイトルなど)")]
        public string FirstSceneName;
        [Tooltip("エディタで再生したとき、開いているシーンに関係なく Boot シーンから起動し、初期化後に元のシーンへ戻る")]
        public bool EditorStartFromBootScene = true;
    }

    /// <summary>
    /// 起動時に一度だけ実行する初期化処理。Installer で Container.Bind&lt;IBootTask&gt;().To&lt;MyTask&gt;().AsSingle() と登録する。
    /// (例: セーブデータ読み込み、Addressables のカタログ更新、ログイン処理)
    /// <code>
    /// public sealed class LoadSaveTask : IBootTask
    /// {
    ///     public int Order => 0;
    ///     public bool IsCritical => true; // 失敗したら起動を中止する (既定は false)
    ///     public async UniTask RunAsync(CancellationToken ct) => await _save.LoadAsync(ct);
    /// }
    /// </code>
    /// </summary>
    public interface IBootTask
    {
        /// <summary>小さい順に実行される</summary>
        int Order { get; }

        /// <summary>
        /// true にすると、この処理が失敗 (例外) したら起動を中止する: 以降のタスクは実行せず、IsReady は true にならず、
        /// <see cref="IBootService.WaitReadyAsync"/> は <see cref="BootFailedException"/> を投げる。
        /// 既定は false (失敗をログと <see cref="IBootService.Failures"/> に残して次へ進む)。
        /// </summary>
        bool IsCritical => false;

        UniTask RunAsync(CancellationToken ct);
    }

    /// <summary>起動処理の状態</summary>
    public enum BootState
    {
        /// <summary>IBootTask を実行中 (または開始前)</summary>
        Running,
        /// <summary>すべての IBootTask が終わった (重要でない失敗があっても Ready になる。<see cref="IBootService.Failures"/> 参照)</summary>
        Ready,
        /// <summary>IsCritical な IBootTask が失敗し、起動を中止した</summary>
        Failed,
    }

    /// <summary>重要な (IsCritical な) 起動処理が失敗したことを表す例外</summary>
    public sealed class BootFailedException : Exception
    {
        public BootFailedException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// 起動処理 (IBootTask の実行) の状態を公開する。
    /// <code>
    /// [Inject] private IBootService _boot;
    /// private async UniTaskVoid Start()
    /// {
    ///     await _boot.WaitReadyAsync(destroyCancellationToken); // 起動処理が終わるまで待つ
    ///     if (_boot.Failures.Count &gt; 0) { /* 重要でない失敗があった */ }
    /// }
    /// </code>
    /// </summary>
    /// <remarks>
    /// 起動処理は ProjectContext の MonoKernel.Start (= ProjectContext の GameObject の Start。最初のシーンの Awake より後) で
    /// IInitializable として始まり、非同期に進む。そのため最初のシーンの Start 時点ではまだ終わっていないことがある。
    /// シーン側のコードで IBootTask の結果 (セーブデータなど) を使う場合や、ISceneService.LoadAsync で遷移する場合は、
    /// 先に <see cref="WaitReadyAsync"/> を await すること。
    /// </remarks>
    public interface IBootService
    {
        /// <summary>すべての IBootTask が終わったら true (重要でない失敗があっても true になる)</summary>
        ReadOnlyReactiveProperty<bool> IsReady { get; }

        /// <summary>起動処理の状態</summary>
        ReadOnlyReactiveProperty<BootState> State { get; }

        /// <summary>失敗した IBootTask の例外 (実行順)。失敗が無ければ空</summary>
        IReadOnlyList<Exception> Failures { get; }

        /// <summary>
        /// すべての IBootTask が終わるまで待つ。シーン側の Start で await すると安全。
        /// </summary>
        /// <exception cref="BootFailedException">IsCritical な IBootTask が失敗して起動が中止された</exception>
        /// <exception cref="OperationCanceledException">ct がキャンセルされた、または BootService が破棄された</exception>
        UniTask WaitReadyAsync(CancellationToken ct = default);
    }

    /// <summary><see cref="IBootService"/> の標準実装</summary>
    /// <remarks>
    /// Initialize は ProjectContext の MonoKernel.Start で呼ばれる (ProjectContext.Instance の生成時ではない)。
    /// AbubuInstaller は他サービスの Initialize がすべて終わった後に実行されるよう、実行順を最後に指定している。
    /// すべてのタスクが終わり、Boot シーンから起動していた場合は FirstSceneName (エディタでは元のシーン) へ遷移する。
    /// </remarks>
    public sealed class BootService : IBootService, IInitializable, IDisposable
    {
        /// <summary>エディタ再生時に戻るシーン名を受け渡すキー (Abubu.Editor 側と共有)</summary>
        public const string EditorReturnSceneKey = "Abubu.Boot.ReturnScene";

        private readonly BootSettings _settings;
        private readonly List<IBootTask> _tasks;
        private readonly ISceneService _scene;
        private readonly ReactiveProperty<bool> _isReady = new(false);
        private readonly ReactiveProperty<BootState> _state = new(BootState.Running);
        private readonly List<Exception> _failures = new();
        private readonly CancellationTokenSource _cts = new();
        /// <summary>Ready / Failed で完了、Dispose でキャンセルされる (WaitReadyAsync の待ち合わせ用)</summary>
        private readonly UniTaskCompletionSource _finished = new();
        private Exception _criticalFailure;
        private bool _disposed;
        private bool _started;

        public ReadOnlyReactiveProperty<bool> IsReady => _isReady;
        public ReadOnlyReactiveProperty<BootState> State => _state;
        public IReadOnlyList<Exception> Failures => _failures;

        /// <param name="settings">設定</param>
        /// <param name="scene">Boot シーンからの遷移に使う。null なら遷移しない</param>
        /// <param name="tasks">起動時に実行する処理 (登録が無ければ null で良い)</param>
        public BootService(BootSettings settings, ISceneService scene, List<IBootTask> tasks = null)
        {
            _settings = settings ?? new BootSettings();
            _scene = scene;
            _tasks = tasks ?? new List<IBootTask>();
        }

        public void Initialize()
        {
            // 二重に呼ばれてもタスクを 2 回実行しない
            if (_disposed || _started) return;
            _started = true;
            RunAsync(_cts.Token).Forget();
        }

        /// <remarks>
        /// 待っている間に BootService が破棄された (再生終了など) 場合や、破棄後に呼んだ場合は OperationCanceledException を投げる。
        /// </remarks>
        public async UniTask WaitReadyAsync(CancellationToken ct = default)
        {
            // R3 の FirstAsync は購読元が Dispose されると InvalidOperationException になるため、専用の完了通知で待つ
            if (_disposed) throw new OperationCanceledException("[Abubu.Boot] BootService は破棄されています。");
            await _finished.Task.AttachExternalCancellation(ct);
            if (_state.Value == BootState.Failed)
            {
                throw new BootFailedException("[Abubu.Boot] 重要な起動処理が失敗したため、起動を中止しました。", _criticalFailure);
            }
        }

        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            foreach (var task in _tasks.OrderBy(t => t.Order))
            {
                try
                {
                    await task.RunAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return; // 破棄された (再生終了など)
                }
                catch (Exception e)
                {
                    if (ct.IsCancellationRequested)
                    {
                        // 破棄に伴う失敗。状態は書き換えずにログだけ残す
                        Debug.LogException(e);
                        return;
                    }
                    _failures.Add(e);
                    Debug.LogError($"[Abubu.Boot] 起動処理 {task.GetType().Name} が失敗しました{(task.IsCritical ? " (重要な処理のため起動を中止します)" : "")}。");
                    Debug.LogException(e);
                    if (task.IsCritical)
                    {
                        _criticalFailure = e;
                        _state.Value = BootState.Failed;
                        _finished.TrySetResult();
                        return;
                    }
                    // 重要でない処理の失敗ではゲーム全体を止めない (Failures で確認できる)
                }

                // ct を無視して完了したタスクがあっても、破棄後は状態を書き換えない・遷移しない
                if (ct.IsCancellationRequested) return;
            }

            _isReady.Value = true;
            _state.Value = BootState.Ready;

            // 先に遷移を始めてから待機中の WaitReadyAsync を再開させる
            // (WaitReadyAsync の直後に LoadAsync した場合に、Boot シーンからの遷移と競合させない)
            var next = GetNextScene();
            var navigation = string.IsNullOrEmpty(next) || _scene == null ? UniTask.FromResult(true) : _scene.LoadAsync(next, ct: ct);
            _finished.TrySetResult();
            try
            {
                await navigation;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        /// <summary>Boot シーンから起動していた場合に、起動処理の後で遷移するシーン。遷移しないなら null</summary>
        private string GetNextScene()
        {
            if (string.IsNullOrEmpty(_settings.BootSceneName)) return null;
            var current = _scene != null ? _scene.CurrentSceneName : SceneManager.GetActiveScene().name;
            if (current != _settings.BootSceneName) return null;

#if UNITY_EDITOR
            var returnScene = UnityEditor.SessionState.GetString(EditorReturnSceneKey, string.Empty);
            UnityEditor.SessionState.EraseString(EditorReturnSceneKey);
            if (!string.IsNullOrEmpty(returnScene)) return returnScene;
#endif
            return _settings.FirstSceneName;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
            _finished.TrySetCanceled();
            _isReady.Dispose();
            _state.Dispose();
        }
    }

    /// <summary>
    /// どのシーンから再生しても、最初のシーンがロードされる前に ProjectContext (= 全サービス) を生成する。
    /// これにより「このシーンから直接再生したら落ちる」を防ぐ。
    /// </summary>
    /// <remarks>
    /// ・Resources/ProjectContext に AbubuInstaller (または <see cref="IAbubuProjectInstaller"/> を付けた Installer) が
    ///   含まれている場合だけ生成する。Abubu と無関係な ProjectContext には何もしない (Zenject 通常どおり最初の SceneContext で生成される)。
    /// ・スクリプティングシンボル ABUBU_DISABLE_AUTO_BOOTSTRAP を定義すると、この自動生成を完全に無効にできる。
    /// ・ここで行うのは生成と Install まで。IInitializable (BootService の起動処理など) は ProjectContext の Start で実行される。
    /// </remarks>
    public static class AbubuBootstrap
    {
#if !ABUBU_DISABLE_AUTO_BOOTSTRAP
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
        private static void Initialize()
        {
            if (ProjectContext.HasInstance) return;

            var prefab = ProjectContext.TryGetPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[Abubu] Resources/ProjectContext.prefab がありません。Tools > Abubu > Setup Project を実行してください " +
                                 "(Abubu の自動起動が不要な場合はスクリプティングシンボル ABUBU_DISABLE_AUTO_BOOTSTRAP を定義してください)。");
                return;
            }

            // Abubu と関係のない ProjectContext を、このパッケージの都合で早く生成しない
            if (!AbubuProjectContextInspector.ContainsAbubuInstaller(prefab)) return;

            // 生成と同時に Installer が実行される (IInitializable は ProjectContext の Start で呼ばれる)
            _ = ProjectContext.Instance;
        }
    }
}
