using System;
using UnityEngine.InputSystem;
using Zenject;

namespace Abubu.Inputs
{
    /// <summary>
    /// ゲームパッドに振動を出す、既定の <see cref="IRumbleBackend"/>。
    /// 最後に使われたゲームパッド (<see cref="IInputDeviceService.CurrentGamepad"/>) に出す。
    /// <code>
    /// // 通常は InputModuleInstaller が自動でバインドする。IHapticsService 経由で使う
    /// GameInput.Rumble(HapticPreset.Medium);
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>IInputDeviceService がある場合、最後に使われたゲームパッドがまだ分からない (切断された) ときは、
    /// つながっているゲームパッドが 1 台だけならそれに出す。複数あるときは出さない (他のプレイヤーのゲームパッドを震わせない)</item>
    /// <item>IInputDeviceService が無ければ Gamepad.current に出す</item>
    /// <item>振動に対応していないゲームパッド (<see cref="GamepadRumbleSupport"/>) には出さない</item>
    /// <item>出力先のゲームパッドが変わったら、前のゲームパッドの振動を止める。Dispose 時も止める</item>
    /// </list>
    /// </remarks>
    public sealed class GamepadRumbleBackend : IRumbleBackend, IDisposable
    {
        private readonly IInputDeviceService _devices;
        private Gamepad _last;

        /// <param name="devices">最後に使われたゲームパッドの取得元 (任意)</param>
        public GamepadRumbleBackend([InjectOptional] IInputDeviceService devices = null) => _devices = devices;

        private Gamepad Target
        {
            get
            {
                if (_devices == null) return Gamepad.current;
                var gamepad = _devices.CurrentGamepad;
                if (gamepad != null) return gamepad;
                return Gamepad.all.Count == 1 ? Gamepad.all[0] : null;
            }
        }

        /// <inheritdoc />
        public bool IsAvailable => SupportsRumble;

        /// <inheritdoc />
        public bool SupportsRumble => GamepadRumbleSupport.IsSupported(Target);

        /// <inheritdoc />
        public void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            var target = Target;
            if (!ReferenceEquals(target, _last))
            {
                ResetLast();
                _last = target;
            }
            if (target != null && GamepadRumbleSupport.IsSupported(target)) target.SetMotorSpeeds(lowFrequency, highFrequency);
        }

        private void ResetLast()
        {
            if (_last != null && _last.added) _last.SetMotorSpeeds(0f, 0f);
            _last = null;
        }

        public void Dispose() => ResetLast();
    }
}
