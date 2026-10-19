using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Abubu.Localization
{
    /// <summary>
    /// 言語コード (BCP 47 風: "zh-Hant-TW" など) の正規化と照合。
    /// <code>
    /// LanguageCode.Normalize("zh_hant_tw");   // "zh-Hant-TW"
    /// LanguageCode.Chain("zh-Hant-TW");       // "zh-Hant-TW", "zh-Hant", "zh"
    /// LanguageCode.Resolve("pt-BR", new[] { "ja", "pt" }); // "pt"
    /// </code>
    /// </summary>
    public static class LanguageCode
    {
        /// <summary>
        /// 区切りを "-" にそろえ、言語は小文字・文字体系 (4 文字) は先頭だけ大文字・地域 (2 文字 / 3 桁) は大文字にする
        /// </summary>
        public static string Normalize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;

            var parts = code.Trim().Replace('_', '-').Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i > 0) sb.Append('-');
                if (i == 0) sb.Append(part.ToLowerInvariant());
                else if (part.Length == 4) sb.Append(char.ToUpperInvariant(part[0])).Append(part.Substring(1).ToLowerInvariant());
                else if (part.Length == 2 || (part.Length == 3 && char.IsDigit(part[0]))) sb.Append(part.ToUpperInvariant());
                else sb.Append(part.ToLowerInvariant());
            }
            return sb.ToString();
        }

        /// <summary>詳しいものから順に、末尾の区切りを 1 つずつ外した候補 ("zh-Hant-TW" → "zh-Hant" → "zh")</summary>
        public static IEnumerable<string> Chain(string code)
        {
            var current = Normalize(code);
            while (current.Length > 0)
            {
                yield return current;
                var index = current.LastIndexOf('-');
                if (index < 0) yield break;
                current = current.Substring(0, index);
            }
        }

        /// <summary>言語の部分 ("zh-Hant-TW" → "zh")</summary>
        public static string BaseOf(string code)
        {
            var normalized = Normalize(code);
            var index = normalized.IndexOf('-');
            return index < 0 ? normalized : normalized.Substring(0, index);
        }

        /// <summary>
        /// 選べる言語の中から、要求された言語に最も近いものを返す (表記ゆれ・大文字小文字は無視)。
        /// 詳しい順 (<see cref="Chain"/>) に完全一致を探し、無ければ同じ言語の別の地域 ("pt" に対する "pt-BR") を探す。見つからなければ null
        /// </summary>
        public static string Resolve(string requested, IReadOnlyList<string> available)
        {
            if (string.IsNullOrWhiteSpace(requested) || available == null) return null;

            foreach (var candidate in Chain(requested))
            {
                var exact = FindExact(candidate, available);
                if (exact != null) return exact;
            }

            var baseLanguage = BaseOf(requested);
            foreach (var language in available)
            {
                if (!string.IsNullOrWhiteSpace(language) && BaseOf(language) == baseLanguage) return language;
            }
            return null;
        }

        /// <summary>表記ゆれを無視して完全一致するものを返す。無ければ null</summary>
        public static string FindExact(string code, IReadOnlyList<string> available)
        {
            var normalized = Normalize(code);
            foreach (var language in available)
            {
                if (!string.IsNullOrWhiteSpace(language) && Normalize(language) == normalized) return language;
            }
            return null;
        }

        /// <summary>端末の言語を言語コードに変換する。対応表に無ければ null</summary>
        public static string FromSystemLanguage(SystemLanguage language) => language switch
        {
            SystemLanguage.Japanese => "ja",
            SystemLanguage.English => "en",
            SystemLanguage.Chinese => "zh-Hans",
            SystemLanguage.ChineseSimplified => "zh-Hans",
            SystemLanguage.ChineseTraditional => "zh-Hant",
            SystemLanguage.Korean => "ko",
            SystemLanguage.French => "fr",
            SystemLanguage.German => "de",
            SystemLanguage.Spanish => "es",
            SystemLanguage.Italian => "it",
            SystemLanguage.Portuguese => "pt",
            SystemLanguage.Russian => "ru",
            SystemLanguage.Ukrainian => "uk",
            SystemLanguage.Polish => "pl",
            SystemLanguage.Dutch => "nl",
            SystemLanguage.Swedish => "sv",
            SystemLanguage.Norwegian => "no",
            SystemLanguage.Danish => "da",
            SystemLanguage.Finnish => "fi",
            SystemLanguage.Turkish => "tr",
            SystemLanguage.Thai => "th",
            SystemLanguage.Vietnamese => "vi",
            SystemLanguage.Indonesian => "id",
            SystemLanguage.Arabic => "ar",
            SystemLanguage.Hebrew => "he",
            SystemLanguage.Czech => "cs",
            SystemLanguage.Hungarian => "hu",
            SystemLanguage.Greek => "el",
            SystemLanguage.Romanian => "ro",
            _ => null,
        };
    }
}
