using System.Linq;
using Abubu.Audio;
using Abubu.Effects;
using UnityEditor;
using UnityEngine;
using Zenject;

namespace Abubu.Editor
{
    /// <summary>
    /// Tools > Abubu > Setup Project
    /// AbubuSettings / SoundLibrary と、AbubuInstaller 入りの Resources/ProjectContext.prefab を生成する。
    /// 既に ProjectContext がある場合は AbubuInstaller を追加するだけ (既存の Installer は保持)。
    /// </summary>
    /// <remarks>
    /// 何度実行しても結果が変わらない (冪等) ように作ってある。
    /// 既存アセットは上書きせず、未設定の参照だけを補完する。
    /// </remarks>
    public static class AbubuProjectSetup
    {
        /// <summary>生成するアセット一式の置き場所</summary>
        private const string RootFolder = "Assets/Abubu";
        private const string SettingsPath = RootFolder + "/AbubuSettings.asset";
        private const string LibraryPath = RootFolder + "/SoundLibrary.asset";
        private const string EffectLibraryPath = RootFolder + "/EffectLibrary.asset";

        /// <summary>
        /// ProjectContext が見つからなかったときに新規作成するパス。
        /// Zenject は Resources/ProjectContext.prefab を自動で読み込む。
        /// </summary>
        private const string DefaultProjectContextPath = "Assets/Resources/ProjectContext.prefab";

        /// <summary>
        /// セットアップ本体。設定アセットを用意し、ProjectContext に AbubuInstaller を組み込む。
        /// </summary>
        [MenuItem("Tools/Abubu/Setup Project", priority = 0)]
        public static void Setup()
        {
            EnsureFolder(RootFolder);

            // 各ライブラリと設定アセットを用意し、設定側の参照が空ならライブラリを割り当てる
            var library = LoadOrCreate<SoundLibrary>(LibraryPath);
            var effectLibrary = LoadOrCreate<EffectLibrary>(EffectLibraryPath);
            var settings = LoadOrCreate<AbubuSettings>(SettingsPath);
            if (settings.Sound.Library == null) settings.Sound.Library = library;
            if (settings.Effect.Library == null) settings.Effect.Library = effectLibrary;
            EditorUtility.SetDirty(settings);

            // ProjectContext が無ければ作り、あれば Installer を追加する
            var prefabPath = FindProjectContextPrefab();
            if (prefabPath == null) CreateProjectContext(settings);
            else AddInstallerToExisting(prefabPath, settings);

            AssetDatabase.SaveAssets();

            // 次に触るべき設定アセットを Project ウィンドウで選択状態にしておく
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            Debug.Log("[Abubu] セットアップ完了。SoundLibrary にサウンドを登録すれば Sound.PlaySe(\"key\") で鳴らせます。");
        }

        /// <summary>
        /// Zenject が読み込む位置 (任意の Resources フォルダ直下の ProjectContext.prefab) にあるプレハブを探す。
        /// </summary>
        /// <returns>見つかったプレハブのアセットパス。無ければ null。</returns>
        private static string FindProjectContextPrefab() =>
            AssetDatabase.FindAssets("ProjectContext t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/Resources/" + ProjectContext.ProjectContextResourcePath + ".prefab"));

        /// <summary>
        /// ProjectContext + AbubuInstaller を持つプレハブを新規作成する。
        /// </summary>
        private static void CreateProjectContext(AbubuSettings settings)
        {
            EnsureFolder("Assets/Resources");

            // 一時的にシーンへ GameObject を作ってプレハブ化し、最後に必ず破棄する
            var go = new GameObject("ProjectContext");
            try
            {
                var context = go.AddComponent<ProjectContext>();
                var installer = AddInstaller(go, settings);
                context.Installers = new MonoInstaller[] { installer };
                PrefabUtility.SaveAsPrefabAsset(go, DefaultProjectContextPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 既存の ProjectContext プレハブに AbubuInstaller を追加する。
        /// 既に付いている場合は settings の参照だけを補完し、Installers リストへの重複登録も防ぐ。
        /// </summary>
        private static void AddInstallerToExisting(string prefabPath, AbubuSettings settings)
        {
            // スコープを抜けるとプレハブへの変更が保存される
            using var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath);
            var root = scope.prefabContentsRoot;
            var context = root.GetComponent<ProjectContext>();
            if (context == null)
            {
                Debug.LogError($"[Abubu] {prefabPath} に ProjectContext コンポーネントがありません。");
                return;
            }

            var installer = root.GetComponent<AbubuInstaller>();
            if (installer == null) installer = AddInstaller(root, settings);
            else SetSettings(installer, settings);

            if (!context.Installers.Contains(installer))
            {
                context.Installers = context.Installers.Append(installer).ToList();
            }
        }

        /// <summary>AbubuInstaller を追加し、設定アセットを割り当てる。</summary>
        private static AbubuInstaller AddInstaller(GameObject go, AbubuSettings settings)
        {
            var installer = go.AddComponent<AbubuInstaller>();
            SetSettings(installer, settings);
            return installer;
        }

        /// <summary>
        /// AbubuInstaller の private シリアライズフィールド "settings" に設定アセットを割り当てる。
        /// ユーザーが別の設定を割り当て済みなら上書きしない。
        /// </summary>
        private static void SetSettings(AbubuInstaller installer, AbubuSettings settings)
        {
            // フィールドは private のため SerializedObject 経由で書き込む
            var so = new SerializedObject(installer);
            var prop = so.FindProperty("settings");
            if (prop.objectReferenceValue == null) prop.objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 指定パスのアセットを読み込む。無ければ新規作成して返す。
        /// </summary>
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>
        /// フォルダを親から順に再帰的に作成する (mkdir -p 相当)。
        /// </summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
