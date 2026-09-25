using System.Linq;
using Abubu.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Abubu.Editor
{
    /// <summary>
    /// BootSettings.EditorStartFromBootScene が有効なら、再生開始時に Boot シーンから起動させ、
    /// 初期化後に元々開いていたシーンへ戻す (BootService が SessionState を読む)。
    /// </summary>
    [InitializeOnLoad]
    internal static class BootPlayModeHook
    {
        static BootPlayModeHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }
            if (state != PlayModeStateChange.ExitingEditMode) return;

            EditorSceneManager.playModeStartScene = null;
            SessionState.EraseString(BootService.EditorReturnSceneKey);

            var settings = AssetDatabase.FindAssets("t:" + nameof(AbubuSettings))
                .Select(guid => AssetDatabase.LoadAssetAtPath<AbubuSettings>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(s => s != null);
            var boot = settings?.Boot;
            if (boot == null || !boot.EditorStartFromBootScene || string.IsNullOrEmpty(boot.BootSceneName)) return;

            var bootScene = AssetDatabase.FindAssets("t:Scene " + boot.BootSceneName)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == boot.BootSceneName)
                .Select(AssetDatabase.LoadAssetAtPath<SceneAsset>)
                .FirstOrDefault();
            if (bootScene == null) return;

            var current = EditorSceneManager.GetActiveScene();
            if (current.name == boot.BootSceneName || string.IsNullOrEmpty(current.path)) return;

            EditorSceneManager.playModeStartScene = bootScene;
            // 名前ではなくパスで渡す: Build Settings 未登録のシーンにも戻れるように (SceneService がパスを解釈する)
            SessionState.SetString(BootService.EditorReturnSceneKey, current.path);
        }
    }
}
