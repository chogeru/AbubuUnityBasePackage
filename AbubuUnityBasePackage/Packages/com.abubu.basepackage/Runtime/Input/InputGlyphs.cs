using System;
using System.Collections.Generic;
using UnityEngine;

namespace Abubu.Inputs
{
    /// <summary>
    /// 操作 (Control Path) → ボタンアイコン (Sprite) の対応表。デバイスの種類・ゲームパッドの種類ごとにセットを持てる。
    /// 中身は空で、プロジェクトで用意したアイコンを登録して使う。
    /// <code>
    /// // Create > Abubu > Input Glyphs で作り、AbubuInputSettings.Glyphs に設定すると IInputGlyphService / InputGlyphImage が使う
    /// var sprite = glyphs.GetSprite("&lt;Gamepad&gt;/buttonSouth", InputDeviceKind.Gamepad, GamepadKind.PlayStation); // × のアイコン
    /// var glyph = GameInput.GetGlyph("Player/Jump");   // 現在のデバイス・割り当てに合わせたアイコン (+ 表示文字列)
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Control Path は "&lt;Gamepad&gt;/buttonSouth" のようにデバイスのレイアウト付きで書く。
    /// 派生レイアウトにも一致する ("&lt;Gamepad&gt;/buttonSouth" は "&lt;XInputController&gt;/buttonSouth" の割り当てにも使われる)。
    /// </para>
    /// <para>
    /// 探す順番: (1) デバイスの種類とゲームパッドの種類が一致するセット → (2) ゲームパッドなら Generic のセット →
    /// (3) デバイスの種類を問わず Control Path が一致するもの。
    /// 例えば Generic のセットに共通のアイコンを入れ、PlayStation / Switch のセットには違うものだけを入れればよい。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Abubu/Input Glyphs", fileName = "InputGlyphs")]
    public sealed class InputGlyphs : ScriptableObject
    {
        /// <summary>操作 1 つ分のアイコン</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Control Path (例: <Gamepad>/buttonSouth, <Keyboard>/space, <Mouse>/leftButton)")]
            public string ControlPath;
            public Sprite Sprite;

            public Entry() { }

            public Entry(string controlPath, Sprite sprite)
            {
                ControlPath = controlPath;
                Sprite = sprite;
            }
        }

        /// <summary>デバイス (+ ゲームパッドの種類) ごとのアイコンのセット</summary>
        [Serializable]
        public sealed class GlyphSet
        {
            [Tooltip("Inspector での見出し用 (任意)")]
            public string Name;
            public InputDeviceKind Device = InputDeviceKind.Gamepad;
            [Tooltip("Device が Gamepad のときだけ使う。Generic は他の種類のセットに無いときの共通アイコン")]
            public GamepadKind Gamepad = GamepadKind.Generic;
            public List<Entry> Entries = new();
        }

        [SerializeField] private List<GlyphSet> sets = new();

        /// <summary>セットの一覧 (実行中に追加・変更してよい)</summary>
        public List<GlyphSet> Sets => sets;

        /// <summary>アイコンを探す。見つからなければ null</summary>
        /// <param name="controlPath">割り当ての Control Path (effectivePath)</param>
        /// <param name="device">使用中のデバイスの種類</param>
        /// <param name="gamepad">ゲームパッドの種類 (device が Gamepad のときだけ使う)</param>
        public Sprite GetSprite(string controlPath, InputDeviceKind device, GamepadKind gamepad = GamepadKind.Generic) =>
            TryGetSprite(controlPath, device, gamepad, out var sprite) ? sprite : null;

        /// <summary>アイコンを探す</summary>
        public bool TryGetSprite(string controlPath, InputDeviceKind device, GamepadKind gamepad, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(controlPath) || sets == null) return false;
            if (device != InputDeviceKind.Gamepad) gamepad = GamepadKind.Generic;

            // (1) デバイスの種類・ゲームパッドの種類が一致するセット
            if (Find(controlPath, s => s.Device == device && (device != InputDeviceKind.Gamepad || s.Gamepad == gamepad), out sprite)) return true;
            // (2) ゲームパッドなら Generic のセット
            if (device == InputDeviceKind.Gamepad && gamepad != GamepadKind.Generic &&
                Find(controlPath, s => s.Device == InputDeviceKind.Gamepad && s.Gamepad == GamepadKind.Generic, out sprite)) return true;
            // (3) 種類を問わず一致するもの (キーボード使用中にゲームパッド専用アクションを表示する場合など)。
            //     ゲームパッドのセットは、指定された種類 → Generic を優先する
            return Find(controlPath, s => s.Device != InputDeviceKind.Gamepad || s.Gamepad == gamepad, out sprite) ||
                   Find(controlPath, s => s.Device == InputDeviceKind.Gamepad && s.Gamepad == GamepadKind.Generic, out sprite) ||
                   Find(controlPath, _ => true, out sprite);
        }

        private bool Find(string controlPath, Predicate<GlyphSet> match, out Sprite sprite)
        {
            foreach (var set in sets)
            {
                if (set?.Entries == null || !match(set)) continue;
                foreach (var entry in set.Entries)
                {
                    if (entry == null || entry.Sprite == null) continue;
                    if (InputBindingUtility.IsSameControl(entry.ControlPath, controlPath))
                    {
                        sprite = entry.Sprite;
                        return true;
                    }
                }
            }
            sprite = null;
            return false;
        }
    }
}
