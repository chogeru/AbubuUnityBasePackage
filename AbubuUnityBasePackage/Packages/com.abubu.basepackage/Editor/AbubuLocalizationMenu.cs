using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Abubu.Localization;
using UnityEditor;
using UnityEngine;

namespace Abubu.Editor
{
    /// <summary>
    /// 翻訳テーブルの CSV / TSV 書き出し・読み込み。Google スプレッドシートで翻訳してから戻す、という使い方を想定している。
    /// Tools > Abubu > Localization、または AbubuLocalizationTable の右クリック (Inspector の ⋮) メニューから使う。
    /// </summary>
    public static class AbubuLocalizationMenu
    {
        private const string MenuRoot = "Tools/Abubu/Localization/";

        [MenuItem(MenuRoot + "Export CSV...", priority = 40)]
        private static void ExportCsv() => Export(FindTargetTable(), ',');

        [MenuItem(MenuRoot + "Export TSV...", priority = 41)]
        private static void ExportTsv() => Export(FindTargetTable(), '\t');

        [MenuItem(MenuRoot + "Import CSV or TSV...", priority = 42)]
        private static void Import() => Import(FindTargetTable());

        [MenuItem("CONTEXT/LocalizationTable/Export CSV...")]
        private static void ExportCsvContext(MenuCommand command) => Export(command.context as AbubuLocalizationTable, ',');

        [MenuItem("CONTEXT/LocalizationTable/Import CSV or TSV...")]
        private static void ImportContext(MenuCommand command) => Import(command.context as AbubuLocalizationTable);

        private static void Export(AbubuLocalizationTable table, char delimiter)
        {
            if (table == null) return;
            var extension = delimiter == '\t' ? "tsv" : "csv";
            var path = EditorUtility.SaveFilePanel("翻訳テーブルを書き出す", "", $"{table.name}.{extension}", extension);
            if (string.IsNullOrEmpty(path)) return;

            File.WriteAllText(path, LocalizationTableIO.Export(table, delimiter), new UTF8Encoding(true));
            Debug.Log($"[Abubu.Localization] {table.name} を書き出しました: {path}");
        }

        private static void Import(AbubuLocalizationTable table)
        {
            if (table == null) return;
            var path = EditorUtility.OpenFilePanelWithFilters("翻訳を読み込む", "", new[] { "CSV / TSV", "csv,tsv,txt" });
            if (string.IsNullOrEmpty(path)) return;

            Undo.RecordObject(table, "Import Localization");
            var result = LocalizationTableIO.Import(table, File.ReadAllText(path));
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssetIfDirty(table);
            Debug.Log($"[Abubu.Localization] {table.name} に読み込みました: 追加 {result.Added} 件 / 更新 {result.Updated} 件 (言語: {string.Join(", ", table.Languages)})");
        }

        /// <summary>選択中の AbubuLocalizationTable。無ければ AbubuSettings に設定されているもの</summary>
        private static AbubuLocalizationTable FindTargetTable()
        {
            if (Selection.activeObject is AbubuLocalizationTable selected) return selected;

            var table = AssetDatabase.FindAssets("t:AbubuSettings")
                .Select(guid => AssetDatabase.LoadAssetAtPath<AbubuSettings>(AssetDatabase.GUIDToAssetPath(guid)))
                .Select(s => s != null ? s.Localization.Table : null)
                .FirstOrDefault(t => t != null);
            if (table == null)
            {
                EditorUtility.DisplayDialog("Abubu", "LocalizationTable を Project ウィンドウで選択してから実行してください。", "OK");
            }
            return table;
        }
    }

    /// <summary>AbubuLocalizationTable と CSV / TSV の変換 (テストしやすいよう副作用はテーブルの書き換えだけ)</summary>
    public static class LocalizationTableIO
    {
        /// <summary>テーブルの文字列 (Includes・Csv Sources を含む) を CSV / TSV にする</summary>
        public static string Export(AbubuLocalizationTable table, char delimiter = ',') =>
            LocalizationCsv.Write(table.GetAllLanguages(), table.Build(), delimiter);

        /// <summary>
        /// CSV / TSV をテーブルの Entries に取り込む。同じキーは上書き、無いキーは追加、見出しに無い言語は Languages に追加する。
        /// 空のセルは既存の訳を消さない
        /// </summary>
        public static (int Added, int Updated) Import(AbubuLocalizationTable table, string text)
        {
            var rows = LocalizationCsv.Parse(text);
            if (rows.Count == 0) return (0, 0);

            var header = rows[0];
            var columns = new List<int>();
            for (var col = 1; col < header.Count; col++)
            {
                var language = header[col].Trim();
                if (language.Length == 0)
                {
                    columns.Add(-1);
                    continue;
                }
                var index = table.Languages.FindIndex(l => LanguageCode.Normalize(l) == LanguageCode.Normalize(language));
                if (index < 0)
                {
                    table.Languages.Add(language);
                    index = table.Languages.Count - 1;
                }
                columns.Add(index);
            }

            int added = 0, updated = 0;
            var entries = table.Entries.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Key))
                .GroupBy(e => e.Key.Trim()).ToDictionary(g => g.Key, g => g.Last());
            for (var r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0) continue;
                var key = row[0].Trim();
                if (key.Length == 0 || key[0] == '#') continue;

                if (!entries.TryGetValue(key, out var entry))
                {
                    entry = new LocalizationEntry { Key = key };
                    table.Entries.Add(entry);
                    entries[key] = entry;
                    added++;
                }
                else
                {
                    updated++;
                }

                for (var col = 1; col < row.Count && col - 1 < columns.Count; col++)
                {
                    var languageIndex = columns[col - 1];
                    if (languageIndex < 0 || string.IsNullOrEmpty(row[col])) continue;
                    while (entry.Values.Count <= languageIndex) entry.Values.Add(string.Empty);
                    entry.Values[languageIndex] = row[col];
                }
            }
            return (added, updated);
        }
    }
}
