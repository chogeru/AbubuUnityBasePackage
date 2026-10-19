using System;

namespace Abubu.Inputs
{
    /// <summary>
    /// ゲームパッドの種類 (ボタンアイコンの出し分け用)。
    /// <code>
    /// if (GameInput.CurrentGamepadKind == GamepadKind.PlayStation) confirmLabel.text = "×";
    /// </code>
    /// </summary>
    public enum GamepadKind
    {
        /// <summary>種類が分からない / 上記以外のゲームパッド (Steam Deck・汎用 HID など)。ゲームパッドが未接続の場合もこれ</summary>
        Generic,
        /// <summary>Xbox 系 (XInput を含む)</summary>
        Xbox,
        /// <summary>PlayStation 系 (DualShock / DualSense)</summary>
        PlayStation,
        /// <summary>Nintendo Switch 系 (Pro コントローラー / Joy-Con)</summary>
        Switch,
    }

    /// <summary>
    /// 現在使われているデバイスの状態。<see cref="Kind"/> がゲームパッド以外でも、
    /// <see cref="Gamepad"/> には最後に使われたゲームパッドの種類が入る (キーボードに持ち替えても忘れない)。
    /// <code>
    /// _devices.Current.Subscribe(s => icon.sprite = s.Kind == InputDeviceKind.Gamepad ? padIcon : keyIcon).AddTo(this);
    /// </code>
    /// </summary>
    public readonly struct InputDeviceState : IEquatable<InputDeviceState>
    {
        /// <summary>最後に操作されたデバイスの種類</summary>
        public readonly InputDeviceKind Kind;
        /// <summary>最後に使われたゲームパッドの種類</summary>
        public readonly GamepadKind Gamepad;

        public InputDeviceState(InputDeviceKind kind, GamepadKind gamepad)
        {
            Kind = kind;
            Gamepad = gamepad;
        }

        /// <summary>ゲームパッドを使用中か</summary>
        public bool IsGamepad => Kind == InputDeviceKind.Gamepad;

        public bool Equals(InputDeviceState other) => Kind == other.Kind && Gamepad == other.Gamepad;
        public override bool Equals(object obj) => obj is InputDeviceState other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ (int)Gamepad;
        public static bool operator ==(InputDeviceState a, InputDeviceState b) => a.Equals(b);
        public static bool operator !=(InputDeviceState a, InputDeviceState b) => !a.Equals(b);
        public override string ToString() => Kind == InputDeviceKind.Gamepad ? $"{Kind} ({Gamepad})" : Kind.ToString();
    }
}
