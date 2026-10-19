using System.Collections.Generic;
using System.Threading;
using Abubu.Inputs;
using Abubu.Inputs.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    public sealed class RebindPresenterTests
    {
        /// <summary>実物の InputActionAsset を使い、対話的な割り当て変更だけを差し替えた IInputService</summary>
        private sealed class FakeInput : IInputService
        {
            private readonly ReactiveProperty<InputDeviceKind> _device = new(InputDeviceKind.KeyboardMouse);
            private readonly ReactiveProperty<InputDeviceKind> _scheme = new(InputDeviceKind.KeyboardMouse);
            private readonly ReactiveProperty<bool> _rebinding = new(false);
            private readonly Subject<Unit> _bindingsChanged = new();

            /// <summary>次の RebindAsync で割り当てる操作 (複合ならパートの数だけ)。空ならキャンセル扱い</summary>
            public readonly Queue<string> NextPaths = new();
            /// <summary>設定すると RebindAsync はこれが完了するまで待つ (キャンセルのテスト用)</summary>
            public UniTaskCompletionSource<bool> Pending;
            public int SaveCount;

            public FakeInput(InputActionAsset asset) => Actions = asset;

            public InputActionAsset Actions { get; }
            public ReadOnlyReactiveProperty<InputDeviceKind> CurrentDevice => _device;
            public ReadOnlyReactiveProperty<InputDeviceKind> CurrentScheme => _scheme;
            public ReadOnlyReactiveProperty<bool> IsRebinding => _rebinding;
            public Observable<Unit> BindingsChanged => _bindingsChanged;

            /// <summary>使用中のデバイスを変える (InputDeviceTracker と同じく、系統はキーボード&マウス / ゲームパッドのときだけ変わる)</summary>
            public void SetDevice(InputDeviceKind kind)
            {
                _device.Value = kind;
                var scheme = InputDeviceClassifier.ToScheme(kind);
                if (scheme.HasValue) _scheme.Value = scheme.Value;
            }

            public InputAction GetAction(string actionPath) => string.IsNullOrEmpty(actionPath) ? null : Actions.FindAction(actionPath);

            public async UniTask<bool> RebindAsync(string actionPath, int bindingIndex = 0, CancellationToken ct = default)
            {
                var action = GetAction(actionPath);
                if (Pending != null)
                {
                    using (ct.Register(() => Pending.TrySetResult(false)))
                    {
                        if (!await Pending.Task) return false;
                    }
                }

                var targets = new List<int>();
                InputBindingUtility.GetTargetIndices(action, bindingIndex, targets);
                if (NextPaths.Count < targets.Count) return false;
                foreach (var index in targets) action.ApplyBindingOverride(index, NextPaths.Dequeue());
                SaveCount++;
                _bindingsChanged.OnNext(Unit.Default);
                return true;
            }

            public string GetBindingDisplayString(string actionPath, int bindingIndex = 0)
            {
                var action = GetAction(actionPath);
                return action != null && bindingIndex >= 0 && bindingIndex < action.bindings.Count
                    ? action.GetBindingDisplayString(bindingIndex)
                    : string.Empty;
            }

            public void ResetBindings()
            {
                Actions.RemoveAllBindingOverrides();
                _bindingsChanged.OnNext(Unit.Default);
            }

            public void ResetBinding(string actionPath, int bindingIndex)
            {
                var action = GetAction(actionPath);
                var targets = new List<int> { bindingIndex };
                InputBindingUtility.GetTargetIndices(action, bindingIndex, targets);
                foreach (var index in targets) action.RemoveBindingOverride(index);
                SaveCount++;
                _bindingsChanged.OnNext(Unit.Default);
            }

            public void SetBindingOverride(string actionPath, int bindingIndex, string overridePath)
            {
                var action = GetAction(actionPath);
                if (overridePath == null) action.RemoveBindingOverride(bindingIndex);
                else action.ApplyBindingOverride(bindingIndex, overridePath);
                _bindingsChanged.OnNext(Unit.Default);
            }

            public void SaveBindings() => SaveCount++;

            // --- このテストでは使わない
            public Observable<InputAction.CallbackContext> Performed(string actionPath) => Observable.Empty<InputAction.CallbackContext>();
            public Observable<Unit> OnPerformed(string actionPath) => Observable.Empty<Unit>();
            public ReadOnlyReactiveProperty<T> ReadValue<T>(string actionPath) where T : struct => new ReactiveProperty<T>(default);
            public ReadOnlyReactiveProperty<bool> IsPressed(string actionPath) => new ReactiveProperty<bool>(false);
            public void SetMapEnabled(string mapName, bool enabled) { }
            public void SetGameplayEnabled(bool enabled) { }
            public ReadOnlyReactiveProperty<Vector2> Move => new ReactiveProperty<Vector2>(default);
            public ReadOnlyReactiveProperty<Vector2> Look => new ReactiveProperty<Vector2>(default);
            public Observable<Unit> Jump => Observable.Empty<Unit>();
            public Observable<Unit> Attack => Observable.Empty<Unit>();
            public Observable<Unit> Interact => Observable.Empty<Unit>();
            public Observable<Unit> Submit => Observable.Empty<Unit>();
            public Observable<Unit> Cancel => Observable.Empty<Unit>();
        }

        private sealed class FakeView : IRebindView
        {
            public readonly List<RebindEntry> EntryList = new();
            public readonly Dictionary<int, RebindRowState> Rows = new();
            public readonly Subject<int> Rebind = new();
            public readonly Subject<int> Reset = new();
            public readonly Subject<Unit> ResetAll = new();
            public readonly Subject<Unit> CancelRequested = new();
            public RebindConflictPolicy Policy = RebindConflictPolicy.Allow;
            public bool Listening;
            public string Prompt = string.Empty;
            public string Message = string.Empty;

            public IReadOnlyList<RebindEntry> Entries => EntryList;
            public RebindConflictPolicy ConflictPolicy => Policy;
            public IRebindLabels Labels => null;
            public void SetRow(int row, in RebindRowState state) => Rows[row] = state;

            public void SetListening(bool listening, string prompt)
            {
                Listening = listening;
                Prompt = prompt;
            }

            public void SetMessage(string message) => Message = message;
            public Observable<int> OnRebindRequested => Rebind;
            public Observable<int> OnResetRequested => Reset;
            public Observable<Unit> OnResetAllRequested => ResetAll;
            public Observable<Unit> OnCancelRequested => CancelRequested;
        }

        private readonly List<Object> _created = new();
        private InputActionAsset _asset;
        private FakeInput _input;
        private FakeView _view;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(_asset);
            var player = _asset.AddActionMap("Player");
            var jump = player.AddAction("Jump", InputActionType.Button);
            jump.AddBinding("<Keyboard>/space");
            jump.AddBinding("<Gamepad>/buttonSouth");
            var interact = player.AddAction("Interact", InputActionType.Button);
            interact.AddBinding("<Keyboard>/f");
            interact.AddBinding("<Gamepad>/buttonWest");
            var move = player.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _input = new FakeInput(_asset);
            _view = new FakeView();
            _view.EntryList.Add(new RebindEntry("Player/Jump", "Jump!"));
            _view.EntryList.Add(new RebindEntry("Player/Interact"));
            _view.EntryList.Add(new RebindEntry("Player/Move", "Move", 0));
            _view.EntryList.Add(new RebindEntry("Player/Missing"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private RebindPresenter Create()
        {
            var presenter = new RebindPresenter(_input, _view);
            presenter.Initialize();
            return presenter;
        }

        private InputAction Jump => _asset.FindAction("Player/Jump");
        private InputAction Interact => _asset.FindAction("Player/Interact");

        [Test]
        public void Initialize_ShowsRows()
        {
            using var presenter = Create();
            Assert.AreEqual("Jump!", _view.Rows[0].ActionName);
            Assert.AreEqual(Jump.GetBindingDisplayString(0), _view.Rows[0].BindingText);
            Assert.AreEqual("Interact", _view.Rows[1].ActionName);
            Assert.IsFalse(_view.Rows[0].IsOverridden);
            Assert.IsTrue(_view.Rows[0].CanRebind);
            Assert.IsFalse(_view.Rows[3].CanRebind, "存在しないアクションの行は操作できない");
        }

        [Test]
        public void DeviceChange_SwitchesToGamepadBinding()
        {
            using var presenter = Create();
            _input.SetDevice(InputDeviceKind.Gamepad);
            Assert.AreEqual(Jump.GetBindingDisplayString(1), _view.Rows[0].BindingText);
            Assert.AreEqual("Move", _view.Rows[2].ActionName, "BindingIndex 固定の行はそのまま");
        }

        [Test]
        public void TouchOrPen_DoesNotMakeRowsUnbound()
        {
            using var presenter = Create();
            _input.SetDevice(InputDeviceKind.Gamepad);
            _input.SetDevice(InputDeviceKind.Touch);
            Assert.AreEqual(Jump.GetBindingDisplayString(1), _view.Rows[0].BindingText, "タッチを触ってもゲームパッドの割り当てのまま");
            Assert.IsTrue(_view.Rows[0].CanRebind);

            _input.SetDevice(InputDeviceKind.Other);
            Assert.AreEqual(Jump.GetBindingDisplayString(1), _view.Rows[0].BindingText);
        }

        [Test]
        public void Rebind_WithoutConflict_Applies()
        {
            using var presenter = Create();
            _input.NextPaths.Enqueue("<Keyboard>/j");
            Assert.IsTrue(presenter.RebindAsync(0).GetAwaiter().GetResult());
            Assert.AreEqual("<Keyboard>/j", Jump.bindings[0].effectivePath);
            Assert.IsTrue(_view.Rows[0].IsOverridden);
            Assert.IsFalse(_view.Rows[0].HasConflict);
            Assert.IsFalse(_view.Listening);
            Assert.IsEmpty(_view.Message);
        }

        [Test]
        public void Rebind_UsesGamepadBinding_WhenGamepadActive()
        {
            using var presenter = Create();
            _input.SetDevice(InputDeviceKind.Gamepad);
            _input.NextPaths.Enqueue("<Gamepad>/buttonNorth");
            Assert.IsTrue(presenter.RebindAsync(0).GetAwaiter().GetResult());
            Assert.AreEqual("<Keyboard>/space", Jump.bindings[0].effectivePath);
            Assert.AreEqual("<Gamepad>/buttonNorth", Jump.bindings[1].effectivePath);
        }

        [Test]
        public void Conflict_Allow_KeepsAndWarns()
        {
            _view.Policy = RebindConflictPolicy.Allow;
            using var presenter = Create();
            _input.NextPaths.Enqueue("<Keyboard>/f");
            Assert.IsTrue(presenter.RebindAsync(0).GetAwaiter().GetResult());

            Assert.AreEqual("<Keyboard>/f", Jump.bindings[0].effectivePath);
            Assert.AreEqual("<Keyboard>/f", Interact.bindings[0].effectivePath);
            Assert.IsTrue(_view.Rows[0].HasConflict);
            Assert.IsTrue(_view.Rows[1].HasConflict);
            StringAssert.Contains("Interact", _view.Rows[0].ConflictText);
            StringAssert.Contains("Jump!", _view.Rows[1].ConflictText, "行の表示名が使われる");
            Assert.IsNotEmpty(_view.Message);
        }

        [Test]
        public void Conflict_Swap_GivesOldBindingToOther()
        {
            _view.Policy = RebindConflictPolicy.Swap;
            using var presenter = Create();
            _input.NextPaths.Enqueue("<Keyboard>/f");
            Assert.IsTrue(presenter.RebindAsync(0).GetAwaiter().GetResult());

            Assert.AreEqual("<Keyboard>/f", Jump.bindings[0].effectivePath);
            Assert.AreEqual("<Keyboard>/space", Interact.bindings[0].effectivePath);
            Assert.IsFalse(_view.Rows[0].HasConflict);
            Assert.IsFalse(_view.Rows[1].HasConflict);
            Assert.Greater(_input.SaveCount, 1, "入れ替え後に保存する");
        }

        [Test]
        public void Conflict_Swap_BackToDefault_RemovesOverride()
        {
            _view.Policy = RebindConflictPolicy.Swap;
            using var presenter = Create();
            _input.NextPaths.Enqueue("<Keyboard>/f");
            presenter.RebindAsync(0).GetAwaiter().GetResult();
            // 元に戻す方向に入れ替えると、上書きは解除される
            _input.NextPaths.Enqueue("<Keyboard>/f");
            presenter.RebindAsync(1).GetAwaiter().GetResult();

            Assert.AreEqual("<Keyboard>/f", Interact.bindings[0].effectivePath);
            Assert.IsNull(Interact.bindings[0].overridePath);
            Assert.AreEqual("<Keyboard>/space", Jump.bindings[0].effectivePath);
            Assert.IsNull(Jump.bindings[0].overridePath);
        }

        [Test]
        public void Conflict_Reject_RevertsAndReturnsFalse()
        {
            _view.Policy = RebindConflictPolicy.Reject;
            using var presenter = Create();
            _input.NextPaths.Enqueue("<Keyboard>/f");
            Assert.IsFalse(presenter.RebindAsync(0).GetAwaiter().GetResult());

            Assert.AreEqual("<Keyboard>/space", Jump.bindings[0].effectivePath);
            Assert.IsNull(Jump.bindings[0].overridePath);
            Assert.AreEqual("<Keyboard>/f", Interact.bindings[0].effectivePath);
            StringAssert.Contains("Interact", _view.Message);
        }

        [Test]
        public void Conflict_Reject_RevertsAllCompositeParts()
        {
            _view.Policy = RebindConflictPolicy.Reject;
            using var presenter = Create();
            var move = _asset.FindAction("Player/Move");
            _input.NextPaths.Enqueue("<Keyboard>/upArrow");
            _input.NextPaths.Enqueue("<Keyboard>/downArrow");
            _input.NextPaths.Enqueue("<Keyboard>/f");
            _input.NextPaths.Enqueue("<Keyboard>/rightArrow");
            Assert.IsFalse(presenter.RebindAsync(2).GetAwaiter().GetResult());

            for (var i = 1; i <= 4; i++) Assert.IsNull(move.bindings[i].overridePath);
        }

        [Test]
        public void Composite_RebindsAllParts()
        {
            using var presenter = Create();
            var move = _asset.FindAction("Player/Move");
            foreach (var key in new[] { "upArrow", "downArrow", "leftArrow", "rightArrow" }) _input.NextPaths.Enqueue("<Keyboard>/" + key);
            Assert.IsTrue(presenter.RebindAsync(2).GetAwaiter().GetResult());
            Assert.AreEqual("<Keyboard>/leftArrow", move.bindings[3].effectivePath);
            Assert.IsTrue(_view.Rows[2].IsOverridden);
        }

        [Test]
        public void Cancel_StopsListening()
        {
            using var presenter = Create();
            _input.Pending = new UniTaskCompletionSource<bool>();
            var task = presenter.RebindAsync(0);
            Assert.IsTrue(presenter.IsRebinding);
            Assert.IsTrue(_view.Listening);
            StringAssert.Contains("Jump!", _view.Prompt);
            Assert.IsTrue(_view.Rows[0].IsListening);
            Assert.IsFalse(_view.Rows[1].CanRebind, "入力待ち中は他の行を操作できない");

            _view.CancelRequested.OnNext(Unit.Default);
            Assert.IsFalse(task.GetAwaiter().GetResult());
            Assert.IsFalse(presenter.IsRebinding);
            Assert.IsFalse(_view.Listening);
            Assert.IsTrue(_view.Rows[1].CanRebind);
        }

        [Test]
        public void ResetRow_AndResetAll()
        {
            using var presenter = Create();
            Jump.ApplyBindingOverride(0, "<Keyboard>/j");
            Interact.ApplyBindingOverride(0, "<Keyboard>/k");

            _view.Reset.OnNext(0);
            Assert.IsNull(Jump.bindings[0].overridePath);
            Assert.AreEqual("<Keyboard>/k", Interact.bindings[0].effectivePath);
            Assert.IsFalse(_view.Rows[0].IsOverridden);

            _view.ResetAll.OnNext(Unit.Default);
            Assert.IsNull(Interact.bindings[0].overridePath);
        }

        [Test]
        public void ViewRequest_StartsRebind()
        {
            using var presenter = Create();
            _input.NextPaths.Enqueue("<Keyboard>/j");
            _view.Rebind.OnNext(0);
            Assert.AreEqual("<Keyboard>/j", Jump.bindings[0].effectivePath);
        }
    }
}
