using System.Collections.Generic;
using System.Text;

namespace Abubu.Localization
{
    /// <summary>
    /// ローカライズ用 CSV の読み込み。1 行目が見出し (key,ja,en ...)、2 行目以降が 1 キー 1 行。
    /// Google スプレッドシートや Excel の CSV 書き出しをそのまま読める (ダブルクォート・セル内改行・BOM に対応)。
    /// </summary>
    /// <remarks>キーが空の行と、キーが # で始まる行 (コメント) は読み飛ばす。</remarks>
    public static class LocalizationCsv
    {
        /// <summary>CSV を行 × 列の文字列に分解する</summary>
        public static List<List<string>> Parse(string text)
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

                switch (c)
                {
                    case '"':
                        quoted = true;
                        break;
                    case ',':
                        row.Add(cell.ToString());
                        cell.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(cell.ToString());
                        cell.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default:
                        cell.Append(c);
                        break;
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
        /// CSV を読み、言語ごとの辞書に書き込む。同じキーがすでにあれば上書きする。
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

        /// <summary>CSV の見出しに書かれた言語コード</summary>
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
    }
}
