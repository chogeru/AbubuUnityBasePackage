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
    /// <remarks>
    /// どのシーンを開いたまま再生しても、ビルド版と同じ初期化順 (Boot → 目的のシーン) を再現するための仕組み。
    /// 受け渡しには SessionState を使うので、値はエディタを閉じるまで保持される。
    /// </remarks>
    [InitializeOnLoad]
    internal static class BootPlayModeHook
    {
        /// <summary>エディタ起動時・スクリプト再コンパイル時に呼ばれ、再生状態の変化を購読する。</summary>
        static BootPlayModeHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// 再生開始の直前 (ExitingEditMode) に開始シーンを Boot に差し替え、戻り先のシーンを記録する。
        /// 再生終了後 (EnteredEditMode) は差し替えを解除して、通常の再生に影響が残らないようにする。
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }
            if (state != PlayModeStateChange.ExitingEditMode) return;

            // 前回の再生で残った設定をまず消す (設定が無効な場合もここで確実に元に戻る)
            EditorSceneManager.playModeStartScene = null;
            SessionState.EraseString(BootService.EditorReturnSceneKey);

            // プロジェクト内の最初の AbubuSettings を採用する
            var settings = AssetDatabase.FindAssets("t:" + nameof(AbubuSettings))
                .Select(guid => AssetDatabase.LoadAssetAtPath<AbubuSettings>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(s => s != null);
            var boot = settings?.Boot;
            if (boot == null || !boot.EditorStartFromBootScene || string.IsNullOrEmpty(boot.BootSceneName)) return;

            // FindAssets は部分一致で探すため、ファイル名が完全一致するものに絞り込む
            var bootScene = AssetDatabase.FindAssets("t:Scene " + boot.BootSceneName)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == boot.BootSceneName)
                .Select(AssetDatabase.LoadAssetAtPath<SceneAsset>)
                .FirstOrDefault();
            if (bootScene == null) return;

            // Boot シーン自身を開いている場合と、未保存の新規シーン (path が空) の場合は差し替えない
            var current = EditorSceneManager.GetActiveScene();
            if (current.name == boot.BootSceneName || string.IsNullOrEmpty(current.path)) return;

            EditorSceneManager.playModeStartScene = bootScene;
            // 名前ではなくパスで渡す: Build Settings 未登録のシーンにも戻れるように (SceneService がパスを解釈する)
            SessionState.SetString(BootService.EditorReturnSceneKey, current.path);
        }
    }
}
