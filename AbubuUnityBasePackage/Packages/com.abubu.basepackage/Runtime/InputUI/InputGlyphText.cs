using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// <see cref="InputGlyphText"/> の設定。TextMeshPro のスプライト (&lt;sprite name=...&gt;) でボタンアイコンを出す場合に使う。
    /// <code>
    /// var options = new InputGlyphTextOptions { UseSpriteTags = true, SpriteAsset = "ButtonIcons" };
    /// tmp.text = InputGlyphText.Format("Press {Player/Jump} to jump", options);
    /// </code>
    /// </summary>
    /// <remarks>
    /// スプライト名の決め方 (優先順): <see cref="SpriteNames"/> の対応表 → InputGlyphs のアイコン (Sprite) の名前
    /// (<see cref="UseGlyphSpriteNames"/>。TMP の Sprite Asset を同じスプライトシートから作った場合はそのまま一致する)。
    /// どちらでも決まらなければ表示文字列 ("Space" / "A" など) を出す。
    /// </remarks>
    [Serializable]
    public sealed class InputGlyphTextOptions
    {
        /// <summary>操作 → TMP のスプライト名 の対応 1 つ分</summary>
        [Serializable]
        public sealed class SpriteName
        {
            [Tooltip("Control Path (例: <Gamepad>/buttonSouth)。派生レイアウトにも一致する")]
            public string ControlPath;
            [Tooltip("ゲームパッドの種類で分けたい場合に指定する。Generic ならどの種類にも使う")]
            public GamepadKind Gamepad = GamepadKind.Generic;
            [Tooltip("TMP の Sprite Asset 内のスプライト名")]
            public string Name;

            public SpriteName() { }

            public SpriteName(string controlPath, string name, GamepadKind gamepad = GamepadKind.Generic)
            {
                ControlPath = controlPath;
                Name = name;
                Gamepad = gamepad;
            }
        }

        [Tooltip("オンにすると、スプライト名が決まる操作は TMP の <sprite> タグにする (TextMeshPro 用)。オフなら常に表示文字列")]
        public bool UseSpriteTags;

        [Tooltip("<sprite=\"Asset\" name=...> の Asset 名。空なら TMP の既定の Sprite Asset (<sprite name=...>)")]
        public string SpriteAsset;

        [Tooltip("InputGlyphs のアイコン (Sprite) の名前を、そのまま TMP のスプライト名として使う")]
        public bool UseGlyphSpriteNames = true;

        [Tooltip("操作 → TMP のスプライト名 の対応表 (任意。InputGlyphs のアイコン名より優先)")]
        public List<SpriteName> SpriteNames = new();

        [Tooltip("アクションが無い・割り当てが空のときに出す文字列")]
        public string UnboundText = "-";

        [Tooltip("複合バインディング (WASD など) をスプライトで出すときの、パートの区切り")]
        public string CompositeSeparator = string.Empty;

        /// <summary>対応表からスプライト名を探す (ゲームパッドの種類が一致するもの → Generic の順)。無ければ null</summary>
        public string FindSpriteName(string controlPath, GamepadKind gamepad)
        {
            if (string.IsNullOrEmpty(controlPath) || SpriteNames == null) return null;
            string fallback = null;
            foreach (var entry in SpriteNames)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name) || !InputBindingUtility.IsSameControl(entry.ControlPath, controlPath)) continue;
                if (entry.Gamepad == gamepad) return entry.Name;
                if (entry.Gamepad == GamepadKind.Generic) fallback ??= entry.Name;
            }
            return fallback;
        }
    }

    /// <summary>
    /// "Press {Player/Jump} to jump" のような文字列の {Map/Action} を、今のデバイス・割り当てに合わせた表示に置き換える。
    /// TextMeshPro のスプライトタグにも対応する (<see cref="InputGlyphTextOptions"/>)。
    /// <code>
    /// label.text = InputGlyphText.Format("Press {Player/Jump} to jump");        // "Press Space to jump" / "Press A to jump"
    /// label.text = InputGlyphText.Format("{Player/Move:1} Move");              // ":1" で割り当てのインデックスを指定
    /// tmp.text = InputGlyphText.Format("{UI/Submit} OK", tmpOptions);          // "&lt;sprite name=xbox_a&gt; OK"
    /// // デバイスが変わったら作り直す (InputPromptLabel コンポーネントなら自動)
    /// GameInput.Glyphs.Changed.Subscribe(_ => Refresh()).AddTo(this);
    /// </code>
    /// </summary>
    /// <remarks>
    /// "{{" / "}}" は "{" / "}" として出力する。閉じていない "{" はそのまま出力する。
    /// IInputGlyphService が無い (入力モジュールが無効など) 場合は、{...} を UnboundText に置き換える。
    /// </remarks>
    public static class InputGlyphText
    {
        private const char Quote = '"';

        /// <summary>options を省略したときに使う設定 (プロジェクト全体で TMP のスプライトを使う場合はここを書き換える)</summary>
        public static InputGlyphTextOptions DefaultOptions { get; set; } = new();

        /// <summary>{Map/Action} を置き換える。glyphs が null なら GameInput.Glyphs、ゲームパッドの種類は GameInput.CurrentGamepadKind を使う</summary>
        public static string Format(string template, InputGlyphTextOptions options = null) =>
            Format(template, GameInput.Glyphs, GameInput.CurrentGamepadKind, options);

        /// <summary>{Map/Action} を置き換える</summary>
        /// <param name="template">"Press {Player/Jump} to jump" のような文字列</param>
        /// <param name="glyphs">アイコン・表示文字列の取得元 (null なら UnboundText に置き換える)</param>
        /// <param name="gamepad">スプライト名の対応表で使うゲームパッドの種類</param>
        /// <param name="options">null なら <see cref="DefaultOptions"/></param>
        public static string Format(string template, IInputGlyphService glyphs, GamepadKind gamepad, InputGlyphTextOptions options = null)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            options ??= DefaultOptions ?? new InputGlyphTextOptions();

            var builder = new StringBuilder(template.Length + 16);
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    builder.Append('{');
                    i++;
                    continue;
                }
                if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
                {
                    builder.Append('}');
                    i++;
                    continue;
                }
                if (c != '{')
                {
                    builder.Append(c);
                    continue;
                }

                var end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    builder.Append(template, i, template.Length - i);
                    break;
                }
                AppendToken(builder, template.Substring(i + 1, end - i - 1), glyphs, gamepad, options);
                i = end;
            }
            return builder.ToString();
        }

        /// <summary>1 つのアクションの表示 ({...} の中身 1 つ分)</summary>
        public static string FormatAction(string actionPath, int bindingIndex, IInputGlyphService glyphs, GamepadKind gamepad,
            InputGlyphTextOptions options = null)
        {
            var builder = new StringBuilder();
            AppendAction(builder, actionPath, bindingIndex, glyphs, gamepad, options ?? DefaultOptions ?? new InputGlyphTextOptions());
            return builder.ToString();
        }

        private static void AppendToken(StringBuilder builder, string token, IInputGlyphService glyphs, GamepadKind gamepad,
            InputGlyphTextOptions options)
        {
            var actionPath = token.Trim();
            var bindingIndex = -1;
            var colon = actionPath.LastIndexOf(':');
            if (colon > 0 && int.TryParse(actionPath.Substring(colon + 1), out var index))
            {
                bindingIndex = index;
                actionPath = actionPath.Substring(0, colon);
            }
            AppendAction(builder, actionPath, bindingIndex, glyphs, gamepad, options);
        }

        private static void AppendAction(StringBuilder builder, string actionPath, int bindingIndex, IInputGlyphService glyphs,
            GamepadKind gamepad, InputGlyphTextOptions options)
        {
            if (glyphs == null)
            {
                builder.Append(options.UnboundText);
                return;
            }

            var glyph = glyphs.GetGlyph(actionPath, bindingIndex);
            if (glyph.IsUnbound)
            {
                builder.Append(options.UnboundText);
                return;
            }

            if (options.UseSpriteTags)
            {
                // 複合バインディング (ControlPath が空) はパートごとのスプライトを並べる。1 つでも決まらなければ表示文字列にする
                if (string.IsNullOrEmpty(glyph.ControlPath))
                {
                    var parts = glyphs.GetCompositeGlyphs(actionPath, glyph.BindingIndex);
                    var names = new List<string>(parts.Count);
                    foreach (var part in parts)
                    {
                        var partName = GetSpriteName(part, gamepad, options);
                        if (partName == null) break;
                        names.Add(partName);
                    }
                    if (names.Count > 0 && names.Count == parts.Count)
                    {
                        for (var i = 0; i < names.Count; i++)
                        {
                            if (i > 0) builder.Append(options.CompositeSeparator);
                            AppendSpriteTag(builder, names[i], options);
                        }
                        return;
                    }
                }
                else
                {
                    var name = GetSpriteName(glyph, gamepad, options);
                    if (name != null)
                    {
                        AppendSpriteTag(builder, name, options);
                        return;
                    }
                }
            }
            builder.Append(glyph.DisplayString);
        }

        private static string GetSpriteName(in InputGlyph glyph, GamepadKind gamepad, InputGlyphTextOptions options)
        {
            var name = options.FindSpriteName(glyph.ControlPath, gamepad);
            if (!string.IsNullOrEmpty(name)) return name;
            return options.UseGlyphSpriteNames && glyph.Sprite != null ? glyph.Sprite.name : null;
        }

        private static void AppendSpriteTag(StringBuilder builder, string name, InputGlyphTextOptions options)
        {
            builder.Append("<sprite");
            if (!string.IsNullOrEmpty(options.SpriteAsset)) builder.Append('=').Append(Quote).Append(options.SpriteAsset).Append(Quote);
            builder.Append(" name=").Append(Quote).Append(name).Append(Quote).Append('>');
        }
    }
}
