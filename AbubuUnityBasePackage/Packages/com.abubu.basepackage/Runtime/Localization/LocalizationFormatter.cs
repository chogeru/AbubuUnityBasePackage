using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Abubu.Localization
{
    /// <summary>
    /// 翻訳文字列の書式化。string.Format と同じ "{0}" "{0:N0}" "{0,5}" に加え、名前付きの "{score}" "{score:N0}" が使える。
    /// "{{" "}}" は波かっこそのもの。値の無いプレースホルダーは例外にせず、そのまま残す (翻訳者のミスで画面が止まらないように)。
    /// </summary>
    public static class LocalizationFormatter
    {
        public static string Format(string template, CultureInfo culture, object[] args = null,
            IReadOnlyDictionary<string, object> named = null)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0 && template.IndexOf('}') < 0) return template ?? string.Empty;
            culture ??= CultureInfo.InvariantCulture;

            var sb = new StringBuilder(template.Length + 16);
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if (c == '}')
                {
                    // "}}" は "}" 1 文字
                    if (i + 1 < template.Length && template[i + 1] == '}') i++;
                    sb.Append('}');
                    continue;
                }
                if (c != '{')
                {
                    sb.Append(c);
                    continue;
                }
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    sb.Append('{');
                    i++;
                    continue;
                }

                var end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    // 閉じていない "{" は文字として扱う
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                var token = template.Substring(i + 1, end - i - 1);
                if (TryFormatToken(token, culture, args, named, out var formatted)) sb.Append(formatted);
                else sb.Append('{').Append(token).Append('}');
                i = end;
            }
            return sb.ToString();
        }

        /// <summary>"name[,alignment][:format]" を値に置き換える。値が無ければ false</summary>
        private static bool TryFormatToken(string token, CultureInfo culture, object[] args, IReadOnlyDictionary<string, object> named,
            out string result)
        {
            result = null;
            var formatIndex = token.IndexOf(':');
            var format = formatIndex >= 0 ? token.Substring(formatIndex + 1) : null;
            var head = formatIndex >= 0 ? token.Substring(0, formatIndex) : token;
            var alignIndex = head.IndexOf(',');
            var name = (alignIndex >= 0 ? head.Substring(0, alignIndex) : head).Trim();
            var alignment = 0;
            if (alignIndex >= 0 && !int.TryParse(head.Substring(alignIndex + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out alignment))
            {
                return false;
            }
            if (name.Length == 0) return false;

            object value;
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                if (args == null || index >= args.Length) return false;
                value = args[index];
            }
            else if (named == null || !named.TryGetValue(name, out value))
            {
                return false;
            }

            var text = value is IFormattable formattable ? formattable.ToString(format, culture) : Convert.ToString(value, culture) ?? string.Empty;
            result = alignment > 0 ? text.PadLeft(alignment) : alignment < 0 ? text.PadRight(-alignment) : text;
            return true;
        }

        /// <summary>
        /// 数に応じた複数形の区分 ("zero" は呼び出し側で扱う)。CLDR の簡略版:
        /// 日中韓など → "other"、フランス語・ポルトガル語 → 0 と 1 が "one"、ロシア語など → "one" / "few" / "many"、それ以外 → 1 が "one"
        /// </summary>
        public static string PluralCategory(string language, long count)
        {
            var n = Math.Abs(count);
            switch (LanguageCode.BaseOf(language))
            {
                case "ja":
                case "zh":
                case "ko":
                case "th":
                case "vi":
                case "id":
                case "ms":
                    return "other";
                case "fr":
                case "pt":
                    return n <= 1 ? "one" : "other";
                case "ru":
                case "uk":
                case "be":
                    if (n % 10 == 1 && n % 100 != 11) return "one";
                    if (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 12 || n % 100 > 14)) return "few";
                    return "many";
                case "pl":
                    if (n == 1) return "one";
                    if (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 12 || n % 100 > 14)) return "few";
                    return "many";
                case "cs":
                case "sk":
                    if (n == 1) return "one";
                    return n >= 2 && n <= 4 ? "few" : "other";
                default:
                    return n == 1 ? "one" : "other";
            }
        }

        /// <summary>言語コードに対応する CultureInfo。対応していない環境 (IL2CPP の Invariant Globalization など) では InvariantCulture</summary>
        public static CultureInfo GetCulture(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return CultureInfo.InvariantCulture;
            foreach (var candidate in LanguageCode.Chain(language))
            {
                try
                {
                    return CultureInfo.GetCultureInfo(candidate);
                }
                catch (CultureNotFoundException)
                {
                }
                catch (ArgumentException)
                {
                }
            }
            return CultureInfo.InvariantCulture;
        }
    }
}
