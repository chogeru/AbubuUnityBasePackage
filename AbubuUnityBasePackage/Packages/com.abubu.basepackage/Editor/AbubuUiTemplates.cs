using System.Linq;
using Abubu.UI;
using UnityEditor;
using UnityEngine;

namespace Abubu.Editor
{
    /// <summary>
    /// Tools > Abubu > Create UI Templates
    /// 組み込みの確認ダイアログ・トーストをプレハブとして書き出し、AbubuUiSettings に設定する。
    /// 書き出したプレハブを編集すれば、Popups.Confirm / Popups.Toast の見た目を変えられる (フォントの差し替えなど)。
    /// </summary>
    /// <remarks>既にあるプレハブは上書きしない。</remarks>
    public static class AbubuUiTemplates
    {
        [MenuItem("Tools/Abubu/Create UI Templates", priority = 3)]
        public static void Create()
        {
            var folder = (string.IsNullOrWhiteSpace(AbubuEditorSettings.instance.SetupFolder) ? "Assets/Abubu" : AbubuEditorSettings.instance.SetupFolder.TrimEnd('/')) + "/UI";
            EnsureFolder(folder);

            var dialog = SavePrefab(folder + "/ConfirmDialog.prefab", () => UiDefaultWidgets.CreateConfirmDialog().gameObject);
            var toast = SavePrefab(folder + "/Toast.prefab", () => UiDefaultWidgets.CreateToast().gameObject);

            var uiSettings = FindOrCreateUiSettings(folder);
            if (uiSettings.ConfirmDialogPrefab == null) uiSettings.ConfirmDialogPrefab = dialog.GetComponent<UiConfirmDialog>();
            if (uiSettings.ToastPrefab == null) uiSettings.ToastPrefab = toast.GetComponent<UiToast>();
            EditorUtility.SetDirty(uiSettings);
            AssetDatabase.SaveAssets();

            Selection.activeObject = dialog;
            EditorGUIUtility.PingObject(dialog);
            Debug.Log($"[Abubu] UI のテンプレートを作成しました: {folder} (AbubuUiSettings に設定済み)");
        }

        private static GameObject SavePrefab(string path, System.Func<GameObject> build)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = build();
            try
            {
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>AbubuSettings.Ui → Resources/AbubuUiSettings → 新規作成 の順に探し、AbubuSettings.Ui が空なら設定する</summary>
        private static AbubuUiSettings FindOrCreateUiSettings(string folder)
        {
            var abubuSettings = AssetDatabase.FindAssets("t:AbubuSettings")
                .Select(guid => AssetDatabase.LoadAssetAtPath<AbubuSettings>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(s => s != null);
            var uiSettings = abubuSettings != null ? abubuSettings.Ui : null;
            if (uiSettings == null) uiSettings = Resources.Load<AbubuUiSettings>(AbubuUiSettings.ResourcePath);
            if (uiSettings == null)
            {
                uiSettings = ScriptableObject.CreateInstance<AbubuUiSettings>();
                AssetDatabase.CreateAsset(uiSettings, folder + "/AbubuUiSettings.asset");
            }

            if (abubuSettings != null && abubuSettings.Ui == null)
            {
                abubuSettings.Ui = uiSettings;
                EditorUtility.SetDirty(abubuSettings);
            }
            return uiSettings;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
