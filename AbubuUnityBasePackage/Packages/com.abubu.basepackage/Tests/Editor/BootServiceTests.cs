using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Abubu.Boot;
using Abubu.SceneFlow;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    /// <summary>
    /// BootService のテスト。タスクは UniTaskCompletionSource で外から進めるため、フレームを待たずに同期的に検証できる。
    /// </summary>
    public sealed class BootServiceTests
    {
        private sealed class FakeTask : IBootTask
        {
            private readonly string _name;
            private readonly List<string> _log;
            public int Order { get; set; }
            public bool IsCritical { get; set; }
            public Exception Throw;
            /// <summary>設定するとこれの完了 (または ct のキャンセル) まで終わらない</summary>
            public UniTaskCompletionSource Gate;
            /// <summary>true なら ct を無視して Gate を待つ</summary>
            public bool IgnoreCancellation;
            public CancellationToken ReceivedToken;

            public FakeTask(string name, List<string> log, int order = 0)
            {
                _name = name;
                _log = log;
                Order = order;
            }

            public async UniTask RunAsync(CancellationToken ct)
            {
                ReceivedToken = ct;
                _log.Add(_name);
                if (Gate != null)
                {
                    if (IgnoreCancellation) await Gate.Task;
                    else await Gate.Task.AttachExternalCancellation(ct);
                }
                if (Throw != null) throw Throw;
            }
        }

        private sealed class FakeSceneService : ISceneService
        {
            private readonly List<string> _log;
            public FakeSceneService(List<string> log) => _log = log;

            public string CurrentSceneName { get; set; } = "Boot";
            public readonly List<string> Loaded = new();

            public ReadOnlyReactiveProperty<bool> IsLoading { get; } = new ReactiveProperty<bool>(false);
            public ReadOnlyReactiveProperty<bool> ShowLoadingScreen { get; } = new ReactiveProperty<bool>(false);
            public ReadOnlyReactiveProperty<float> Progress { get; } = new ReactiveProperty<float>(0f);
            public Observable<string> OnLoaded => Observable.Empty<string>();
            public Observable<Unit> OnBeforeSceneUnload => Observable.Empty<Unit>();

            public UniTask<bool> LoadAsync(string sceneName, SceneLoadOptions options = null, CancellationToken ct = default)
            {
                _log.Add("load:" + sceneName);
                Loaded.Add(sceneName);
                return UniTask.FromResult(true);
            }

            public UniTask<bool> LoadWithPayloadAsync<TPayload>(string sceneName, TPayload payload, SceneLoadOptions options = null, CancellationToken ct = default) =>
                LoadAsync(sceneName, options, ct);

            public UniTask<bool> ReloadAsync(SceneLoadOptions options = null, CancellationToken ct = default) => LoadAsync(CurrentSceneName, options, ct);
            public UniTask<bool> LoadAdditiveAsync(string sceneName, CancellationToken ct = default) => UniTask.FromResult(true);
            public UniTask<bool> UnloadAsync(string sceneName, CancellationToken ct = default) => UniTask.FromResult(true);

            public bool TryGetPayload<T>(out T payload)
            {
                payload = default;
                return false;
            }
        }

        private List<string> _log;
        private FakeSceneService _scene;
        private string _savedReturnScene;
        private readonly List<BootService> _created = new();

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _scene = new FakeSceneService(_log);
            // エディタ再生時の「元のシーンへ戻る」設定がテストに影響しないよう退避する
            _savedReturnScene = SessionState.GetString(BootService.EditorReturnSceneKey, string.Empty);
            SessionState.EraseString(BootService.EditorReturnSceneKey);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var boot in _created) boot.Dispose();
            _created.Clear();
            if (string.IsNullOrEmpty(_savedReturnScene)) SessionState.EraseString(BootService.EditorReturnSceneKey);
            else SessionState.SetString(BootService.EditorReturnSceneKey, _savedReturnScene);
        }

        private BootService Create(BootSettings settings, params IBootTask[] tasks)
        {
            var boot = new BootService(settings ?? new BootSettings(), _scene, new List<IBootTask>(tasks));
            _created.Add(boot);
            return boot;
        }

        /// <summary>完了済みの UniTask の結果 (例外) を取り出す</summary>
        private static Exception ResultOf(UniTask task)
        {
            Assert.That(task.Status, Is.Not.EqualTo(UniTaskStatus.Pending), "まだ完了していない");
            try
            {
                task.GetAwaiter().GetResult();
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        // ---- 実行順と状態 ----

        [Test]
        public void Tasks_RunInOrder_ThenReady()
        {
            var boot = Create(null,
                new FakeTask("c", _log, order: 10),
                new FakeTask("a", _log, order: -5),
                new FakeTask("b", _log, order: 0));
            Assert.That(boot.State.CurrentValue, Is.EqualTo(BootState.Running));

            boot.Initialize();

            Assert.That(_log, Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(boot.State.CurrentValue, Is.EqualTo(BootState.Ready));
            Assert.That(boot.IsReady.CurrentValue, Is.True);
            Assert.That(boot.Failures, Is.Empty);
        }

        [Test]
        public void NoTasks_BecomesReadyImmediately()
        {
            var boot = new BootService(null, null);
            _created.Add(boot);
            boot.Initialize();
            Assert.That(boot.State.CurrentValue, Is.EqualTo(BootState.Ready));
        }

        [Test]
        public void InitializeTwice_RunsTasksOnce()
        {
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "Title" }, new FakeTask("a", _log));
            boot.Initialize();
            boot.Initialize();
            Assert.That(_log, Is.EqualTo(new[] { "a", "load:Title" }));
        }

        [Test]
        public void TasksAwait_NextStartsOnlyAfterPreviousCompletes()
        {
            var first = new FakeTask("first", _log, order: 0) { Gate = new UniTaskCompletionSource() };
            var boot = Create(null, first, new FakeTask("second", _log, order: 1));

            boot.Initialize();
            Assert.That(_log, Is.EqualTo(new[] { "first" }));
            Assert.That(boot.IsReady.CurrentValue, Is.False);

            first.Gate.TrySetResult();
            Assert.That(_log, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(boot.IsReady.CurrentValue, Is.True);
        }

        [Test]
        public void NonCriticalFailure_IsRecorded_AndBootContinuesToReady()
        {
            var error = new InvalidOperationException("save-broken");
            var boot = Create(null,
                new FakeTask("a", _log, order: 0) { Throw = error },
                new FakeTask("b", _log, order: 1));

            LogAssert.Expect(LogType.Error, new Regex(@"起動処理 FakeTask が失敗しました。"));
            LogAssert.Expect(LogType.Exception, new Regex("save-broken"));
            boot.Initialize();

            Assert.That(_log, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(boot.State.CurrentValue, Is.EqualTo(BootState.Ready));
            Assert.That(boot.IsReady.CurrentValue, Is.True);
            Assert.That(boot.Failures, Is.EqualTo(new[] { error }));
            Assert.That(ResultOf(boot.WaitReadyAsync()), Is.Null);
        }

        [Test]
        public void CriticalFailure_StopsBoot_AndWaitReadyThrowsBootFailed()
        {
            var error = new InvalidOperationException("login-failed");
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "Title" },
                new FakeTask("a", _log, order: 0) { Throw = error, IsCritical = true },
                new FakeTask("b", _log, order: 1));
            var waiting = boot.WaitReadyAsync();

            LogAssert.Expect(LogType.Error, new Regex("重要な処理のため起動を中止します"));
            LogAssert.Expect(LogType.Exception, new Regex("login-failed"));
            boot.Initialize();

            Assert.That(_log, Is.EqualTo(new[] { "a" }), "以降のタスクは実行しない・遷移もしない");
            Assert.That(boot.State.CurrentValue, Is.EqualTo(BootState.Failed));
            Assert.That(boot.IsReady.CurrentValue, Is.False);
            Assert.That(boot.Failures, Is.EqualTo(new[] { error }));

            var waited = ResultOf(waiting);
            Assert.That(waited, Is.InstanceOf<BootFailedException>());
            Assert.That(waited.InnerException, Is.SameAs(error));
            // 失敗後に呼んでも同じ
            Assert.That(ResultOf(boot.WaitReadyAsync()), Is.InstanceOf<BootFailedException>());
        }

        // ---- WaitReadyAsync ----

        [Test]
        public void WaitReady_BeforeReady_CompletesWhenReady()
        {
            var task = new FakeTask("a", _log) { Gate = new UniTaskCompletionSource() };
            var boot = Create(null, task);
            var beforeInitialize = boot.WaitReadyAsync(); // Initialize 前から待てる
            boot.Initialize();
            var whileRunning = boot.WaitReadyAsync();

            Assert.That(beforeInitialize.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(whileRunning.Status, Is.EqualTo(UniTaskStatus.Pending));

            task.Gate.TrySetResult();

            Assert.That(ResultOf(beforeInitialize), Is.Null);
            Assert.That(ResultOf(whileRunning), Is.Null);
        }

        [Test]
        public void WaitReady_AfterReady_CompletesImmediately()
        {
            var boot = Create(null, new FakeTask("a", _log));
            boot.Initialize();

            var wait = boot.WaitReadyAsync();
            Assert.That(wait.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [Test]
        public void WaitReady_CancelledByCaller_ThrowsOperationCanceled_BootContinues()
        {
            var task = new FakeTask("a", _log) { Gate = new UniTaskCompletionSource() };
            var boot = Create(null, task);
            boot.Initialize();
            var cts = new CancellationTokenSource();
            var wait = boot.WaitReadyAsync(cts.Token);

            cts.Cancel();

            Assert.That(ResultOf(wait), Is.InstanceOf<OperationCanceledException>());
            Assert.That(task.ReceivedToken.IsCancellationRequested, Is.False, "呼び出し側のキャンセルは起動処理を止めない");
            task.Gate.TrySetResult();
            Assert.That(boot.IsReady.CurrentValue, Is.True);
        }

        // ---- 破棄 ----

        [Test]
        public void Dispose_CancelsRunningTask_AndWaitersGetOperationCanceled()
        {
            var task = new FakeTask("a", _log, order: 0) { Gate = new UniTaskCompletionSource() };
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "Title" },
                task, new FakeTask("b", _log, order: 1));
            boot.Initialize();
            var waiting = boot.WaitReadyAsync();

            boot.Dispose();
            boot.Dispose(); // 二重に呼んでも安全

            Assert.That(task.ReceivedToken.IsCancellationRequested, Is.True);
            // 以前は R3 の FirstAsync が InvalidOperationException (Sequence contains no elements) を投げていた
            Assert.That(ResultOf(waiting), Is.InstanceOf<OperationCanceledException>());
            Assert.That(ResultOf(boot.WaitReadyAsync()), Is.InstanceOf<OperationCanceledException>(), "破棄後の呼び出し");
            Assert.That(_log, Is.EqualTo(new[] { "a" }), "以降のタスクも遷移も実行しない");
            Assert.That(boot.Failures, Is.Empty, "破棄によるキャンセルは失敗に数えない");
        }

        [Test]
        public void Dispose_TaskIgnoringCancellation_DoesNotTouchDisposedState()
        {
            // ct を無視して後から完了するタスクがあっても、破棄済みの状態を書き換えたり遷移したりしない
            // (以前は破棄済みの ReactiveProperty に書き込んで ObjectDisposedException が未処理例外としてログに出ていた)
            var task = new FakeTask("a", _log) { Gate = new UniTaskCompletionSource(), IgnoreCancellation = true };
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "Title" }, task, new FakeTask("b", _log, order: 1));
            boot.Initialize();

            boot.Dispose();
            task.Gate.TrySetResult();

            Assert.That(_log, Is.EqualTo(new[] { "a" }));
            Assert.That(_scene.Loaded, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Dispose_TaskFailingAfterCancellation_IsLoggedButNotRecorded()
        {
            var task = new FakeTask("a", _log)
            {
                Gate = new UniTaskCompletionSource(),
                IgnoreCancellation = true,
                IsCritical = true,
                Throw = new InvalidOperationException("after-dispose"),
            };
            var boot = Create(null, task);
            boot.Initialize();
            boot.Dispose();

            LogAssert.Expect(LogType.Exception, new Regex("after-dispose"));
            task.Gate.TrySetResult();

            Assert.That(boot.Failures, Is.Empty);
        }

        [Test]
        public void Initialize_AfterDispose_DoesNothing()
        {
            var boot = Create(null, new FakeTask("a", _log));
            boot.Dispose();
            Assert.DoesNotThrow(boot.Initialize);
            Assert.That(_log, Is.Empty);
        }

        // ---- Boot シーンからの遷移 ----

        [Test]
        public void FromBootScene_NavigatesToFirstScene()
        {
            _scene.CurrentSceneName = "Boot";
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "Title" }, new FakeTask("a", _log));

            boot.Initialize();

            Assert.That(_scene.Loaded, Is.EqualTo(new[] { "Title" }));
        }

        [Test]
        public void FromOtherScene_DoesNotNavigate()
        {
            _scene.CurrentSceneName = "Game"; // Boot 以外のシーンから直接再生した
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "Title" });

            boot.Initialize();

            Assert.That(boot.IsReady.CurrentValue, Is.True);
            Assert.That(_scene.Loaded, Is.Empty);
        }

        [Test]
        public void NoBootSceneConfigured_DoesNotNavigate()
        {
            _scene.CurrentSceneName = "";
            var boot = Create(new BootSettings { BootSceneName = "", FirstSceneName = "Title" });
            boot.Initialize();
            Assert.That(_scene.Loaded, Is.Empty);
        }

        [Test]
        public void FirstSceneEmpty_DoesNotNavigate()
        {
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "" });
            boot.Initialize();
            Assert.That(boot.IsReady.CurrentValue, Is.True);
            Assert.That(_scene.Loaded, Is.Empty);
        }

        [Test]
        public void EditorReturnScene_TakesPriority_AndIsConsumed()
        {
            // エディタで別のシーンから再生した場合、Boot シーンを経由して元のシーンに戻る
            SessionState.SetString(BootService.EditorReturnSceneKey, "Assets/Scenes/Game.unity");
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "Title" });

            boot.Initialize();

            Assert.That(_scene.Loaded, Is.EqualTo(new[] { "Assets/Scenes/Game.unity" }));
            Assert.That(SessionState.GetString(BootService.EditorReturnSceneKey, string.Empty), Is.Empty, "一度使ったら消す");
        }

        [Test]
        public void WaitReady_ResumesAfterBootNavigationHasStarted()
        {
            // WaitReadyAsync の直後に LoadAsync したコードが、Boot シーンからの遷移と競合しないこと
            var task = new FakeTask("a", _log) { Gate = new UniTaskCompletionSource() };
            var boot = Create(new BootSettings { BootSceneName = "Boot", FirstSceneName = "Title" }, task);
            boot.Initialize();
            UniTask.Void(async () =>
            {
                await boot.WaitReadyAsync();
                _log.Add("waiter");
            });

            task.Gate.TrySetResult();

            Assert.That(_log, Is.EqualTo(new[] { "a", "load:Title", "waiter" }));
        }
    }
}
