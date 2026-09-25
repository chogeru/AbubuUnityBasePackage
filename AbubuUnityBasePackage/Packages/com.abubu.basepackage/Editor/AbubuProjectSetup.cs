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
    public static class AbubuProjectSetup
    {
        private const string RootFolder = "Assets/Abubu";
        private const string SettingsPath = RootFolder + "/AbubuSettings.asset";
        private const string LibraryPath = RootFolder + "/SoundLibrary.asset";
        private const string EffectLibraryPath = RootFolder + "/EffectLibrary.asset";
        private const string DefaultProjectContextPath = "Assets/Resources/ProjectContext.prefab";

        [MenuItem("Tools/Abubu/Setup Project", priority = 0)]
        public static void Setup()
        {
            EnsureFolder(RootFolder);

            var library = LoadOrCreate<SoundLibrary>(LibraryPath);
            var effectLibrary = LoadOrCreate<EffectLibrary>(EffectLibraryPath);
            var settings = LoadOrCreate<AbubuSettings>(SettingsPath);
            if (settings.Sound.Library == null) settings.Sound.Library = library;
            if (settings.Effect.Library == null) settings.Effect.Library = effectLibrary;
            EditorUtility.SetDirty(settings);

            var prefabPath = FindProjectContextPrefab();
            if (prefabPath == null) CreateProjectContext(settings);
            else AddInstallerToExisting(prefabPath, settings);

            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            Debug.Log("[Abubu] セットアップ完了。SoundLibrary にサウンドを登録すれば Sound.PlaySe(\"key\") で鳴らせます。");
        }

        private static string FindProjectContextPrefab() =>
            AssetDatabase.FindAssets("ProjectContext t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/Resources/" + ProjectContext.ProjectContextResourcePath + ".prefab"));

        private static void CreateProjectContext(AbubuSettings settings)
        {
            EnsureFolder("Assets/Resources");

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

        private static void AddInstallerToExisting(string prefabPath, AbubuSettings settings)
        {
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

        private static AbubuInstaller AddInstaller(GameObject go, AbubuSettings settings)
        {
            var installer = go.AddComponent<AbubuInstaller>();
            SetSettings(installer, settings);
            return installer;
        }

        private static void SetSettings(AbubuInstaller installer, AbubuSettings settings)
        {
            var so = new SerializedObject(installer);
            var prop = so.FindProperty("settings");
            if (prop.objectReferenceValue == null) prop.objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
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
