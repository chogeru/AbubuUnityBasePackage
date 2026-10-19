using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Abubu.Inputs
{
    /// <summary>割り当て変更の入力待ち中に押されたボタンの扱い (<see cref="RebindInputGuard.Decide"/> の結果)</summary>
    public enum RebindPressAction
    {
        /// <summary>何もしない (割り当て変更の処理・UI にそのまま渡す)</summary>
        PassThrough,
        /// <summary>入力待ちをキャンセルする (イベントは UI・ゲームに届けない)</summary>
        Cancel,
        /// <summary>受け付けないデバイスのボタン。イベントを捨てて UI・ゲームに届けない (B で画面が閉じる・Start でポーズする のを防ぐ)</summary>
        Suppress,
        /// <summary>マウスのボタン。UI のクリックを妨げないよう、離して 1 フレーム後に (キャンセルされていなければ) 割り当てる</summary>
        PointerCandidate,
    }

    /// <summary>
    /// 割り当て変更 (<see cref="IInputService.RebindAsync"/>) の入力待ち中に押されたボタンをどう扱うかを決める。
    /// RebindingOperation の外で、全デバイスのボタンを見て判定する (InputService が InputSystem.onEvent から呼ぶ)。
    /// <code>
    /// var guard = new RebindInputGuard(new[] { "&lt;Keyboard&gt;/escape", "&lt;Gamepad&gt;/start" }, InputDeviceKind.KeyboardMouse, allowMouseButtons: true);
    /// guard.Decide(InputDeviceKind.Gamepad, isPointerDevice: false, isMouseButton: false, "&lt;Gamepad&gt;/start"); // Cancel
    /// guard.Decide(InputDeviceKind.Gamepad, false, false, "&lt;Gamepad&gt;/buttonEast");                          // Suppress
    /// </code>
    /// </summary>
    /// <remarks>
    /// 判定の順番:
    /// <list type="number">
    /// <item>キャンセル操作 (どのデバイスでも) → Cancel</item>
    /// <item>マウス・ペン・タッチ: UI (キャンセルボタンなど) を押せるよう、捨てない。マウスのボタンで割り当て可能なら PointerCandidate</item>
    /// <item>受け付けるデバイスが決まっていて (<c>requiredKind</c>)、それ以外のデバイスのボタン → Suppress</item>
    /// <item>それ以外 → PassThrough (RebindingOperation が候補として扱い、イベントも捨てる)</item>
    /// </list>
    /// </remarks>
    public sealed class RebindInputGuard
    {
        private readonly IReadOnlyList<string> _cancelPaths;
        private readonly IReadOnlyList<string> _excludedPaths;
        private readonly InputDeviceKind? _requiredKind;
        private readonly bool _allowMouseButtons;

        /// <param name="cancelPaths">キャンセル操作 (Input Control Path)</param>
        /// <param name="requiredKind">受け付けるデバイスの種類 (null なら制限しない)</param>
        /// <param name="allowMouseButtons">マウスのボタンを割り当てられるか</param>
        /// <param name="excludedPaths">割り当てない操作 (Input Control Path。任意)</param>
        public RebindInputGuard(IReadOnlyList<string> cancelPaths, InputDeviceKind? requiredKind, bool allowMouseButtons,
            IReadOnlyList<string> excludedPaths = null)
        {
            _cancelPaths = cancelPaths ?? System.Array.Empty<string>();
            _excludedPaths = excludedPaths ?? System.Array.Empty<string>();
            _requiredKind = requiredKind;
            _allowMouseButtons = allowMouseButtons;
        }

        /// <summary>
        /// 押されたボタンの扱いを決める
        /// </summary>
        /// <param name="deviceKind">押されたデバイスの種類</param>
        /// <param name="isPointerDevice">マウス・ペン・タッチなどのポインターか</param>
        /// <param name="isMouseButton">マウスのボタン (左・右・中・進む・戻る) か</param>
        /// <param name="controlPath">"&lt;Layout&gt;/control" 形式の操作 (control が null のときの照合に使う)</param>
        /// <param name="control">押されたコントロール (あればワイルドカードなどを含むパスとも照合できる)</param>
        public RebindPressAction Decide(InputDeviceKind deviceKind, bool isPointerDevice, bool isMouseButton, string controlPath,
            InputControl control = null)
        {
            if (Matches(_cancelPaths, controlPath, control)) return RebindPressAction.Cancel;

            if (isPointerDevice)
            {
                if (!isMouseButton || !_allowMouseButtons || Matches(_excludedPaths, controlPath, control)) return RebindPressAction.PassThrough;
                return !_requiredKind.HasValue || _requiredKind.Value == InputDeviceKind.KeyboardMouse
                    ? RebindPressAction.PointerCandidate
                    : RebindPressAction.PassThrough;
            }

            if (_requiredKind.HasValue && deviceKind != _requiredKind.Value) return RebindPressAction.Suppress;
            return RebindPressAction.PassThrough;
        }

        /// <summary>キャンセル操作か</summary>
        public bool IsCancel(string controlPath, InputControl control = null) => Matches(_cancelPaths, controlPath, control);

        private static bool Matches(IReadOnlyList<string> paths, string controlPath, InputControl control)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (string.IsNullOrEmpty(path)) continue;
                if (control != null)
                {
                    if (InputControlPath.Matches(path, control)) return true;
                }
                else if (InputBindingUtility.IsSameControl(path, controlPath))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
