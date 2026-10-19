using Abubu.Inputs;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Abubu
{
    /// <summary>
    /// 入力の static ショートカット。入力モジュールが無効・未セットアップのときは何もしない (既定値を返す)。
    /// <code>
    /// GameInput.Performed("Player/Jump").Subscribe(_ => ...);
    /// var steer = GameInput.Value&lt;float&gt;("Vehicle/Steer");
    /// var move = GameInput.Move;             // AbubuInputSettings.Move のアクションの現在値
    /// if (GameInput.CurrentDevice == InputDeviceKind.Gamepad) ...       // 使用中のデバイス
    /// var pad = GameInput.CurrentGamepadKind;                            // Xbox / PlayStation / Switch / Generic
    /// GameInput.Rumble(HapticPreset.Light);                              // ゲームパッドの振動
    /// var glyph = GameInput.GetGlyph("Player/Jump");                     // ボタンアイコン + 表示文字列
    /// </code>
    /// </summary>
    public static class GameInput
    {
        public static IInputService Service => AbubuServices.TryResolve<IInputService>();

        /// <summary>"Map/Action" 形式でアクションを取得する。見つからなければ null</summary>
        public static InputAction GetAction(string actionPath) => Service?.GetAction(actionPath);
        /// <summary>"Map/Action" 形式で任意のアクションの押下を購読する</summary>
        public static Observable<Unit> OnPerformed(string actionPath) => Service?.OnPerformed(actionPath) ?? Observable.Empty<Unit>();
        /// <summary>"Map/Action" 形式で任意のアクションの performed を購読する (CallbackContext はコールバック中だけ有効)</summary>
        public static Observable<InputAction.CallbackContext> Performed(string actionPath) =>
            Service?.Performed(actionPath) ?? Observable.Empty<InputAction.CallbackContext>();
        /// <summary>"Map/Action" 形式で指定したアクションの現在値</summary>
        public static T Value<T>(string actionPath) where T : struct
        {
            var service = Service;
            return service != null ? service.ReadValue<T>(actionPath).CurrentValue : default;
        }
        /// <summary>"Map/Action" 形式で指定したボタンが押されているか</summary>
        public static bool IsPressed(string actionPath) => Service?.IsPressed(actionPath).CurrentValue ?? false;
        /// <summary>使用中デバイスの判定 (未セットアップなら null)</summary>
        public static IInputDeviceService Devices => AbubuServices.TryResolve<IInputDeviceService>();
        /// <summary>振動 (未セットアップなら null)</summary>
        public static IHapticsService Haptics => AbubuServices.TryResolve<IHapticsService>();
        /// <summary>ボタンアイコン (未セットアップなら null)</summary>
        public static IInputGlyphService Glyphs => AbubuServices.TryResolve<IInputGlyphService>();

        /// <summary>最後に操作されたデバイスの種類</summary>
        public static InputDeviceKind CurrentDevice => Service?.CurrentDevice.CurrentValue ?? InputDeviceKind.KeyboardMouse;
        /// <summary>最後に使われたゲームパッドの種類 (未使用なら Generic)</summary>
        public static GamepadKind CurrentGamepadKind => Devices?.CurrentGamepadKind.CurrentValue ?? GamepadKind.Generic;
        /// <summary>最後に使われた操作の系統 (KeyboardMouse か Gamepad)。タッチ・ペンなどでは変わらない</summary>
        public static InputDeviceKind CurrentScheme => Service?.CurrentScheme.CurrentValue ?? InputDeviceKind.KeyboardMouse;
        /// <summary>ゲームパッドを使用中か</summary>
        public static bool IsUsingGamepad => CurrentDevice == InputDeviceKind.Gamepad;
        /// <summary>キー割り当て変更の入力待ち中か (この間は UI の「戻る」などを止める)</summary>
        public static bool IsRebinding => Service?.IsRebinding.CurrentValue ?? false;
        /// <summary>振動に対応したゲームパッドを使っているか (オプション画面で振動の設定を出すか決める)</summary>
        public static bool SupportsRumble => Haptics?.SupportsRumble ?? false;

        /// <summary>プリセットで振動させる (ゲームパッドが無い・振動オフ・ポーズ中は何もしない)</summary>
        public static void Rumble(HapticPreset preset) => Haptics?.Play(preset);
        /// <summary>モーターの強さ (0〜1) と秒数を指定して振動させる</summary>
        public static void Rumble(float lowFrequency, float highFrequency, float duration) => Haptics?.Play(lowFrequency, highFrequency, duration);
        /// <summary>HapticsSettings.Presets の名前で振動させる。見つからなければ false</summary>
        public static bool Rumble(string presetName) => Haptics?.Play(presetName) ?? false;
        /// <summary>振動を止める</summary>
        public static void StopRumble() => Haptics?.Stop();

        /// <summary>アクションの、現在のデバイスでのアイコン・表示文字列</summary>
        public static InputGlyph GetGlyph(string actionPath, int bindingIndex = -1) =>
            Glyphs?.GetGlyph(actionPath, bindingIndex) ?? new InputGlyph(null, string.Empty, string.Empty, -1);
        /// <summary>アクションの割り当ての表示用文字列 (bindingIndex が負なら現在のデバイス向けの割り当て)</summary>
        public static string GetBindingDisplayString(string actionPath, int bindingIndex = -1) => GetGlyph(actionPath, bindingIndex).DisplayString;
        /// <summary>ActionMap を有効/無効にする (Dispose 時に元へ戻る)</summary>
        public static void SetMapEnabled(string mapName, bool enabled) => Service?.SetMapEnabled(mapName, enabled);
        /// <summary>AbubuInputSettings.GameplayMap の ActionMap を有効/無効にする (会話中・カットシーン中など)</summary>
        public static void SetGameplayEnabled(bool enabled) => Service?.SetGameplayEnabled(enabled);

        // --- AbubuInputSettings のパスで決まる便利メンバー ---

        public static Vector2 Move => Service?.Move.CurrentValue ?? Vector2.zero;
        public static Vector2 Look => Service?.Look.CurrentValue ?? Vector2.zero;
        public static Observable<Unit> Jump => Service?.Jump ?? Observable.Empty<Unit>();
        public static Observable<Unit> Attack => Service?.Attack ?? Observable.Empty<Unit>();
        public static Observable<Unit> Interact => Service?.Interact ?? Observable.Empty<Unit>();
        public static Observable<Unit> Submit => Service?.Submit ?? Observable.Empty<Unit>();
        public static Observable<Unit> Cancel => Service?.Cancel ?? Observable.Empty<Unit>();
    }
}
