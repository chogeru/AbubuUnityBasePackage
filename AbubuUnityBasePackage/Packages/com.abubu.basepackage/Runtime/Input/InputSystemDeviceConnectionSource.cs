using System;
using System.Collections.Generic;
using R3;
using UnityEngine.InputSystem;
using Zenject;

namespace Abubu.Inputs
{
    /// <summary>
    /// InputSystem.onDeviceChange からデバイスの接続・切断を拾う、既定の <see cref="IInputDeviceConnectionSource"/>。
    /// <code>
    /// // 通常は InputModuleInstaller が自動でバインドする。使う側は IInputDeviceService.DeviceLost / DeviceRegained を見る
    /// _devices.DeviceLost.Subscribe(c => Debug.Log($"{c.Device} が切断されました")).AddTo(this);
    /// </code>
    /// </summary>
    /// <remarks>
    /// Added / Reconnected を接続、Removed / Disconnected を切断として 1 回ずつ通知する
    /// (Disconnected の後に Removed が来ても 2 回は通知しない)。
    /// </remarks>
    public sealed class InputSystemDeviceConnectionSource : IInputDeviceConnectionSource, IInitializable, IDisposable
    {
        private readonly Subject<InputDeviceConnection> _changed = new();
        private readonly Action<InputDevice, InputDeviceChange> _handler;
        /// <summary>切断を通知済みのデバイス (deviceId)</summary>
        private readonly HashSet<int> _lost = new();
        private bool _hooked;
        private bool _disposed;

        public Observable<InputDeviceConnection> Changed => _changed;

        /// <inheritdoc />
        public int ConnectedGamepadCount
        {
            get
            {
                var count = 0;
                foreach (var gamepad in Gamepad.all)
                {
                    if (gamepad.added && !_lost.Contains(gamepad.deviceId)) count++;
                }
                return count;
            }
        }

        public InputSystemDeviceConnectionSource() => _handler = OnDeviceChange;

        public void Initialize()
        {
            if (_hooked || _disposed) return;
            InputSystem.onDeviceChange += _handler;
            _hooked = true;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device == null || _disposed) return;
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                    _lost.Remove(device.deviceId);
                    _changed.OnNext(new InputDeviceConnection(device, InputDeviceClassifier.GetKind(device), true));
                    break;
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                    if (!_lost.Add(device.deviceId)) return;
                    _changed.OnNext(new InputDeviceConnection(device, InputDeviceClassifier.GetKind(device), false));
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hooked) InputSystem.onDeviceChange -= _handler;
            _hooked = false;
            _changed.Dispose();
        }
    }
}
