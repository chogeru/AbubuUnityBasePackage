using System;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Abubu.Inputs
{
    /// <summary>
    /// 現在使われているデバイスの窓口 (ボタンアイコン・操作説明の出し分け用)。
    /// スティックのドリフトや、キーボードとゲームパッドの同時操作でチラつかないよう、
    /// <see cref="InputDeviceSwitchSettings"/> のしきい値・保持時間で切り替えを抑える。
    /// <code>
    /// _devices.CurrentScheme.Subscribe(s => hint.text = s == InputDeviceKind.Gamepad ? "Press A" : "Press Space").AddTo(this);
    /// _devices.DeviceLost.Where(c => c.Kind == InputDeviceKind.Gamepad).Subscribe(_ => ShowReconnectPrompt()).AddTo(this);
    /// var kind = GameInput.CurrentDevice;          // static でも取れる
    /// var pad = GameInput.CurrentGamepadKind;      // Xbox / PlayStation / Switch / Generic
    /// </code>
    /// </summary>
    /// <remarks>
    /// <see cref="CurrentDevice"/> は最後に操作されたデバイスそのもの (Touch / XR / Joystick もある)。
    /// 割り当て・ボタンアイコンを選ぶときは、キーボード&amp;マウスかゲームパッドのどちらかだけを返す <see cref="CurrentScheme"/> を使う
    /// (タッチやペンを触っても、キー割り当て画面の表示が「未割り当て」にならない)。
    /// </remarks>
    public interface IInputDeviceService
    {
        /// <summary>現在のデバイスの状態 (種類 + 最後に使われたゲームパッドの種類)</summary>
        ReadOnlyReactiveProperty<InputDeviceState> Current { get; }

        /// <summary>最後に操作されたデバイスの種類 (<see cref="InputDeviceKind.Other"/> には切り替わらない)</summary>
        ReadOnlyReactiveProperty<InputDeviceKind> CurrentDevice { get; }

        /// <summary>
        /// 最後に使われた操作の系統。<see cref="InputDeviceKind.KeyboardMouse"/> か <see cref="InputDeviceKind.Gamepad"/> のどちらかだけ。
        /// タッチ・XR・ジョイスティックなどを触っても直前の値のまま。割り当て・ボタンアイコンの選択に使う
        /// </summary>
        ReadOnlyReactiveProperty<InputDeviceKind> CurrentScheme { get; }

        /// <summary>最後に使われたゲームパッドの種類 (未使用なら <see cref="GamepadKind.Generic"/>)</summary>
        ReadOnlyReactiveProperty<GamepadKind> CurrentGamepadKind { get; }

        /// <summary>最後に使われたゲームパッド (未使用・切断済みなら null)。振動の出力先に使う</summary>
        Gamepad CurrentGamepad { get; }

        /// <summary>
        /// デバイスが切断された。使用中のゲームパッドが切断され、他にゲームパッドが無ければ
        /// <see cref="CurrentDevice"/> はキーボード&amp;マウスに戻る (この通知の前に切り替わる)
        /// </summary>
        Observable<InputDeviceConnection> DeviceLost { get; }

        /// <summary>デバイスが接続された (再接続・新しいデバイスの接続のどちらも)</summary>
        Observable<InputDeviceConnection> DeviceRegained { get; }
    }

    /// <summary>デバイスの接続・切断 1 回分</summary>
    public readonly struct InputDeviceConnection
    {
        /// <summary>接続・切断されたデバイス (テストなどでは null)</summary>
        public readonly InputDevice Device;
        /// <summary>デバイスの種類</summary>
        public readonly InputDeviceKind Kind;
        /// <summary>接続されたなら true、切断されたなら false</summary>
        public readonly bool Connected;
        /// <summary>
        /// 切断時: そのデバイスが使用中だったか (最後に使われたゲームパッドなど)。
        /// 接続時は常に false。<see cref="IInputDeviceService"/> が設定する
        /// </summary>
        public readonly bool WasInUse;

        public InputDeviceConnection(InputDevice device, InputDeviceKind kind, bool connected, bool wasInUse = false)
        {
            Device = device;
            Kind = kind;
            Connected = connected;
            WasInUse = wasInUse;
        }

        /// <summary>WasInUse だけを変えたコピー</summary>
        public InputDeviceConnection WithInUse(bool wasInUse) => new(Device, Kind, Connected, wasInUse);
    }

    /// <summary>
    /// デバイスの接続・切断を <see cref="IInputDeviceService"/> に知らせる元 (任意)。
    /// 既定は InputSystem.onDeviceChange を見る <see cref="InputSystemDeviceConnectionSource"/>。テストでは差し替えられる。
    /// <code>
    /// Container.Bind&lt;IInputDeviceConnectionSource&gt;().To&lt;MyConnectionSource&gt;().AsSingle();
    /// </code>
    /// </summary>
    public interface IInputDeviceConnectionSource
    {
        /// <summary>デバイスが接続・切断された</summary>
        Observable<InputDeviceConnection> Changed { get; }

        /// <summary>今つながっているゲームパッドの数</summary>
        int ConnectedGamepadCount { get; }
    }

    /// <summary>
    /// デバイスが操作されたことを <see cref="IInputDeviceService"/> に知らせる元。
    /// 既定は Input System のイベントを見る <see cref="InputSystemActivitySource"/>。
    /// テストや独自の入力 (ネットワーク越しの入力など) では差し替えられる。
    /// <code>
    /// Container.Bind&lt;IInputActivitySource&gt;().To&lt;MyActivitySource&gt;().AsSingle();
    /// </code>
    /// </summary>
    public interface IInputActivitySource
    {
        /// <summary>デバイスが操作された</summary>
        Observable<InputActivity> Activity { get; }

        /// <summary>起動直後に使っていると見なすデバイス (まだ何も操作されていないとき)</summary>
        InputDeviceState InitialState { get; }
    }

    /// <summary>デバイスの操作 1 回分</summary>
    public readonly struct InputActivity
    {
        /// <summary>操作されたデバイスの種類</summary>
        public readonly InputDeviceKind Kind;
        /// <summary>ゲームパッドの種類 (<see cref="Kind"/> が Gamepad のときだけ意味がある)</summary>
        public readonly GamepadKind Gamepad;
        /// <summary>
        /// 操作量。ボタン・スティックは 0〜1、マウス移動 (<see cref="IsPointerMotion"/>) は移動量 (ピクセル)
        /// </summary>
        public readonly float Magnitude;
        /// <summary>マウスカーソルの移動か (ボタンではなく)</summary>
        public readonly bool IsPointerMotion;
        /// <summary>操作された時刻 (秒。単調増加していればよい)</summary>
        public readonly double Time;
        /// <summary>操作されたデバイス (テストなどでは null)</summary>
        public readonly InputDevice Device;

        public InputActivity(InputDeviceKind kind, GamepadKind gamepad, float magnitude, double time,
            bool isPointerMotion = false, InputDevice device = null)
        {
            Kind = kind;
            Gamepad = gamepad;
            Magnitude = magnitude;
            Time = time;
            IsPointerMotion = isPointerMotion;
            Device = device;
        }
    }

    /// <summary>使用中デバイスの切り替え条件 (<see cref="AbubuInputSettings.DeviceSwitch"/>)</summary>
    [Serializable]
    public sealed class InputDeviceSwitchSettings
    {
        [Tooltip("この操作量 (0〜1) 未満のスティック・トリガーの動きでは切り替えない (ドリフト・ノイズ対策)")]
        [Range(0f, 1f)]
        public float ActuationThreshold = 0.5f;

        [Tooltip("Pointer Move Window 秒以内のマウス移動の合計がこの量 (ピクセル) 未満なら、キーボード&マウスに切り替えない (机の振動などで切り替わらないように)")]
        [Min(0f)]
        public float PointerMoveThreshold = 4f;

        [Tooltip("マウス移動量を合計する時間 (秒)。ゆっくり動かしても (1 イベントの移動量が小さくても) 切り替わるようにする")]
        [Min(0f)]
        public float PointerMoveWindow = 0.15f;

        [Tooltip("今のデバイスが最後に操作されてからこの秒数が経つまでは、別のデバイスに切り替えない。" +
                 "キーボードとゲームパッドを同時に触ったときに表示がチラつかないようにする。0 なら即座に切り替える")]
        [Min(0f)]
        public float HoldTime = 0.25f;
    }
}
