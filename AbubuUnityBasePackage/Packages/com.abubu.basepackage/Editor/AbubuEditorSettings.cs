using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Abubu.Editor
{
    /// <summary>
    /// エディタ拡張 (Setup Project / Generate Keys) の設定。ProjectSettings/AbubuEditorSettings.asset に保存され、git で共有できる。
    /// Edit > Project Settings > Abubu で編集する。
    /// </summary>
    [FilePath("ProjectSettings/AbubuEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class AbubuEditorSettings : ScriptableSingleton<AbubuEditorSettings>
    {
        [Header("Setup Project")]
        [Tooltip("AbubuSettings / SoundLibrary などを作るフォルダ")]
        public string SetupFolder = "Assets/Abubu";

        [Tooltip("ProjectContext が無いときに作るプレハブのパス (Resources フォルダ直下の ProjectContext.prefab であること)")]
        public string ProjectContextPath = "Assets/Resources/ProjectContext.prefab";

        [Header("Generate Keys")]
        [Tooltip("キー定数を書き出すファイル")]
        public string KeysOutputPath = "Assets/Abubu/Generated/AbubuKeys.cs";

        [Tooltip("キー定数の名前空間")]
        public string KeysNamespace = "Abubu";

        [Tooltip("出力先のフォルダに asmdef も作る (asmdef で分割したプロジェクトから参照できるように)")]
        public bool KeysGenerateAsmdef;

        [Tooltip("作る asmdef の名前")]
        public string KeysAsmdefName = "Abubu.Generated";

        [Tooltip("Packages 以下 (埋め込みパッケージ) のライブラリ・翻訳テーブルも対象にする")]
        public bool KeysSearchPackages;

        public void Save() => Save(true);

        /// <summary>アセットを探すフォルダ</summary>
        public string[] SearchFolders => KeysSearchPackages ? new[] { "Assets", "Packages" } : new[] { "Assets" };
    }

    internal static class AbubuEditorSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create() => new("Project/Abubu", SettingsScope.Project, new HashSet<string> { "Abubu", "Keys", "Setup" })
        {
            guiHandler = _ =>
            {
                var settings = AbubuEditorSettings.instance;
                var so = new SerializedObject(settings);
                EditorGUI.BeginChangeCheck();
                var property = so.GetIterator();
                property.NextVisible(true);
                while (property.NextVisible(false))
                {
                    if (property.name == "m_Script") continue;
                    EditorGUILayout.PropertyField(property, true);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    settings.Save();
                }
            },
        };
    }
}
