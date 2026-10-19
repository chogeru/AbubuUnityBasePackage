using System;
using System.Collections.Generic;
using R3;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using Zenject;

namespace Abubu.Inputs
{
    /// <summary>
    /// Input System のイベント (InputSystem.onEvent) からデバイスの操作を拾う、既定の <see cref="IInputActivitySource"/>。
    /// アクションに割り当てられていない操作 (どのボタンでも) で使用中のデバイスが切り替わる。
    /// <code>
    /// // 通常は InputModuleInstaller が自動でバインドする。差し替える場合は先にバインドしておく
    /// Container.BindInterfacesTo&lt;MyActivitySource&gt;().AsSingle();
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>ゲームパッド・ジョイスティック: ActuationThreshold 以上動いたボタン・スティック・トリガー</item>
    /// <item>キーボード・タッチ・XR・ペン: ボタン (キー・タッチ) の押下だけ (位置・姿勢の変化は数えない)</item>
    /// <item>マウス: ボタン押下、または移動量 (ピクセル)。移動量のしきい値は <see cref="InputDeviceTracker"/> が判定する</item>
    /// <item>センサー (加速度計など) やノイズ扱いのコントロールは無視する</item>
    /// </list>
    /// </remarks>
    public sealed class InputSystemActivitySource : IInputActivitySource, IInitializable, IDisposable
    {
        private readonly Subject<InputActivity> _activity = new();
        private readonly float _threshold;
        private readonly Action<InputEventPtr, InputDevice> _handler;
        /// <summary>ゲームパッドの種類の判定結果 (deviceId ごと。イベントのたびに判定しないため)</summary>
        private readonly Dictionary<int, GamepadKind> _gamepadKinds = new();
        private bool _hooked;
        private bool _disposed;

        public Observable<InputActivity> Activity => _activity;

        /// <inheritdoc />
        /// <remarks>タッチ端末でキーボードが無ければ Touch、キーボードが無くゲームパッドがあれば Gamepad、それ以外はキーボード&マウス</remarks>
        public InputDeviceState InitialState
        {
            get
            {
                var gamepad = Gamepad.current;
                var gamepadKind = InputDeviceClassifier.GetGamepadKind(gamepad);
                if (Keyboard.current == null)
                {
                    if (Touchscreen.current != null && gamepad == null) return new InputDeviceState(InputDeviceKind.Touch, gamepadKind);
                    if (gamepad != null) return new InputDeviceState(InputDeviceKind.Gamepad, gamepadKind);
                }
                return new InputDeviceState(InputDeviceKind.KeyboardMouse, gamepadKind);
            }
        }

        /// <param name="settings">無ければ Resources/AbubuInputSettings、それも無ければ既定値</param>
        public InputSystemActivitySource([InjectOptional] AbubuInputSettings settings = null)
        {
            settings ??= AbubuInputSettings.FindDefault();
            _threshold = settings != null ? settings.DeviceSwitch.ActuationThreshold : new InputDeviceSwitchSettings().ActuationThreshold;
            // 0 だと変化の無いコントロールまで拾うので、最低限のしきい値を設ける
            if (_threshold < 0.01f) _threshold = 0.01f;
            _handler = OnEvent;
        }

        public void Initialize()
        {
            if (_hooked || _disposed) return;
            InputSystem.onEvent += _handler;
            _hooked = true;
        }

        private void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device == null || device is Sensor) return;
            var type = eventPtr.type;
            if (type != StateEvent.Type && type != DeltaStateEvent.Type) return;

            var kind = InputDeviceClassifier.GetKind(device);
            var time = eventPtr.time;

            if (device is Mouse mouse)
            {
                if (eventPtr.HasButtonPress())
                {
                    Emit(kind, GamepadKind.Generic, 1f, time, false, device);
                }
                else if (mouse.delta.ReadValueFromEvent(eventPtr, out var delta))
                {
                    var magnitude = delta.magnitude;
                    if (magnitude > 0f) Emit(kind, GamepadKind.Generic, magnitude, time, true, device);
                }
                return;
            }

            // スティック・トリガーまで数えるのはゲームパッドとジョイスティックだけ。それ以外は位置・姿勢などが常に変わるのでボタンだけ見る
            var analog = kind == InputDeviceKind.Gamepad || kind == InputDeviceKind.Joystick;
            foreach (var control in eventPtr.EnumerateChangedControls(device, _threshold))
            {
                if (control.noisy || control.synthetic) continue;
                if (!analog && control is not ButtonControl) continue;

                var gamepadKind = kind == InputDeviceKind.Gamepad ? GetGamepadKind(device) : GamepadKind.Generic;
                // しきい値で絞り込み済みなので、操作量は「しきい値以上」= 1 として通知する
                Emit(kind, gamepadKind, 1f, time, false, device);
                return;
            }
        }

        private GamepadKind GetGamepadKind(InputDevice device)
        {
            if (_gamepadKinds.TryGetValue(device.deviceId, out var kind)) return kind;
            kind = InputDeviceClassifier.GetGamepadKind(device);
            _gamepadKinds[device.deviceId] = kind;
            return kind;
        }

        private void Emit(InputDeviceKind kind, GamepadKind gamepad, float magnitude, double time, bool pointer, InputDevice device) =>
            _activity.OnNext(new InputActivity(kind, gamepad, magnitude, time, pointer, device));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hooked) InputSystem.onEvent -= _handler;
            _hooked = false;
            _activity.Dispose();
        }
    }
}
