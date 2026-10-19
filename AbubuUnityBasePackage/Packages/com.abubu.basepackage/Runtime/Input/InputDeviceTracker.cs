using System;
using R3;
using UnityEngine.InputSystem;
using Zenject;

namespace Abubu.Inputs
{
    /// <summary>
    /// <see cref="IInputDeviceService"/> の標準実装。<see cref="IInputActivitySource"/> から届く操作を見て、使用中のデバイスを切り替える。
    /// <code>
    /// // 通常は InputModuleInstaller が自動でバインドするので、受け取って使うだけ
    /// [Inject] IInputDeviceService _devices;
    /// _devices.CurrentScheme.Subscribe(kind => glyph.sprite = kind == InputDeviceKind.Gamepad ? pad : key).AddTo(this);
    /// </code>
    /// </summary>
    /// <remarks>
    /// 切り替えの規則 (<see cref="InputDeviceSwitchSettings"/>):
    /// <list type="number">
    /// <item>操作量が ActuationThreshold 未満の操作は無視する (ドリフト・ノイズ対策)。
    /// マウス移動は PointerMoveWindow 秒以内の移動量の合計が PointerMoveThreshold 未満なら無視する</item>
    /// <item><see cref="InputDeviceKind.Other"/> (種類の分からないデバイス) の操作では切り替えない</item>
    /// <item>今のデバイスと同じ種類の操作は「まだ使っている」印として最終操作時刻を更新する</item>
    /// <item>別の種類の操作は、今のデバイスの最終操作から HoldTime 以上経っていれば切り替える。経っていなければ無視する
    /// (キーボードとゲームパッドを同時に触ってもチラつかない)</item>
    /// <item>使用中のゲームパッドが切断され、他にゲームパッドが無ければキーボード&amp;マウスに戻す
    /// (<see cref="IInputDeviceConnectionSource"/> がある場合)</item>
    /// </list>
    /// ゲームパッドの種類 (Xbox → PlayStation など) は、ゲームパッド同士なら HoldTime を待たずに更新する。
    /// <see cref="CurrentScheme"/> はキーボード&amp;マウス / ゲームパッドに切り替わったときだけ変わる。
    /// </remarks>
    public sealed class InputDeviceTracker : IInputDeviceService, IInitializable, IDisposable
    {
        private readonly IInputActivitySource _source;
        private readonly IInputDeviceConnectionSource _connections;
        private readonly InputDeviceSwitchSettings _switch;
        private readonly bool _enabled;
        private readonly ReactiveProperty<InputDeviceState> _current;
        private readonly ReactiveProperty<InputDeviceKind> _kind;
        private readonly ReactiveProperty<InputDeviceKind> _scheme;
        private readonly ReactiveProperty<GamepadKind> _gamepadKind;
        private readonly Subject<InputDeviceConnection> _lost = new();
        private readonly Subject<InputDeviceConnection> _regained = new();
        private readonly CompositeDisposable _subscriptions = new();
        private Gamepad _gamepad;
        private double _lastActiveTime;
        private bool _hasActivity;
        private float _pointerAccumulated;
        private double _pointerWindowStart = double.NegativeInfinity;
        private bool _initialized;
        private bool _disposed;

        public ReadOnlyReactiveProperty<InputDeviceState> Current => _current;
        public ReadOnlyReactiveProperty<InputDeviceKind> CurrentDevice => _kind;
        public ReadOnlyReactiveProperty<InputDeviceKind> CurrentScheme => _scheme;
        public ReadOnlyReactiveProperty<GamepadKind> CurrentGamepadKind => _gamepadKind;
        public Observable<InputDeviceConnection> DeviceLost => _lost;
        public Observable<InputDeviceConnection> DeviceRegained => _regained;

        /// <inheritdoc />
        public Gamepad CurrentGamepad
        {
            get
            {
                // 切断されたら忘れる (再接続時は別インスタンスになる場合がある)
                if (_gamepad != null && !_gamepad.added) _gamepad = null;
                return _gamepad;
            }
        }

        /// <param name="source">デバイスの操作の通知元</param>
        /// <param name="settings">無ければ Resources/AbubuInputSettings、それも無ければ既定値</param>
        /// <param name="connections">デバイスの接続・切断の通知元 (任意。無ければ切断を検知しない)</param>
        [Inject]
        public InputDeviceTracker(IInputActivitySource source, [InjectOptional] AbubuInputSettings settings = null,
            [InjectOptional] IInputDeviceConnectionSource connections = null)
            : this(source, settings ?? AbubuInputSettings.FindDefault(), connections, 0)
        {
        }

        private InputDeviceTracker(IInputActivitySource source, AbubuInputSettings settings, IInputDeviceConnectionSource connections, int _)
            : this(source, settings != null ? settings.DeviceSwitch : null, settings == null || settings.Enabled, connections)
        {
        }

        /// <param name="source">デバイスの操作の通知元</param>
        /// <param name="switchSettings">切り替え条件 (null なら既定値)</param>
        /// <param name="enabled">false なら何も購読しない (初期状態のまま)</param>
        /// <param name="connections">デバイスの接続・切断の通知元 (任意)</param>
        public InputDeviceTracker(IInputActivitySource source, InputDeviceSwitchSettings switchSettings, bool enabled = true,
            IInputDeviceConnectionSource connections = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _connections = connections;
            _switch = switchSettings ?? new InputDeviceSwitchSettings();
            _enabled = enabled;

            var initial = source.InitialState;
            if (initial.Kind == InputDeviceKind.Other) initial = new InputDeviceState(InputDeviceKind.KeyboardMouse, initial.Gamepad);
            _current = new ReactiveProperty<InputDeviceState>(initial);
            _kind = new ReactiveProperty<InputDeviceKind>(initial.Kind);
            _scheme = new ReactiveProperty<InputDeviceKind>(InputDeviceClassifier.ToScheme(initial.Kind) ?? InputDeviceKind.KeyboardMouse);
            _gamepadKind = new ReactiveProperty<GamepadKind>(initial.Gamepad);
            if (initial.Kind == InputDeviceKind.Gamepad) _gamepad = Gamepad.current;
        }

        public void Initialize()
        {
            if (_initialized || _disposed) return;
            _initialized = true;
            if (!_enabled) return;
            _source.Activity.Subscribe(this, static (a, self) => self.OnActivity(a)).AddTo(_subscriptions);
            _connections?.Changed.Subscribe(this, static (c, self) => self.OnConnectionChanged(c)).AddTo(_subscriptions);
        }

        /// <summary>操作 1 回分を処理する (通常は <see cref="IInputActivitySource"/> から呼ばれる)</summary>
        public void OnActivity(in InputActivity activity)
        {
            if (_disposed || activity.Kind == InputDeviceKind.Other || activity.Magnitude <= 0f) return;

            if (activity.IsPointerMotion)
            {
                if (!AccumulatePointer(activity)) return;
            }
            else if (activity.Magnitude < _switch.ActuationThreshold)
            {
                return;
            }

            var current = _current.Value;
            if (activity.Kind == current.Kind)
            {
                _lastActiveTime = activity.Time;
                _hasActivity = true;
                if (activity.Kind == InputDeviceKind.Gamepad) UpdateGamepad(activity, current);
                return;
            }

            // 今のデバイスがまだ使われている間は切り替えない
            if (_hasActivity && activity.Time - _lastActiveTime < _switch.HoldTime) return;

            _lastActiveTime = activity.Time;
            _hasActivity = true;
            if (activity.Kind == InputDeviceKind.Gamepad)
            {
                if (activity.Device is Gamepad gamepad) _gamepad = gamepad;
                Set(new InputDeviceState(InputDeviceKind.Gamepad, activity.Gamepad));
            }
            else
            {
                Set(new InputDeviceState(activity.Kind, current.Gamepad));
            }
        }

        /// <summary>マウス移動量を PointerMoveWindow 秒の間合計し、しきい値に届いたら true</summary>
        private bool AccumulatePointer(in InputActivity activity)
        {
            if (activity.Time < _pointerWindowStart || activity.Time - _pointerWindowStart > _switch.PointerMoveWindow)
            {
                _pointerWindowStart = activity.Time;
                _pointerAccumulated = 0f;
            }
            _pointerAccumulated += activity.Magnitude;
            if (_pointerAccumulated < _switch.PointerMoveThreshold) return false;
            _pointerAccumulated = 0f;
            _pointerWindowStart = double.NegativeInfinity;
            return true;
        }

        /// <summary>デバイスの接続・切断を処理する (通常は <see cref="IInputDeviceConnectionSource"/> から呼ばれる)</summary>
        public void OnConnectionChanged(in InputDeviceConnection connection)
        {
            if (_disposed) return;
            if (connection.Connected)
            {
                _regained.OnNext(connection.WithInUse(false));
                return;
            }

            var current = _current.Value;
            var wasInUse = connection.Kind == current.Kind;
            if (connection.Kind == InputDeviceKind.Gamepad)
            {
                // 使用中のゲームパッドが分かっていれば、それが切断されたときだけ「使用中」とみなす
                var isTracked = connection.Device != null && ReferenceEquals(_gamepad, connection.Device);
                if (_gamepad != null && connection.Device != null) wasInUse &= isTracked;
                if (isTracked) _gamepad = null;

                var remaining = _connections?.ConnectedGamepadCount ?? 0;
                if (current.Kind == InputDeviceKind.Gamepad && remaining == 0)
                {
                    _gamepad = null;
                    _hasActivity = false;
                    Set(new InputDeviceState(InputDeviceKind.KeyboardMouse, current.Gamepad));
                }
            }
            _lost.OnNext(connection.WithInUse(wasInUse));
        }

        private void UpdateGamepad(in InputActivity activity, InputDeviceState current)
        {
            if (activity.Device is Gamepad gamepad) _gamepad = gamepad;
            if (activity.Gamepad != current.Gamepad) Set(new InputDeviceState(InputDeviceKind.Gamepad, activity.Gamepad));
        }

        private void Set(InputDeviceState state)
        {
            _current.Value = state;
            _kind.Value = state.Kind;
            var scheme = InputDeviceClassifier.ToScheme(state.Kind);
            if (scheme.HasValue) _scheme.Value = scheme.Value;
            _gamepadKind.Value = state.Gamepad;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscriptions.Dispose();
            _current.Dispose();
            _kind.Dispose();
            _scheme.Dispose();
            _gamepadKind.Dispose();
            _lost.Dispose();
            _regained.Dispose();
            _gamepad = null;
        }
    }
}
