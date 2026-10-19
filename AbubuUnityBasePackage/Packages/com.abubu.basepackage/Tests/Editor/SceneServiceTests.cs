using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Abubu.SceneFlow;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;

namespace Abubu.Tests
{
    /// <summary>
    /// SceneService の遷移手順のテスト。SceneManager の代わりに偽の ISceneLoader、
    /// フェードの代わりに偽の ISceneTransition を使うため、EditMode で UI もシーンも作らずに動く。
    /// </summary>
    public sealed class SceneServiceTests
    {
        // ---- 偽物 ----

        private sealed class FakeOperation : ISceneOperation
        {
            private readonly UniTaskCompletionSource _done = new();
            private readonly List<Action> _callbacks = new();
            private readonly Action _onActivate;
            private readonly bool _completeOnActivation;

            public FakeOperation(Action onActivate, bool completeOnActivation)
            {
                _onActivate = onActivate;
                _completeOnActivation = completeOnActivation;
            }

            public float Progress { get; set; } = 1f;
            public bool IsDone { get; private set; }

            public void AllowActivation()
            {
                _onActivate?.Invoke();
                if (_completeOnActivation) Complete();
            }

            public void Complete()
            {
                if (IsDone) return;
                IsDone = true;
                foreach (var callback in _callbacks) callback();
                _callbacks.Clear();
                _done.TrySetResult();
            }

            public void OnCompleted(Action callback)
            {
                if (IsDone) callback();
                else _callbacks.Add(callback);
            }

            public UniTask WaitAsync(CancellationToken ct) => _done.Task.AttachExternalCancellation(ct);
        }

        private sealed class FakeLoader : ISceneLoader
        {
            private readonly List<string> _log;
            public readonly HashSet<string> Available = new() { "Title", "Game", "Overlay", "Hud" };
            public readonly HashSet<string> Loaded = new() { "Title" };
            public readonly List<FakeOperation> AdditiveOperations = new();
            public FakeOperation LastSingle;
            public int SingleLoadCount;
            /// <summary>アクティベート時に呼ばれる (ExtraBindingsInstallMethod の確認や例外の注入に使う)</summary>
            public Action OnActivate;
            /// <summary>読み込み開始時の進捗 (1 未満にすると進捗待ちのループに入る)</summary>
            public float InitialProgress = 1f;
            public bool FailToStart;

            public FakeLoader(List<string> log) => _log = log;

            public string ActiveSceneName { get; set; } = "Title";
            public bool CanLoad(string scene) => Available.Contains(scene);
            public bool IsLoaded(string scene) => Loaded.Contains(scene);

            public ISceneOperation LoadSingle(string scene)
            {
                SingleLoadCount++;
                if (FailToStart) return null;
                LastSingle = new FakeOperation(() =>
                {
                    _log.Add("activate:" + scene);
                    OnActivate?.Invoke();
                    ActiveSceneName = scene;
                    Loaded.Clear();
                    Loaded.Add(scene);
                }, completeOnActivation: true) { Progress = InitialProgress };
                return LastSingle;
            }

            public ISceneOperation LoadAdditive(string scene)
            {
                var op = new FakeOperation(null, false);
                op.OnCompleted(() => Loaded.Add(scene));
                AdditiveOperations.Add(op);
                return op;
            }

            public ISceneOperation Unload(string scene)
            {
                if (Loaded.Count <= 1) return null; // 最後の 1 シーンは破棄できない (Unity と同じ)
                var op = new FakeOperation(null, false);
                op.OnCompleted(() => Loaded.Remove(scene));
                AdditiveOperations.Add(op);
                return op;
            }
        }

        private sealed class FakeTransition : ISceneTransition
        {
            private readonly List<string> _log;
            /// <summary>設定するとフェードアウトがこれの完了まで終わらない</summary>
            public UniTaskCompletionSource FadeOutGate;
            public int FadeOutCount;
            public int FadeInCount;

            public FakeTransition(List<string> log) => _log = log;

            public async UniTask FadeOutAsync(float duration, Color color, CancellationToken ct)
            {
                FadeOutCount++;
                _log.Add("fadeOut");
                // FadeTransition と同じく、キャンセルされても例外は投げずに戻る (中止の判断は SceneService が行う)
                if (FadeOutGate != null) await FadeOutGate.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
            }

            public UniTask FadeInAsync(float duration, CancellationToken ct)
            {
                FadeInCount++;
                _log.Add("fadeIn");
                return UniTask.CompletedTask;
            }
        }

        private sealed class RecordingHook : ISceneTransitionHook
        {
            private readonly string _name;
            private readonly List<string> _log;
            public Exception ThrowBefore;
            public Exception ThrowAfter;
            /// <summary>設定すると OnBeforeUnloadAsync がこれの完了 (または ct のキャンセル) まで終わらない</summary>
            public UniTaskCompletionSource BeforeGate;

            public RecordingHook(string name, List<string> log)
            {
                _name = name;
                _log = log;
            }

            public async UniTask OnBeforeUnloadAsync(string from, string to, CancellationToken ct)
            {
                _log.Add($"{_name}.before:{from}->{to}");
                if (BeforeGate != null) await BeforeGate.Task.AttachExternalCancellation(ct);
                if (ThrowBefore != null) throw ThrowBefore;
            }

            public UniTask OnAfterLoadAsync(string from, string to, CancellationToken ct)
            {
                _log.Add($"{_name}.after:{from}->{to}");
                if (ThrowAfter != null) throw ThrowAfter;
                return UniTask.CompletedTask;
            }
        }

        private sealed class Payload
        {
            public int Value;
        }

        // ---- 準備 ----

        private List<string> _log;
        private FakeLoader _loader;
        private FakeTransition _transition;
        private List<ISceneTransitionHook> _hooks;
        private SceneService _service;
        private Action<DiContainer> _savedExtraBindings;

        [SetUp]
        public void SetUp()
        {
            _savedExtraBindings = SceneContext.ExtraBindingsInstallMethod;
            SceneContext.ExtraBindingsInstallMethod = null;
            _log = new List<string>();
            _loader = new FakeLoader(_log);
            _transition = new FakeTransition(_log);
            _hooks = new List<ISceneTransitionHook>();
            _service = new SceneService(new SceneSettings { MinimumLoadingTime = 0f }, _transition, _hooks, _loader);
            _service.OnBeforeSceneUnload.Subscribe(_ => _log.Add("beforeSceneUnload"));
            _service.OnLoaded.Subscribe(name => _log.Add("loaded:" + name));
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
            SceneContext.ExtraBindingsInstallMethod = _savedExtraBindings;
        }

        private static IEnumerator Run(Func<UniTask> test) => UniTask.ToCoroutine(test);

        private static async UniTask<Exception> CatchAsync(UniTask task)
        {
            try
            {
                await task;
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        // ---- 遷移の順序 ----

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_RunsStepsInDocumentedOrder() => Run(async () =>
        {
            _hooks.Add(new RecordingHook("h1", _log));
            _hooks.Add(new RecordingHook("h2", _log));

            var result = await _service.LoadAsync("Game");

            Assert.That(result, Is.True);
            Assert.That(_log, Is.EqualTo(new[]
            {
                "fadeOut",
                "h1.before:Title->Game", "h2.before:Title->Game", // 登録順に、読み込み前
                "beforeSceneUnload",
                "activate:Game",
                "h1.after:Title->Game", "h2.after:Title->Game",   // アクティベート後、フェードイン前
                "fadeIn",
                "loaded:Game",
            }));
            Assert.That(_service.CurrentSceneName, Is.EqualTo("Game"));
            Assert.That(_service.IsLoading.CurrentValue, Is.False);
            Assert.That(_service.ShowLoadingScreen.CurrentValue, Is.False);
            Assert.That(_service.Progress.CurrentValue, Is.EqualTo(1f));
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_IsLoadingAndLoadingScreen_AreTrueDuringTransition() => Run(async () =>
        {
            var isLoadingAtActivate = false;
            var showLoadingAtActivate = false;
            _loader.OnActivate = () =>
            {
                isLoadingAtActivate = _service.IsLoading.CurrentValue;
                showLoadingAtActivate = _service.ShowLoadingScreen.CurrentValue;
            };

            await _service.LoadAsync("Game");

            Assert.That(isLoadingAtActivate, Is.True);
            Assert.That(showLoadingAtActivate, Is.True);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_ShowLoadingViewFalse_DoesNotRaiseLoadingScreen() => Run(async () =>
        {
            var raised = false;
            _service.ShowLoadingScreen.Subscribe(x => raised |= x);

            await _service.LoadAsync("Game", SceneLoadOptions.Instant);

            Assert.That(raised, Is.False);
        });

        // ---- 拒否 ----

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_WhileLoading_IsRejectedWithFalse() => Run(async () =>
        {
            _transition.FadeOutGate = new UniTaskCompletionSource();
            var first = _service.LoadAsync("Game");
            Assert.That(_service.IsLoading.CurrentValue, Is.True);

            LogAssert.Expect(LogType.Warning, new Regex("ロード中のため \"Overlay\""));
            Assert.That(await _service.LoadAsync("Overlay"), Is.False);
            LogAssert.Expect(LogType.Warning, new Regex("ロード中のため"));
            Assert.That(await _service.ReloadAsync(), Is.False);

            _transition.FadeOutGate.TrySetResult();
            Assert.That(await first, Is.True);
            Assert.That(_service.CurrentSceneName, Is.EqualTo("Game"));
            Assert.That(_loader.SingleLoadCount, Is.EqualTo(1));
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator AdditiveAndUnload_WhileLoading_AreRejectedWithFalse() => Run(async () =>
        {
            _loader.Loaded.Add("Hud");
            _transition.FadeOutGate = new UniTaskCompletionSource();
            var first = _service.LoadAsync("Game");

            LogAssert.Expect(LogType.Warning, new Regex("シーン遷移中のため \"Overlay\" の追加読み込み"));
            Assert.That(await _service.LoadAdditiveAsync("Overlay"), Is.False);
            LogAssert.Expect(LogType.Warning, new Regex("シーン遷移中のため \"Hud\" の破棄"));
            Assert.That(await _service.UnloadAsync("Hud"), Is.False);
            Assert.That(_loader.AdditiveOperations, Is.Empty);

            _transition.FadeOutGate.TrySetResult();
            await first;
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_UnknownOrEmptyScene_ReturnsFalseWithoutFade() => Run(async () =>
        {
            LogAssert.Expect(LogType.Error, new Regex("シーン \"Missing\" が見つかりません"));
            Assert.That(await _service.LoadAsync("Missing"), Is.False);
            LogAssert.Expect(LogType.Error, new Regex("が見つかりません"));
            Assert.That(await _service.LoadAsync(""), Is.False);
            LogAssert.Expect(LogType.Error, new Regex("が見つかりません"));
            Assert.That(await _service.LoadAsync(null), Is.False);

            Assert.That(_transition.FadeOutCount, Is.Zero);
            Assert.That(_service.IsLoading.CurrentValue, Is.False);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_LoadCannotStart_ThrowsAndRecovers() => Run(async () =>
        {
            _loader.FailToStart = true;

            var e = await CatchAsync(_service.LoadAsync("Game"));

            Assert.That(e, Is.InstanceOf<InvalidOperationException>());
            Assert.That(_transition.FadeInCount, Is.EqualTo(1), "失敗しても覆いは外す");
            Assert.That(_service.IsLoading.CurrentValue, Is.False, "失敗後も次の遷移を受け付ける");
            Assert.That(_log, Does.Not.Contain("loaded:Game"));
        });

        // ---- キャンセル ----

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_CancelledDuringFadeOut_StaysAndThrows() => Run(async () =>
        {
            _transition.FadeOutGate = new UniTaskCompletionSource();
            var cts = new CancellationTokenSource();
            var load = _service.LoadAsync("Game", ct: cts.Token);

            cts.Cancel();
            var e = await CatchAsync(load);

            Assert.That(e, Is.InstanceOf<OperationCanceledException>());
            Assert.That(_service.IsLoading.CurrentValue, Is.False);
            Assert.That(_transition.FadeInCount, Is.EqualTo(1), "元のシーンに留まってフェードインする");
            Assert.That(_loader.SingleLoadCount, Is.Zero, "読み込みは始めない");
            Assert.That(_service.CurrentSceneName, Is.EqualTo("Title"));
            Assert.That(_log, Does.Not.Contain("loaded:Game"));

            // 中止後も詰まらずに次の遷移ができる
            _transition.FadeOutGate = null;
            Assert.That(await _service.LoadAsync("Game"), Is.True);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_AlreadyCancelled_ThrowsWithoutFade() => Run(async () =>
        {
            var e = await CatchAsync(_service.LoadAsync("Game", ct: new CancellationToken(true)));

            Assert.That(e, Is.InstanceOf<OperationCanceledException>());
            Assert.That(_transition.FadeOutCount, Is.Zero);
            Assert.That(_service.IsLoading.CurrentValue, Is.False);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_CancelledInBeforeUnloadHook_AbortsAndSkipsRemainingHooks() => Run(async () =>
        {
            var h1 = new RecordingHook("h1", _log) { BeforeGate = new UniTaskCompletionSource() };
            _hooks.Add(h1);
            _hooks.Add(new RecordingHook("h2", _log));
            var cts = new CancellationTokenSource();
            var load = _service.LoadAsync("Game", ct: cts.Token);

            cts.Cancel();
            var e = await CatchAsync(load);

            Assert.That(e, Is.InstanceOf<OperationCanceledException>());
            Assert.That(_log, Is.EqualTo(new[] { "fadeOut", "h1.before:Title->Game", "fadeIn" }));
            Assert.That(_loader.SingleLoadCount, Is.Zero);
            Assert.That(_service.IsLoading.CurrentValue, Is.False);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAsync_CancelledAfterLoadStarted_IsIgnored() => Run(async () =>
        {
            var cts = new CancellationTokenSource();
            // 読み込み開始後 (アクティベート直前) にキャンセルしても止まらない
            _service.OnBeforeSceneUnload.Subscribe(_ => cts.Cancel());

            var result = await _service.LoadAsync("Game", ct: cts.Token);

            Assert.That(result, Is.True);
            Assert.That(_service.CurrentSceneName, Is.EqualTo("Game"));
        });

        // ---- フックの例外 ----

        [UnityTest, Timeout(10000)]
        public IEnumerator HookException_IsLogged_AndTransitionContinues() => Run(async () =>
        {
            _hooks.Add(new RecordingHook("h1", _log)
            {
                ThrowBefore = new InvalidOperationException("before-boom"),
                ThrowAfter = new InvalidOperationException("after-boom"),
            });
            _hooks.Add(new RecordingHook("h2", _log));

            LogAssert.Expect(LogType.Error, new Regex(@"RecordingHook\.OnBeforeUnloadAsync で例外"));
            LogAssert.Expect(LogType.Exception, new Regex("before-boom"));
            LogAssert.Expect(LogType.Error, new Regex(@"RecordingHook\.OnAfterLoadAsync で例外"));
            LogAssert.Expect(LogType.Exception, new Regex("after-boom"));

            var result = await _service.LoadAsync("Game");

            Assert.That(result, Is.True);
            // 例外を投げたフックの後のフックも呼ばれる
            Assert.That(_log, Does.Contain("h2.before:Title->Game"));
            Assert.That(_log, Does.Contain("h2.after:Title->Game"));
            Assert.That(_log, Does.Contain("loaded:Game"));
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator HookOwnCancellation_WithoutCallerCancel_IsTreatedAsFailure() => Run(async () =>
        {
            // ct がキャンセルされていない OperationCanceledException (フック内部のタイムアウトなど) は中止ではなく失敗扱い
            _hooks.Add(new RecordingHook("h1", _log) { ThrowBefore = new OperationCanceledException("timeout") });

            LogAssert.Expect(LogType.Error, new Regex("OnBeforeUnloadAsync で例外"));
            // UniTask はキャンセル状態に変換する際に例外のメッセージを捨てるため、型名で照合する
            LogAssert.Expect(LogType.Exception, new Regex("OperationCanceledException"));

            Assert.That(await _service.LoadAsync("Game"), Is.True);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator SubscriberException_DoesNotBreakTransition() => Run(async () =>
        {
            // R3 は購読側の例外を未処理例外ハンドラに回す。遷移の結果や状態には影響しないこと
            var previous = ObservableSystem.GetUnhandledExceptionHandler();
            var unhandled = new List<Exception>();
            ObservableSystem.RegisterUnhandledExceptionHandler(unhandled.Add);
            try
            {
                _service.OnBeforeSceneUnload.Subscribe(_ => throw new InvalidOperationException("unload-boom"));
                _service.OnLoaded.Subscribe(_ => throw new InvalidOperationException("loaded-boom"));

                var result = await _service.LoadAsync("Game");

                Assert.That(result, Is.True);
                Assert.That(_service.IsLoading.CurrentValue, Is.False);
                Assert.That(unhandled.ConvertAll(x => x.Message), Is.EquivalentTo(new[] { "unload-boom", "loaded-boom" }));
                Assert.That(await _service.LoadAsync("Title"), Is.True, "次の遷移も受け付ける");
            }
            finally
            {
                ObservableSystem.RegisterUnhandledExceptionHandler(previous);
            }
        });

        // ---- データの受け渡し ----

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadWithPayload_BindsPayloadDuringActivation_AndRestoresAfterwards() => Run(async () =>
        {
            var payload = new Payload { Value = 42 };
            Payload injected = null;
            _loader.OnActivate = () =>
            {
                // 遷移先の SceneContext が Install 時に使う
                var install = SceneContext.ExtraBindingsInstallMethod;
                Assert.That(install, Is.Not.Null);
                var container = new DiContainer();
                install(container);
                injected = container.Resolve<Payload>();
            };

            Assert.That(await _service.LoadWithPayloadAsync("Game", payload), Is.True);

            Assert.That(injected, Is.SameAs(payload));
            Assert.That(SceneContext.ExtraBindingsInstallMethod, Is.Null, "使われなかった設定は元に戻す");
            Assert.That(_service.TryGetPayload<Payload>(out var got), Is.True);
            Assert.That(got, Is.SameAs(payload));
            Assert.That(_service.TryGetPayload<string>(out _), Is.False, "型が違えば取得できない");
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator Reload_KeepsPayload_PlainLoad_ClearsIt() => Run(async () =>
        {
            var payload = new Payload { Value = 1 };
            await _service.LoadWithPayloadAsync("Game", payload);

            Payload injected = null;
            _loader.OnActivate = () =>
            {
                var container = new DiContainer();
                SceneContext.ExtraBindingsInstallMethod?.Invoke(container);
                injected = container.TryResolve<Payload>();
            };
            Assert.That(await _service.ReloadAsync(), Is.True);
            Assert.That(injected, Is.SameAs(payload), "リロード後も [Inject] で受け取れる");
            Assert.That(_service.TryGetPayload<Payload>(out _), Is.True);

            _loader.OnActivate = null;
            await _service.LoadAsync("Title");
            Assert.That(_service.TryGetPayload<Payload>(out _), Is.False);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadWithPayload_ActivationThrows_RestoresPreviousExtraBindings() => Run(async () =>
        {
            Action<DiContainer> other = _ => { }; // ZenjectSceneLoader などが設定していた値
            SceneContext.ExtraBindingsInstallMethod = other;
            _loader.OnActivate = () => throw new InvalidOperationException("activate-boom");

            LogAssert.Expect(LogType.Warning, new Regex("ExtraBindingsInstallMethod が他の処理"));
            var e = await CatchAsync(_service.LoadWithPayloadAsync("Game", new Payload()));

            Assert.That(e, Is.InstanceOf<InvalidOperationException>().With.Message.EqualTo("activate-boom"));
            Assert.That(SceneContext.ExtraBindingsInstallMethod, Is.SameAs(other));
            Assert.That(_service.IsLoading.CurrentValue, Is.False);
            Assert.That(_transition.FadeInCount, Is.EqualTo(1));
        });

        // ---- 追加読み込み / 破棄 ----

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAdditive_SameScenePending_IsRejected_OtherSceneAllowed() => Run(async () =>
        {
            var first = _service.LoadAdditiveAsync("Overlay");
            Assert.That(_loader.AdditiveOperations, Has.Count.EqualTo(1));

            LogAssert.Expect(LogType.Warning, new Regex("\"Overlay\" は追加読み込み / 破棄の処理中"));
            Assert.That(await _service.LoadAdditiveAsync("Overlay"), Is.False);
            LogAssert.Expect(LogType.Warning, new Regex("\"Overlay\" は追加読み込み / 破棄の処理中"));
            Assert.That(await _service.UnloadAsync("Overlay"), Is.False);

            var other = _service.LoadAdditiveAsync("Hud");
            Assert.That(_loader.AdditiveOperations, Has.Count.EqualTo(2));

            _loader.AdditiveOperations[0].Complete();
            _loader.AdditiveOperations[1].Complete();
            Assert.That(await first, Is.True);
            Assert.That(await other, Is.True);

            // 完了後は同じシーンを操作できる
            var unload = _service.UnloadAsync("Overlay");
            _loader.AdditiveOperations[2].Complete();
            Assert.That(await unload, Is.True);
            Assert.That(_loader.IsLoaded("Overlay"), Is.False);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAdditive_AlreadyLoaded_And_Unload_NotLoaded_AreNoOpTrue() => Run(async () =>
        {
            _loader.Loaded.Add("Overlay");
            Assert.That(await _service.LoadAdditiveAsync("Overlay"), Is.True);
            Assert.That(await _service.UnloadAsync("Hud"), Is.True);
            Assert.That(_loader.AdditiveOperations, Is.Empty);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAdditive_InvalidInput_ReturnsFalse() => Run(async () =>
        {
            LogAssert.Expect(LogType.Error, new Regex("シーン \"Missing\" が見つかりません"));
            Assert.That(await _service.LoadAdditiveAsync("Missing"), Is.False);
            LogAssert.Expect(LogType.Error, new Regex("シーン名が空のため追加読み込み"));
            Assert.That(await _service.LoadAdditiveAsync(""), Is.False);
            LogAssert.Expect(LogType.Error, new Regex("シーン名が空のため破棄"));
            Assert.That(await _service.UnloadAsync(null), Is.False);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator Unload_LastScene_ReturnsFalse() => Run(async () =>
        {
            LogAssert.Expect(LogType.Error, new Regex("最後の 1 シーンは破棄できません"));
            Assert.That(await _service.UnloadAsync("Title"), Is.False);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator LoadAdditive_Cancelled_Throws_ButKeepsPendingUntilUnityFinishes() => Run(async () =>
        {
            var cts = new CancellationTokenSource();
            var load = _service.LoadAdditiveAsync("Overlay", cts.Token);
            cts.Cancel();

            Assert.That(await CatchAsync(load), Is.InstanceOf<OperationCanceledException>());

            // 読み込み自体は裏で続いているので、終わるまでは同じシーンを操作できない
            LogAssert.Expect(LogType.Warning, new Regex("処理中"));
            Assert.That(await _service.LoadAdditiveAsync("Overlay"), Is.False);

            _loader.AdditiveOperations[0].Complete();
            Assert.That(await _service.LoadAdditiveAsync("Overlay"), Is.True, "読み込み済みなので true");
        });

        // ---- 破棄 ----

        [UnityTest, Timeout(10000)]
        public IEnumerator DisposeDuringFadeOut_AbortsWithoutLoadingOrFadeIn() => Run(async () =>
        {
            _transition.FadeOutGate = new UniTaskCompletionSource();
            var load = _service.LoadAsync("Game");

            _service.Dispose();
            var e = await CatchAsync(load);

            Assert.That(e, Is.InstanceOf<OperationCanceledException>());
            Assert.That(_loader.SingleLoadCount, Is.Zero, "破棄後に新しいシーンを読み込み始めない");
            Assert.That(_transition.FadeInCount, Is.Zero);
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator DisposeWhileLoading_FinishesLoadWithoutTouchingDisposedState() => Run(async () =>
        {
            _loader.InitialProgress = 0f; // 進捗待ちのループに入れる
            var load = _service.LoadAsync("Game");
            await UniTask.Yield();
            Assert.That(_loader.LastSingle, Is.Not.Null);

            _service.Dispose();
            _loader.LastSingle.Progress = 1f;

            // 以前は破棄済みの ReactiveProperty / Subject に書き込んで ObjectDisposedException になっていた
            var e = await CatchAsync(load);

            Assert.That(e, Is.Null);
            Assert.That(_loader.ActiveSceneName, Is.EqualTo("Game"), "allowSceneActivation=false のまま放置しない");
            Assert.That(_log, Does.Not.Contain("loaded:Game"));
        });

        [UnityTest, Timeout(10000)]
        public IEnumerator AfterDispose_AllRequestsReturnFalse() => Run(async () =>
        {
            _service.Dispose();
            _service.Dispose(); // 二重に呼んでも安全

            Assert.That(await _service.LoadAsync("Game"), Is.False);
            Assert.That(await _service.LoadAdditiveAsync("Overlay"), Is.False);
            Assert.That(await _service.UnloadAsync("Title"), Is.False);
            Assert.That(_transition.FadeOutCount, Is.Zero);
        });

        [Test]
        public void Constructor_NullTransition_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SceneService(new SceneSettings(), null));
        }
    }
}
