using System.Linq;
using Abubu.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zenject;

namespace Abubu.Editor
{
    /// <summary>
    /// BootSettings.EditorStartFromBootScene が有効なら、再生開始時に Boot シーンから起動させ、
    /// 初期化後に元々開いていたシーンへ戻す (BootService が SessionState を読む)。
    /// </summary>
    /// <remarks>
    /// どのシーンを開いたまま再生しても、ビルド版と同じ初期化順 (Boot → 目的のシーン) を再現するための仕組み。
    /// 受け渡しには SessionState を使うので、値はエディタを閉じるまで保持される。
    /// <para>
    /// 既存プロジェクトへの配慮:
    /// プロジェクト側 (他のエディタ拡張) が EditorSceneManager.playModeStartScene を設定している場合は上書きしない。
    /// Abubu が設定した場合だけ、再生終了後に元の値へ戻す。
    /// 使う AbubuSettings は、実行時と同じく Resources/ProjectContext プレハブの AbubuInstaller に設定されたもの。
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class BootPlayModeHook
    {
        /// <summary>Abubu が playModeStartScene を設定したか (再生開始時の Domain Reload をまたぐため SessionState に置く)</summary>
        private const string OwnedKey = "Abubu.Boot.PlayModeStartSceneOwned";

        /// <summary>エディタ起動時・スクリプト再コンパイル時に呼ばれ、再生状態の変化を購読する。</summary>
        static BootPlayModeHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// 再生開始の直前 (ExitingEditMode) に開始シーンを Boot に差し替え、戻り先のシーンを記録する。
        /// 再生終了後 (EnteredEditMode) は、Abubu が差し替えた場合だけ元の値に戻す。
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                RestoreIfOwned();
                return;
            }
            if (state != PlayModeStateChange.ExitingEditMode) return;

            // 前回の再生で残った設定 (エディタが途中で落ちた場合など) をまず元に戻す
            RestoreIfOwned();
            SessionState.EraseString(BootService.EditorReturnSceneKey);

            var settings = FindSettings();
            var boot = settings != null ? settings.Boot : null;
            if (boot == null || !boot.EditorStartFromBootScene || string.IsNullOrEmpty(boot.BootSceneName)) return;

            // 他のエディタ拡張が開始シーンを指定している場合はそちらを優先する
            if (EditorSceneManager.playModeStartScene != null)
            {
                Debug.Log($"[Abubu] playModeStartScene が既に設定されているため ({AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)})、" +
                          "Boot シーンからの起動は行いません。(AbubuSettings > Boot > Editor Start From Boot Scene で無効化できます)");
                return;
            }

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

            SessionState.SetBool(OwnedKey, true);
            EditorSceneManager.playModeStartScene = bootScene;
            // 名前ではなくパスで渡す: Build Settings 未登録のシーンにも戻れるように (SceneService がパスを解釈する)
            SessionState.SetString(BootService.EditorReturnSceneKey, current.path);
        }

        /// <summary>
        /// Abubu が playModeStartScene を設定していた場合だけ、設定前の値 (null) に戻す。
        /// 他者が設定済みのときは差し替えないので、設定前の値は常に null。
        /// </summary>
        private static void RestoreIfOwned()
        {
            if (!SessionState.GetBool(OwnedKey, false)) return;

            EditorSceneManager.playModeStartScene = null;
            SessionState.EraseBool(OwnedKey);
        }

        /// <summary>
        /// 実行時に使われる AbubuSettings を探す。
        /// Zenject が自動で読み込む Resources/ProjectContext プレハブの AbubuInstaller に設定されたものを優先し、
        /// 見つからない場合はプロジェクト内に AbubuSettings が 1 つだけあればそれを使う (複数あるときはどれが使われるか分からないので使わない)。
        /// </summary>
        private static AbubuSettings FindSettings()
        {
            var context = Resources.Load<ProjectContext>(ProjectContext.ProjectContextResourcePath);
            if (context != null)
            {
                var installer = context.GetComponentInChildren<AbubuInstaller>(true);
                if (installer != null)
                {
                    // settings は private なシリアライズフィールドなので SerializedObject 経由で読む
                    using var serialized = new SerializedObject(installer);
                    var property = serialized.FindProperty("settings");
                    if (property != null && property.objectReferenceValue is AbubuSettings fromContext) return fromContext;
                }
            }

            var guids = AssetDatabase.FindAssets("t:" + nameof(AbubuSettings));
            return guids.Length == 1 ? AssetDatabase.LoadAssetAtPath<AbubuSettings>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }
    }
}
