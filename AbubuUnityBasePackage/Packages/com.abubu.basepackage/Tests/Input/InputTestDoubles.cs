using System.Collections.Generic;
using Abubu.Inputs;
using Abubu.Save;
using R3;
using UnityEngine.InputSystem;

namespace Abubu.Tests
{
    /// <summary>メモリ上だけに保存する ISaveService (保存回数も数える)</summary>
    internal sealed class MemorySaveService : ISaveService
    {
        public readonly Dictionary<string, object> Data = new();
        public int SaveCount;

        public bool Save<T>(string key, T data)
        {
            Data[key] = data;
            SaveCount++;
            return true;
        }

        public bool ForceSave<T>(string key, T data) => Save(key, data);

        public bool TryLoad<T>(string key, out T data)
        {
            if (Data.TryGetValue(key, out var value) && value is T typed)
            {
                data = typed;
                return true;
            }
            data = default;
            return false;
        }

        public T Load<T>(string key, T defaultValue = default) => TryLoad<T>(key, out var data) ? data : defaultValue;
        public bool Exists(string key) => Data.ContainsKey(key);
        public void Delete(string key) => Data.Remove(key);
    }

    /// <summary>テストから操作を流せる IInputActivitySource</summary>
    internal sealed class FakeActivitySource : IInputActivitySource
    {
        public readonly Subject<InputActivity> Subject = new();
        public InputDeviceState Initial = new(InputDeviceKind.KeyboardMouse, GamepadKind.Generic);

        public Observable<InputActivity> Activity => Subject;
        public InputDeviceState InitialState => Initial;

        public void Emit(InputDeviceKind kind, float magnitude, double time, GamepadKind gamepad = GamepadKind.Generic, bool pointer = false) =>
            Subject.OnNext(new InputActivity(kind, gamepad, magnitude, time, pointer));
    }

    /// <summary>テストから現在のデバイス・接続/切断を設定できる IInputDeviceService</summary>
    internal sealed class FakeDeviceService : IInputDeviceService
    {
        private readonly ReactiveProperty<InputDeviceState> _current = new(new InputDeviceState(InputDeviceKind.KeyboardMouse, GamepadKind.Generic));
        private readonly ReactiveProperty<InputDeviceKind> _kind = new(InputDeviceKind.KeyboardMouse);
        private readonly ReactiveProperty<InputDeviceKind> _scheme = new(InputDeviceKind.KeyboardMouse);
        private readonly ReactiveProperty<GamepadKind> _gamepad = new(GamepadKind.Generic);
        public readonly Subject<InputDeviceConnection> Lost = new();
        public readonly Subject<InputDeviceConnection> Regained = new();

        public ReadOnlyReactiveProperty<InputDeviceState> Current => _current;
        public ReadOnlyReactiveProperty<InputDeviceKind> CurrentDevice => _kind;
        public ReadOnlyReactiveProperty<InputDeviceKind> CurrentScheme => _scheme;
        public ReadOnlyReactiveProperty<GamepadKind> CurrentGamepadKind => _gamepad;
        public Gamepad CurrentGamepad => null;
        public Observable<InputDeviceConnection> DeviceLost => Lost;
        public Observable<InputDeviceConnection> DeviceRegained => Regained;

        public void Set(InputDeviceKind kind, GamepadKind gamepad = GamepadKind.Generic)
        {
            _current.Value = new InputDeviceState(kind, gamepad);
            _kind.Value = kind;
            var scheme = InputDeviceClassifier.ToScheme(kind);
            if (scheme.HasValue) _scheme.Value = scheme.Value;
            _gamepad.Value = gamepad;
        }
    }

    /// <summary>テストから接続・切断を流せる IInputDeviceConnectionSource</summary>
    internal sealed class FakeConnectionSource : IInputDeviceConnectionSource
    {
        public readonly Subject<InputDeviceConnection> Subject = new();
        public int GamepadCount;

        public Observable<InputDeviceConnection> Changed => Subject;
        public int ConnectedGamepadCount => GamepadCount;

        /// <summary>ゲームパッドを接続する (台数も増やす)</summary>
        public void ConnectGamepad()
        {
            GamepadCount++;
            Subject.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, true));
        }

        /// <summary>ゲームパッドを切断する (台数も減らす)</summary>
        public void DisconnectGamepad()
        {
            if (GamepadCount > 0) GamepadCount--;
            Subject.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, false));
        }
    }

    /// <summary>最後に設定されたモーターの強さを覚えておく IRumbleBackend</summary>
    internal sealed class FakeRumbleBackend : IRumbleBackend
    {
        public bool Available = true;
        public bool Supports = true;
        public float Low;
        public float High;
        public int CallCount;

        public bool IsAvailable => Available;
        public bool SupportsRumble => Supports;

        public void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            Low = lowFrequency;
            High = highFrequency;
            CallCount++;
        }
    }
}
