using System;
using System.Collections.Generic;
using System.Text;

namespace Abubu.Localization
{
    /// <summary>
    /// ローカライズ用 CSV / TSV の読み書き。1 行目が見出し (key,ja,en ...)、2 行目以降が 1 キー 1 行。
    /// Google スプレッドシートや Excel の CSV / TSV 書き出しをそのまま読める (ダブルクォート・セル内改行・BOM に対応)。
    /// <code>
    /// var rows = LocalizationCsv.Parse(text);                       // 区切り文字は 1 行目から自動判定
    /// var csv = LocalizationCsv.Write(new[] { "ja", "en" }, values); // 書き出し
    /// </code>
    /// </summary>
    /// <remarks>キーが空の行と、キーが # で始まる行 (コメント) は読み飛ばす。</remarks>
    public static class LocalizationCsv
    {
        /// <summary>1 行目にタブがあれば TSV、無ければ CSV とみなした区切り文字</summary>
        public static char DetectDelimiter(string text)
        {
            if (string.IsNullOrEmpty(text)) return ',';
            var lineEnd = text.IndexOf('\n');
            var firstLine = lineEnd < 0 ? text : text.Substring(0, lineEnd);
            return firstLine.IndexOf('\t') >= 0 ? '\t' : ',';
        }

        /// <summary>CSV (TSV) を行 × 列の文字列に分解する。区切り文字は 1 行目から自動で判定する</summary>
        public static List<List<string>> Parse(string text) => Parse(text, DetectDelimiter(text));

        /// <summary>指定した区切り文字で行 × 列の文字列に分解する</summary>
        public static List<List<string>> Parse(string text, char delimiter)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;

            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            var start = text[0] == '﻿' ? 1 : 0;

            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c != '"') cell.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        // "" はダブルクォート 1 文字
                        cell.Append('"');
                        i++;
                    }
                    else quoted = false;
                    continue;
                }

                if (c == '"')
                {
                    quoted = true;
                }
                else if (c == delimiter)
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                }
                else if (c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else if (c != '\r')
                {
                    cell.Append(c);
                }
            }

            // 最終行に改行が無い場合
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// CSV (TSV) を読み、言語ごとの辞書に書き込む。同じキーがすでにあれば上書きする。
        /// 見出しに無い言語の列は無視し、空のセルは「未翻訳」として登録しない。
        /// </summary>
        /// <param name="output">言語コード → (キー → 文字列)</param>
        public static void ReadInto(string text, Dictionary<string, Dictionary<string, string>> output)
        {
            var rows = Parse(text);
            if (rows.Count == 0) return;

            var header = rows[0];
            for (var r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0) continue;
                var key = row[0].Trim();
                if (key.Length == 0 || key[0] == '#') continue;

                for (var col = 1; col < header.Count && col < row.Count; col++)
                {
                    var language = header[col].Trim();
                    if (language.Length == 0 || string.IsNullOrEmpty(row[col])) continue;
                    if (!output.TryGetValue(language, out var table)) output[language] = table = new Dictionary<string, string>();
                    table[key] = row[col];
                }
            }
        }

        /// <summary>CSV (TSV) の見出しに書かれた言語コード</summary>
        public static IEnumerable<string> ReadLanguages(string text)
        {
            var rows = Parse(text);
            if (rows.Count == 0) yield break;
            for (var col = 1; col < rows[0].Count; col++)
            {
                var language = rows[0][col].Trim();
                if (language.Length > 0) yield return language;
            }
        }

        /// <summary>
        /// 言語ごとの辞書を CSV (TSV) に書き出す。1 行目は "key,言語1,言語2..."、キーは並べ替えて出力する。
        /// 区切り文字・ダブルクォート・改行を含むセルはダブルクォートで囲む (Google スプレッドシートにそのまま読み込める)
        /// </summary>
        public static string Write(IReadOnlyList<string> languages, IReadOnlyDictionary<string, Dictionary<string, string>> values,
            char delimiter = ',')
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var table in values.Values)
            {
                foreach (var key in table.Keys) keys.Add(key);
            }

            var sb = new StringBuilder();
            sb.Append("key");
            foreach (var language in languages) sb.Append(delimiter).Append(Escape(language, delimiter));
            sb.Append('\n');

            foreach (var key in keys)
            {
                sb.Append(Escape(key, delimiter));
                foreach (var language in languages)
                {
                    sb.Append(delimiter);
                    if (values.TryGetValue(language, out var table) && table.TryGetValue(key, out var text)) sb.Append(Escape(text, delimiter));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string Escape(string value, char delimiter)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var needsQuote = value.IndexOf(delimiter) >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            return needsQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
