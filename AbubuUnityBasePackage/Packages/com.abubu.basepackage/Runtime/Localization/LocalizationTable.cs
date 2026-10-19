using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Abubu.Localization
{
    /// <summary>キー 1 つ分の翻訳。Values は <see cref="LocalizationTable.Languages"/> と同じ順に並べる</summary>
    [Serializable]
    public sealed class LocalizationEntry
    {
        public string Key;
        [TextArea(1, 4)] public List<string> Values = new();
    }

    /// <summary>キー 1 つ分の言語ごとのアセット (画像・音声など)。Values は <see cref="LocalizationTable.Languages"/> と同じ順に並べる</summary>
    [Serializable]
    public sealed class LocalizedAssetEntry
    {
        public string Key;
        public List<UnityEngine.Object> Values = new();
    }

    /// <summary>
    /// キー → 各言語の文字列 の表。Create > Abubu > Localization Table で作成し、AbubuSettings に登録して使う。
    /// 翻訳は Entries に直接書くか、CSV (Google スプレッドシートの書き出しなど) を Csv Sources に入れる。
    /// </summary>
    /// <remarks>
    /// 同じキーが複数あるときの優先順は Entries → Csv Sources (後ろのものほど優先) → Includes。
    /// </remarks>
    [CreateAssetMenu(menuName = "Abubu/Localization Table", fileName = "LocalizationTable")]
    public sealed class LocalizationTable : ScriptableObject
    {
        [Tooltip("言語コード (ja, en, zh-Hans など)。Entries の Values はこの順に並べる")]
        public List<string> Languages = new() { "ja", "en" };

        public List<LocalizationEntry> Entries = new();

        [Tooltip("CSV / TSV (UTF-8)。1 行目は key,ja,en のような見出し。キーが # で始まる行はコメント。タブ区切りなら TSV として読む")]
        public List<TextAsset> CsvSources = new();

        [Tooltip("言語ごとのアセット (画像・音声など)。LocalizedImage / LocalizedAudio や ILocalizationService.GetAsset で使う")]
        public List<LocalizedAssetEntry> Assets = new();

        [Tooltip("他のテーブルを取り込む (共通 UI 文言 + ステージ別テキスト など分割管理用)")]
        public List<LocalizationTable> Includes = new();

        /// <summary>言語コード → (キー → 文字列) を組み立てる。空文字の翻訳は「未翻訳」として含めない</summary>
        public Dictionary<string, Dictionary<string, string>> Build()
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            Collect(this, result, new HashSet<LocalizationTable>());
            return result;
        }

        /// <summary>言語コード → (キー → アセット) を組み立てる。未設定 (null) のものは含めない</summary>
        public Dictionary<string, Dictionary<string, UnityEngine.Object>> BuildAssets()
        {
            var result = new Dictionary<string, Dictionary<string, UnityEngine.Object>>();
            CollectAssets(this, result, new HashSet<LocalizationTable>());
            return result;
        }

        private static void CollectAssets(LocalizationTable table, Dictionary<string, Dictionary<string, UnityEngine.Object>> output,
            HashSet<LocalizationTable> visited)
        {
            if (table == null || !visited.Add(table)) return;
            for (var i = table.Includes.Count - 1; i >= 0; i--) CollectAssets(table.Includes[i], output, visited);

            foreach (var entry in table.Assets)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key)) continue;
                var key = entry.Key.Trim();
                for (var i = 0; i < table.Languages.Count && i < entry.Values.Count; i++)
                {
                    var language = table.Languages[i]?.Trim();
                    if (string.IsNullOrEmpty(language) || entry.Values[i] == null) continue;
                    if (!output.TryGetValue(language, out var values)) output[language] = values = new Dictionary<string, UnityEngine.Object>();
                    values[key] = entry.Values[i];
                }
            }
        }

        /// <summary>このテーブル (Includes 先・CSV の見出しを含む) で使われている言語コード。Languages に書いた順が先</summary>
        public IReadOnlyList<string> GetAllLanguages()
        {
            var languages = new List<string>();
            CollectLanguages(this, languages, new HashSet<LocalizationTable>());
            return languages;
        }

        /// <summary>登録されているキー (Includes 先・CSV を含む)。キー定数の生成や診断に使う</summary>
        public IEnumerable<string> GetAllKeys() => Build().Values.SelectMany(t => t.Keys).Distinct();

        private static void Collect(LocalizationTable table, Dictionary<string, Dictionary<string, string>> output, HashSet<LocalizationTable> visited)
        {
            if (table == null || !visited.Add(table)) return;

            // 優先度の低いものから書き込み、後から書いたもので上書きする
            for (var i = table.Includes.Count - 1; i >= 0; i--) Collect(table.Includes[i], output, visited);

            foreach (var csv in table.CsvSources)
            {
                if (csv != null) LocalizationCsv.ReadInto(csv.text, output);
            }

            foreach (var entry in table.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key)) continue;
                var key = entry.Key.Trim();
                for (var i = 0; i < table.Languages.Count && i < entry.Values.Count; i++)
                {
                    var language = table.Languages[i]?.Trim();
                    if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(entry.Values[i])) continue;
                    if (!output.TryGetValue(language, out var values)) output[language] = values = new Dictionary<string, string>();
                    values[key] = entry.Values[i];
                }
            }
        }

        private static void CollectLanguages(LocalizationTable table, List<string> output, HashSet<LocalizationTable> visited)
        {
            if (table == null || !visited.Add(table)) return;

            foreach (var language in table.Languages) AddLanguage(language);
            foreach (var csv in table.CsvSources)
            {
                if (csv == null) continue;
                foreach (var language in LocalizationCsv.ReadLanguages(csv.text)) AddLanguage(language);
            }
            foreach (var include in table.Includes) CollectLanguages(include, output, visited);

            void AddLanguage(string language)
            {
                if (string.IsNullOrWhiteSpace(language)) return;
                language = language.Trim();
                if (!output.Contains(language)) output.Add(language);
            }
        }
    }
}
