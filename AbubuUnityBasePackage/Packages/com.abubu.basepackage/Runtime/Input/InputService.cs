using System;
using System.Collections.Generic;
using System.Threading;
using Abubu.Pause;
using Abubu.Save;
using Cysharp.Threading.Tasks;
using R3;
using Abubu.Debugging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Scripting;
using Zenject;
using Object = UnityEngine.Object;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.Inputs.InputModuleInstaller))]

namespace Abubu.Inputs
{
    /// <summary>
    /// <see cref="IInputService"/> の実装。プロジェクトの入力設定を乗っ取らないよう、次の方針で動く:
    /// <list type="bullet">
    /// <item>ActionMap は EnabledMaps / PauseDisabledMaps / SetMapEnabled で指定されたものだけ変更し、Dispose 時に元へ戻す
    /// (他から状態が変えられていたマップには触らない)</item>
    /// <item>割り当て変更は読み込み前の状態を覚えておき、Dispose 時に戻す (RevertBindingOverridesOnDispose)</item>
    /// <item>見つからないアクションは黙って無視する (StrictActionLookup で警告)</item>
    /// </list>
    /// PlayerInput などで ActionMap を切り替えているプロジェクトでは、EnabledMaps / PauseDisabledMaps を空のままにする。
    /// <para>
    /// 割り当て変更 (<see cref="RebindAsync"/>) の入力待ち中は、キャンセル操作 (RebindCancelPaths) をどのデバイスでも受け付け、
    /// 受け付けないデバイスのボタンは捨てる (<see cref="RebindInputGuard"/>)。入力待ち中は <see cref="IsRebinding"/> が true になる。
    /// </para>
    /// <para>
    /// DebugOverlay をゲームパッドで操作している間は、GameplayMap と PauseDisabledMaps を無効にする
    /// (AbubuInputSettings.BlockGameplayWhileDebugOverlayFocused)。
    /// </para>
    /// </summary>
    public sealed class InputService : IInputService, IInitializable, IDisposable
    {
        private const string LogPrefix = "[Abubu.Input] ";

        /// <summary>このサービスが触った ActionMap の状態</summary>
        private sealed class MapState
        {
            public InputActionMap Map;
            /// <summary>最初に触る前の有効状態 (Dispose で戻す)</summary>
            public bool Original;
            /// <summary>最後にこのサービスが設定した状態。現在の状態と違えば、他の誰かが変えたとみなして戻さない</summary>
            public bool LastApplied;
            /// <summary>ポーズなどで無効にしたので、すべての理由が解消したら有効へ戻す</summary>
            public bool ResumeOnUnpause;
            /// <summary>無効にしている理由 (<see cref="BlockReason"/> の組み合わせ)</summary>
            public BlockReason Blocks;
        }

        /// <summary>ActionMap を一時的に無効にしている理由</summary>
        [Flags]
        private enum BlockReason
        {
            None = 0,
            Pause = 1 << 0,
            DebugOverlay = 1 << 1,
        }

        private readonly AbubuInputSettings _settings;
        private readonly bool _ownsSettings;
        private readonly ISaveService _save;
        private readonly IPauseService _pause;
        private readonly InputActionAsset _asset;
        private readonly CompositeDisposable _disposables = new();
        private readonly List<IDisposable> _ownedObservables = new();
        private readonly Dictionary<string, Subject<InputAction.CallbackContext>> _performed = new();
        private readonly Dictionary<string, Observable<Unit>> _performedUnit = new();
        private readonly Dictionary<(string path, Type type), object> _values = new();
        private readonly Dictionary<string, ReactiveProperty<bool>> _pressed = new();
        private readonly List<(InputAction action, Action<InputAction.CallbackContext> handler, bool performedOnly)> _handlers = new();
        /// <summary>ActionMap を無効にしたときに値を既定値へ戻す処理 (canceled が来ない場合の保険)</summary>
        private readonly List<(InputAction action, Action reset)> _resetters = new();
        private readonly Dictionary<InputActionMap, MapState> _mapStates = new();
        private readonly ReactiveProperty<InputDeviceKind> _device = new(InputDeviceKind.KeyboardMouse);
        private readonly ReactiveProperty<InputDeviceKind> _scheme = new(InputDeviceKind.KeyboardMouse);
        private readonly ReactiveProperty<bool> _rebinding = new(false);
        private readonly IInputDeviceService _devices;
        private readonly Subject<Unit> _bindingsChanged = new();
        private bool _initialized;
        private bool _disposed;
        private bool _deviceHooked;
        private bool _pausedApplied;
        private bool _debugBlocked;
        /// <summary>入力待ち中の割り当て変更 (Dispose でキャンセルする)</summary>
        private RebindSession _activeSession;
        private bool _debugHooked;
        /// <summary>このサービスが割り当てを変更する前の状態 (null = まだ変更していない)</summary>
        private string _overridesSnapshot;

        public InputActionAsset Actions => _asset;
        public ReadOnlyReactiveProperty<InputDeviceKind> CurrentDevice => _devices != null ? _devices.CurrentDevice : _device;
        public ReadOnlyReactiveProperty<InputDeviceKind> CurrentScheme => _devices != null ? _devices.CurrentScheme : _scheme;
        public ReadOnlyReactiveProperty<bool> IsRebinding => _rebinding;
        public Observable<Unit> BindingsChanged => _bindingsChanged;

        public ReadOnlyReactiveProperty<Vector2> Move => ReadValue<Vector2>(_settings.Move);
        public ReadOnlyReactiveProperty<Vector2> Look => ReadValue<Vector2>(_settings.Look);
        public Observable<Unit> Jump => OnPerformed(_settings.Jump);
        public Observable<Unit> Attack => OnPerformed(_settings.Attack);
        public Observable<Unit> Interact => OnPerformed(_settings.Interact);
        public Observable<Unit> Submit => OnPerformed(_settings.Submit);
        public Observable<Unit> Cancel => OnPerformed(_settings.Cancel);

        /// <param name="save">割り当て変更の保存先。無ければ保存しない</param>
        /// <param name="pause">PauseDisabledMaps を使う場合に必要</param>
        /// <param name="settings">無ければ Resources/AbubuInputSettings、それも無ければ既定値</param>
        /// <param name="devices">
        /// 使用中デバイスの判定 (任意)。あれば <see cref="CurrentDevice"/> はその値になる。無ければアクションが反応したデバイスで判定する
        /// </param>
        public InputService(
            [InjectOptional] ISaveService save = null,
            [InjectOptional] IPauseService pause = null,
            [InjectOptional] AbubuInputSettings settings = null,
            [InjectOptional] IInputDeviceService devices = null)
        {
            _save = save;
            _pause = pause;
            _devices = devices;
            if (settings == null) settings = Resources.Load<AbubuInputSettings>(AbubuInputSettings.ResourcePath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
                settings.hideFlags = HideFlags.HideAndDontSave;
                _ownsSettings = true;
            }
            _settings = settings;

            if (_settings.Enabled) _asset = _settings.Actions != null ? _settings.Actions : InputSystem.actions;
        }

        public void Initialize()
        {
            if (_initialized || _disposed) return;
            _initialized = true;

            if (!_settings.Enabled) return;
            if (_asset == null)
            {
                if (_settings.StrictActionLookup)
                {
                    Debug.LogWarning(LogPrefix + "Input Actions が見つかりません。Project Settings > Input System Package で Project-wide Actions を設定するか、AbubuInputSettings.Actions を指定してください。");
                }
                return;
            }

            if (_devices == null)
            {
                foreach (var map in _asset.actionMaps) map.actionTriggered += OnActionTriggered;
                _deviceHooked = true;
            }

            if (_settings.LoadBindingOverridesOnStart) LoadSavedOverrides();

            foreach (var mapName in _settings.EnabledMaps)
            {
                var map = FindMap(mapName);
                if (map != null) Apply(Track(map), true);
            }

            if (_pause != null && _settings.PauseDisabledMaps.Count > 0)
            {
                _pause.IsPaused.Subscribe(this, static (paused, self) => self.OnPauseChanged(paused)).AddTo(_disposables);
            }

            if (_settings.BlockGameplayWhileDebugOverlayFocused)
            {
                DebugOverlay.GamepadFocusChanged += OnDebugOverlayFocusChanged;
                _debugHooked = true;
                if (DebugOverlay.HasGamepadFocus) OnDebugOverlayFocusChanged(true);
            }
        }

        // ---------------------------------------------------------------- アクション

        public InputAction GetAction(string actionPath) => FindAction(actionPath);

        public Observable<InputAction.CallbackContext> Performed(string actionPath)
        {
            actionPath ??= string.Empty;
            if (_performed.TryGetValue(actionPath, out var subject)) return subject;

            subject = new Subject<InputAction.CallbackContext>();
            _performed.Add(actionPath, subject);
            _ownedObservables.Add(subject);
            var action = FindAction(actionPath);
            if (action != null) AddHandler(action, ctx => subject.OnNext(ctx), performedOnly: true);
            return subject;
        }

        public Observable<Unit> OnPerformed(string actionPath)
        {
            actionPath ??= string.Empty;
            if (_performedUnit.TryGetValue(actionPath, out var observable)) return observable;

            observable = Performed(actionPath).Select(static _ => Unit.Default);
            _performedUnit.Add(actionPath, observable);
            return observable;
        }

        public ReadOnlyReactiveProperty<T> ReadValue<T>(string actionPath) where T : struct
        {
            actionPath ??= string.Empty;
            var key = (actionPath, typeof(T));
            if (_values.TryGetValue(key, out var cached)) return (ReadOnlyReactiveProperty<T>)cached;

            var property = new ReactiveProperty<T>(default);
            _values.Add(key, property);
            _ownedObservables.Add(property);
            var action = FindAction(actionPath);
            if (action != null)
            {
                AddHandler(action, ctx => property.Value = ctx.canceled ? default : ctx.ReadValue<T>(), performedOnly: false);
                _resetters.Add((action, () => property.Value = default));
            }
            return property;
        }

        public ReadOnlyReactiveProperty<bool> IsPressed(string actionPath)
        {
            actionPath ??= string.Empty;
            if (_pressed.TryGetValue(actionPath, out var property)) return property;

            property = new ReactiveProperty<bool>(false);
            _pressed.Add(actionPath, property);
            _ownedObservables.Add(property);
            var action = FindAction(actionPath);
            if (action != null)
            {
                AddHandler(action, ctx => property.Value = ctx.action.IsPressed(), performedOnly: false);
                _resetters.Add((action, () => property.Value = false));
            }
            return property;
        }

        // ---------------------------------------------------------------- ActionMap

        public void SetMapEnabled(string mapName, bool enabled)
        {
            var map = FindMap(mapName);
            if (map == null) return;

            var state = Track(map);
            if (enabled && state.Blocks != BlockReason.None)
            {
                // ポーズ中などは有効にせず、解除時に有効にする
                state.ResumeOnUnpause = true;
                return;
            }

            state.ResumeOnUnpause = false;
            Apply(state, enabled);
        }

        public void SetGameplayEnabled(bool enabled)
        {
            if (!string.IsNullOrEmpty(_settings.GameplayMap)) SetMapEnabled(_settings.GameplayMap, enabled);
        }

        private void OnPauseChanged(bool paused)
        {
            if (paused == _pausedApplied) return;
            _pausedApplied = paused;
            foreach (var mapName in _settings.PauseDisabledMaps) SetBlocked(FindMap(mapName), BlockReason.Pause, paused);
        }

        private void OnDebugOverlayFocusChanged(bool focused)
        {
            if (_disposed || focused == _debugBlocked) return;
            _debugBlocked = focused;
            SetBlocked(FindMap(_settings.GameplayMap), BlockReason.DebugOverlay, focused);
            foreach (var mapName in _settings.PauseDisabledMaps) SetBlocked(FindMap(mapName), BlockReason.DebugOverlay, focused);
        }

        /// <summary>理由ごとに ActionMap を無効にする / 戻す。すべての理由が解消したら、無効にする前の状態 (有効) に戻す</summary>
        private void SetBlocked(InputActionMap map, BlockReason reason, bool blocked)
        {
            if (map == null) return;
            var state = Track(map);
            var wasBlocked = state.Blocks != BlockReason.None;
            if (blocked) state.Blocks |= reason;
            else state.Blocks &= ~reason;
            var isBlocked = state.Blocks != BlockReason.None;
            if (wasBlocked == isBlocked) return;

            if (isBlocked)
            {
                // 元から無効のマップは、解除時にも有効にしない
                if (!map.enabled) return;
                state.ResumeOnUnpause = true;
                Apply(state, false);
            }
            else if (state.ResumeOnUnpause)
            {
                state.ResumeOnUnpause = false;
                if (!map.enabled) Apply(state, true);
            }
        }

        private MapState Track(InputActionMap map)
        {
            if (!_mapStates.TryGetValue(map, out var state))
            {
                state = new MapState { Map = map, Original = map.enabled, LastApplied = map.enabled };
                _mapStates.Add(map, state);
            }
            return state;
        }

        private void Apply(MapState state, bool enabled)
        {
            if (enabled)
            {
                state.Map.Enable();
            }
            else
            {
                state.Map.Disable();
                foreach (var (action, reset) in _resetters)
                {
                    if (action.actionMap == state.Map) reset();
                }
            }
            state.LastApplied = enabled;
        }

        // ---------------------------------------------------------------- 割り当て変更

        public async UniTask<bool> RebindAsync(string actionPath, int bindingIndex = 0, CancellationToken ct = default)
        {
            var action = FindAction(actionPath);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return false;
            if (ct.IsCancellationRequested || _rebinding.Value) return false;

            CaptureOverrides();
            var wasEnabled = action.enabled;
            action.Disable();
            _rebinding.Value = true;
            bool result;
            try
            {
                if (action.bindings[bindingIndex].isComposite)
                {
                    result = await RebindCompositeAsync(action, bindingIndex, ct);
                }
                else
                {
                    result = await RebindSingleAsync(action, bindingIndex, ct);
                }
            }
            finally
            {
                if (wasEnabled && !_disposed) action.Enable();
                if (!_disposed) _rebinding.Value = false;
            }

            if (result)
            {
                SaveOverrides();
                NotifyBindingsChanged();
            }
            return result;
        }

        /// <summary>複合バインディングの各パートを順に割り当てる。途中でキャンセルされたら、変更したパートを元に戻す</summary>
        private async UniTask<bool> RebindCompositeAsync(InputAction action, int compositeIndex, CancellationToken ct)
        {
            var previous = new List<(int index, string overridePath)>();
            for (var i = compositeIndex + 1; i < action.bindings.Count && action.bindings[i].isPartOfComposite; i++)
            {
                previous.Add((i, action.bindings[i].overridePath));
                if (await RebindSingleAsync(action, i, ct)) continue;

                foreach (var (index, overridePath) in previous)
                {
                    if (overridePath == null) action.RemoveBindingOverride(index);
                    else action.ApplyBindingOverride(index, overridePath);
                }
                return false;
            }
            return previous.Count > 0;
        }

        /// <summary>RebindingOperation には渡さず、<see cref="RebindSession"/> が自分で扱うポインターのボタン</summary>
        private static readonly string[] PointerButtonPaths =
        {
            "<Mouse>/leftButton", "<Mouse>/rightButton", "<Mouse>/middleButton", "<Mouse>/forwardButton", "<Mouse>/backButton",
            "<Pointer>/press", "<Pen>", "<Touchscreen>",
        };

        private async UniTask<bool> RebindSingleAsync(InputAction action, int bindingIndex, CancellationToken ct)
        {
            var required = _settings.RebindMatchBindingDevice ? RequiredKind(action.bindings[bindingIndex].effectivePath) : null;
            var guard = new RebindInputGuard(_settings.RebindCancelPaths, required, _settings.RebindAllowMouseButtons, _settings.RebindExcludedPaths);
            var session = new RebindSession(action, bindingIndex, guard);

            // キャンセル操作は RebindingOperation の外 (RebindSession) で全デバイスを見て判定するので、ここでは設定しない
            var operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough((string)null)
                .OnComplete(_ => session.Completion.TrySetResult(true))
                .OnCancel(_ => session.Completion.TrySetResult(session.PointerPath != null));
            session.Operation = operation;

            if (required.HasValue) RestrictToBindingDevice(operation, required.Value);
            foreach (var path in _settings.RebindExcludedPaths)
            {
                if (!string.IsNullOrEmpty(path)) operation.WithControlsExcluding(path);
            }
            foreach (var path in _settings.RebindCancelPaths)
            {
                if (!string.IsNullOrEmpty(path)) operation.WithControlsExcluding(path);
            }
            foreach (var path in PointerButtonPaths) operation.WithControlsExcluding(path);
            if (_settings.RebindTimeout > 0f) operation.WithTimeout(_settings.RebindTimeout);

            // RebindingOperation より先に onEvent に登録し、キャンセル操作・受け付けないボタンを先に処理する
            session.Hook();
            _activeSession = session;
            try
            {
                operation.Start();
                using (ct.Register(static state => ((RebindSession)state).Cancel(), session))
                {
                    return await session.Completion.Task;
                }
            }
            finally
            {
                session.Unhook();
                if (_activeSession == session) _activeSession = null;
                // キャンセルは RebindingOperation の onEvent と同じ処理の途中で起きるので、Dispose は次のフレームに行う
                DisposeNextFrameAsync(operation).Forget();
            }
        }

        private static async UniTaskVoid DisposeNextFrameAsync(InputActionRebindingExtensions.RebindingOperation operation)
        {
            await UniTask.Yield();
            operation.Dispose();
        }

        /// <summary>割り当てを変更できるデバイスの種類 (元の割り当てから判定。判定できなければ null = 制限しない)</summary>
        private static InputDeviceKind? RequiredKind(string path)
        {
            var kind = InputBindingUtility.GetDeviceKind(path);
            return kind == InputDeviceKind.Other ? null : kind;
        }

        /// <summary>元の割り当てと同じ種類のデバイスの操作だけを受け付ける</summary>
        private static void RestrictToBindingDevice(InputActionRebindingExtensions.RebindingOperation operation, InputDeviceKind kind)
        {
            switch (kind)
            {
                case InputDeviceKind.KeyboardMouse:
                    operation.WithControlsHavingToMatchPath("<Keyboard>");
                    operation.WithControlsHavingToMatchPath("<Mouse>");
                    break;
                case InputDeviceKind.Gamepad:
                    operation.WithControlsHavingToMatchPath("<Gamepad>");
                    break;
                case InputDeviceKind.Joystick:
                    operation.WithControlsHavingToMatchPath("<Joystick>");
                    break;
                case InputDeviceKind.Touch:
                    operation.WithControlsHavingToMatchPath("<Touchscreen>");
                    break;
                case InputDeviceKind.XR:
                    operation.WithControlsHavingToMatchPath("<TrackedDevice>");
                    break;
            }
        }

        /// <summary>
        /// 割り当て変更 1 回分の入力の監視。RebindingOperation の外で全デバイスのボタンを見て、
        /// キャンセル操作ならキャンセル、受け付けないデバイスのボタンは捨てる (UI・ゲームに届けない)、
        /// マウスのボタンは UI のクリックを妨げないよう離して 1 フレーム後に割り当てる。
        /// </summary>
        private sealed class RebindSession
        {
            public readonly UniTaskCompletionSource<bool> Completion = new();
            public InputActionRebindingExtensions.RebindingOperation Operation;
            /// <summary>マウスのボタンで割り当てた場合のパス (OnCancel で成功扱いにする)</summary>
            public string PointerPath;

            private readonly InputAction _action;
            private readonly int _bindingIndex;
            private readonly RebindInputGuard _guard;
            private readonly Action<InputEventPtr, InputDevice> _handler;
            private ButtonControl _pendingPointer;
            private string _pendingPointerPath;
            private bool _hooked;
            private bool _done;

            public RebindSession(InputAction action, int bindingIndex, RebindInputGuard guard)
            {
                _action = action;
                _bindingIndex = bindingIndex;
                _guard = guard;
                _handler = OnEvent;
            }

            public void Hook()
            {
                if (_hooked) return;
                InputSystem.onEvent += _handler;
                _hooked = true;
            }

            public void Unhook()
            {
                _done = true;
                if (!_hooked) return;
                InputSystem.onEvent -= _handler;
                _hooked = false;
            }

            public void Cancel()
            {
                if (_done) return;
                if (Operation != null && Operation.started) Operation.Cancel();
                else Completion.TrySetResult(false);
            }

            private void OnEvent(InputEventPtr eventPtr, InputDevice device)
            {
                if (_done || device == null) return;
                var type = eventPtr.type;
                if (type != StateEvent.Type && type != DeltaStateEvent.Type) return;

                if (_pendingPointer != null && _pendingPointer.device == device && _pendingPointer.ReadValueFromEvent(eventPtr, out var value) &&
                    value < _pendingPointer.pressPointOrDefault)
                {
                    FinishPointerAsync().Forget();
                }

                var kind = InputDeviceClassifier.GetKind(device);
                var isPointer = device is Pointer;
                var suppress = false;
                var threshold = InputSystem.settings.defaultButtonPressPoint;
                foreach (var control in eventPtr.EnumerateControls(InputControlExtensions.Enumerate.IgnoreControlsInDefaultState, device, threshold))
                {
                    if (control is not ButtonControl || !control.HasValueChangeInEvent(eventPtr)) continue;

                    var isMouseButton = device is Mouse && control.parent == device && control is ButtonControl;
                    var path = "<" + (device is Mouse ? "Mouse" : device.layout) + ">/" + RelativePath(control);
                    switch (_guard.Decide(kind, isPointer, isMouseButton, path, control))
                    {
                        case RebindPressAction.Cancel:
                            eventPtr.handled = true;
                            Cancel();
                            return;
                        case RebindPressAction.Suppress:
                            suppress = true;
                            break;
                        case RebindPressAction.PointerCandidate:
                            if (_pendingPointer == null)
                            {
                                _pendingPointer = (ButtonControl)control;
                                _pendingPointerPath = path;
                            }
                            break;
                    }
                }
                if (suppress) eventPtr.handled = true;
            }

            /// <summary>マウスのボタンを離した 1 フレーム後、キャンセル (キャンセルボタンのクリックなど) されていなければ割り当てる</summary>
            private async UniTaskVoid FinishPointerAsync()
            {
                var path = _pendingPointerPath;
                _pendingPointer = null;
                _pendingPointerPath = null;
                await UniTask.DelayFrame(1, PlayerLoopTiming.PostLateUpdate);
                if (_done || Operation == null || !Operation.started || Operation.completed || Operation.canceled) return;

                _action.ApplyBindingOverride(_bindingIndex, path);
                PointerPath = path;
                Operation.Cancel();
            }

            private static string RelativePath(InputControl control)
            {
                var full = control.path;
                var devicePath = control.device.path;
                return full.Length > devicePath.Length + 1 ? full.Substring(devicePath.Length + 1) : control.name;
            }
        }

        public string GetBindingDisplayString(string actionPath, int bindingIndex = 0)
        {
            var action = FindAction(actionPath);
            return action != null && bindingIndex >= 0 && bindingIndex < action.bindings.Count
                ? action.GetBindingDisplayString(bindingIndex)
                : string.Empty;
        }

        public void ResetBindings()
        {
            if (_asset == null) return;
            CaptureOverrides();
            _asset.RemoveAllBindingOverrides();
            if (_save != null && !string.IsNullOrEmpty(_settings.SaveKey)) _save.Delete(_settings.SaveKey);
            NotifyBindingsChanged();
        }

        public void ResetBinding(string actionPath, int bindingIndex)
        {
            var action = FindAction(actionPath);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return;

            CaptureOverrides();
            action.RemoveBindingOverride(bindingIndex);
            if (action.bindings[bindingIndex].isComposite)
            {
                for (var i = bindingIndex + 1; i < action.bindings.Count && action.bindings[i].isPartOfComposite; i++) action.RemoveBindingOverride(i);
            }
            SaveOverrides();
            NotifyBindingsChanged();
        }

        public void SetBindingOverride(string actionPath, int bindingIndex, string overridePath)
        {
            var action = FindAction(actionPath);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return;

            CaptureOverrides();
            if (overridePath == null) action.RemoveBindingOverride(bindingIndex);
            else action.ApplyBindingOverride(bindingIndex, overridePath);
            NotifyBindingsChanged();
        }

        public void SaveBindings()
        {
            if (_asset != null && !_disposed) SaveOverrides();
        }

        private void NotifyBindingsChanged()
        {
            if (!_disposed) _bindingsChanged.OnNext(Unit.Default);
        }

        private void LoadSavedOverrides()
        {
            if (_save == null || string.IsNullOrEmpty(_settings.SaveKey)) return;

            var json = _save.Load(_settings.SaveKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            CaptureOverrides();
            try
            {
                _asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + $"保存された割り当てを読み込めませんでした: {e.Message}");
            }
        }

        private void SaveOverrides()
        {
            if (_save == null || string.IsNullOrEmpty(_settings.SaveKey)) return;
            _save.Save(_settings.SaveKey, _asset.SaveBindingOverridesAsJson());
        }

        /// <summary>このサービスが初めて割り当てを変更する直前に、元の状態を覚えておく</summary>
        private void CaptureOverrides()
        {
            if (_overridesSnapshot == null && _asset != null) _overridesSnapshot = _asset.SaveBindingOverridesAsJson() ?? string.Empty;
        }

        // ---------------------------------------------------------------- 共通

        private void AddHandler(InputAction action, Action<InputAction.CallbackContext> handler, bool performedOnly)
        {
            action.performed += handler;
            if (!performedOnly) action.canceled += handler;
            _handlers.Add((action, handler, performedOnly));
        }

        private InputAction FindAction(string actionPath)
        {
            if (_asset == null || _disposed || string.IsNullOrEmpty(actionPath)) return null;
            var action = _asset.FindAction(actionPath);
            if (action == null && _settings.StrictActionLookup) Debug.LogWarning(LogPrefix + $"アクション \"{actionPath}\" が見つかりません。");
            return action;
        }

        private InputActionMap FindMap(string mapName)
        {
            if (_asset == null || _disposed || string.IsNullOrEmpty(mapName)) return null;
            var map = _asset.FindActionMap(mapName);
            if (map == null && _settings.StrictActionLookup) Debug.LogWarning(LogPrefix + $"ActionMap \"{mapName}\" が見つかりません。");
            return map;
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            var device = context.control?.device;
            if (device == null) return;
            var kind = InputDeviceClassifier.GetKind(device);
            if (kind == InputDeviceKind.Other) return;
            _device.Value = kind;
            var scheme = InputDeviceClassifier.ToScheme(kind);
            if (scheme.HasValue) _scheme.Value = scheme.Value;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _activeSession?.Cancel();
            _activeSession = null;
            _disposables.Dispose();
            if (_debugHooked) DebugOverlay.GamepadFocusChanged -= OnDebugOverlayFocusChanged;
            _debugHooked = false;
            foreach (var (action, handler, performedOnly) in _handlers)
            {
                action.performed -= handler;
                if (!performedOnly) action.canceled -= handler;
            }
            _handlers.Clear();
            _resetters.Clear();

            if (_asset != null)
            {
                if (_deviceHooked)
                {
                    foreach (var map in _asset.actionMaps) map.actionTriggered -= OnActionTriggered;
                }

                // 自分が変えたマップだけ元に戻す。その後に他から変えられたものはそのままにする
                foreach (var state in _mapStates.Values)
                {
                    if (state.Map.enabled != state.LastApplied || state.LastApplied == state.Original) continue;
                    if (state.Original) state.Map.Enable();
                    else state.Map.Disable();
                }

                if (_settings.RevertBindingOverridesOnDispose && _overridesSnapshot != null)
                {
                    if (_overridesSnapshot.Length == 0) _asset.RemoveAllBindingOverrides();
                    else _asset.LoadBindingOverridesFromJson(_overridesSnapshot);
                }
            }
            _mapStates.Clear();
            _disposed = true;

            foreach (var observable in _ownedObservables) observable.Dispose();
            _ownedObservables.Clear();
            _performed.Clear();
            _performedUnit.Clear();
            _values.Clear();
            _pressed.Clear();
            _device.Dispose();
            _scheme.Dispose();
            _rebinding.Dispose();
            _bindingsChanged.Dispose();

            if (_ownsSettings)
            {
                if (Application.isPlaying) Object.Destroy(_settings);
                else Object.DestroyImmediate(_settings);
            }
        }
    }

    /// <summary>
    /// Input System 導入時に AbubuInstaller から自動で呼ばれ、入力モジュールのサービスをまとめてバインドする。
    /// 何も設定しなくても動く (Resources/AbubuInputSettings が無ければ既定値)。
    /// <code>
    /// // 使う側は受け取るだけ (または GameInput の static ショートカット)
    /// [Inject] IInputService _input; [Inject] IInputDeviceService _devices; [Inject] IHapticsService _haptics; [Inject] IInputGlyphService _glyphs;
    /// </code>
    /// バインドするもの (すでにバインドされている型はスキップするので、先にバインドすれば差し替えられる):
    /// <list type="bullet">
    /// <item><see cref="IInputActivitySource"/> → <see cref="InputSystemActivitySource"/></item>
    /// <item><see cref="IInputDeviceConnectionSource"/> → <see cref="InputSystemDeviceConnectionSource"/> (ゲームパッドの抜き差し)</item>
    /// <item><see cref="IInputDeviceService"/> → <see cref="InputDeviceTracker"/></item>
    /// <item><see cref="IInputService"/> → <see cref="InputService"/></item>
    /// <item><see cref="IRumbleBackend"/> → <see cref="GamepadRumbleBackend"/>、<see cref="IHapticsService"/> → <see cref="HapticsService"/></item>
    /// <item><see cref="IInputGlyphService"/> → <see cref="InputGlyphService"/></item>
    /// <item><see cref="InputCursorAutoHide"/> (AbubuInputSettings.AutoHideCursor がオフなら何もしない)</item>
    /// <item><see cref="GamepadDisconnectPauser"/> (AbubuInputSettings.PauseOnGamepadDisconnect がオフなら何もしない)</item>
    /// </list>
    /// 次の場合はインストールしない:
    /// <list type="bullet">
    /// <item>Scripting Define Symbols に ABUBU_DISABLE_INPUT_MODULE がある</item>
    /// <item>Resources/AbubuInputSettings の Enabled がオフ</item>
    /// </list>
    /// DI で AbubuInputSettings をバインドしている場合はそちらが使われる (Enabled がオフなら各サービスは何もしない)。
    /// </summary>
    [Preserve]
    public sealed class InputModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public InputModuleInstaller() { }

#if ABUBU_DISABLE_INPUT_MODULE
        public void Install(DiContainer container) { }
#else
        public void Install(DiContainer container)
        {
            // DI でバインドされた設定は、インストール中に Resolve すると Zenject が警告を出すため、ここでは見ない
            if (!container.HasBinding<AbubuInputSettings>())
            {
                var settings = AbubuInputSettings.FindDefault();
                if (settings != null && !settings.Enabled) return;
            }

            if (!container.HasBinding<IInputActivitySource>()) container.BindInterfacesTo<InputSystemActivitySource>().AsSingle();
            if (!container.HasBinding<IInputDeviceConnectionSource>()) container.BindInterfacesTo<InputSystemDeviceConnectionSource>().AsSingle();
            if (!container.HasBinding<IInputDeviceService>()) container.BindInterfacesTo<InputDeviceTracker>().AsSingle();
            if (!container.HasBinding<IInputService>()) container.BindInterfacesTo<InputService>().AsSingle();
            if (!container.HasBinding<IRumbleBackend>()) container.BindInterfacesTo<GamepadRumbleBackend>().AsSingle();
            if (!container.HasBinding<IHapticsService>()) container.BindInterfacesTo<HapticsService>().AsSingle();
            if (!container.HasBinding<IInputGlyphService>()) container.BindInterfacesTo<InputGlyphService>().AsSingle();
            if (!container.HasBinding<InputCursorAutoHide>()) container.BindInterfacesAndSelfTo<InputCursorAutoHide>().AsSingle();
            if (!container.HasBinding<GamepadDisconnectPauser>()) container.BindInterfacesAndSelfTo<GamepadDisconnectPauser>().AsSingle();
        }
#endif
    }
}
