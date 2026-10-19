using System;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Samples
{
    /// <summary>SampleTitle → SampleGame に渡すデータ</summary>
    [Serializable]
    public sealed class SampleGamePayload
    {
        public string Message;
        public string StartedAt;
    }

    /// <summary>
    /// サンプルシーンの遷移先を決める。
    /// Build Profiles (Build Settings) に登録されていればシーン名で、未登録ならエディタ上だけシーンのパスで読み込む
    /// (SceneService はエディタで "Assets/.../Foo.unity" 形式のパスを受け付ける)。
    /// これにより、サンプルのシーンを Build Settings に追加しなくても (= リリースビルドに含めなくても) エディタで動作確認できる。
    /// </summary>
    public static class SampleScenes
    {
        public static string Resolve(string sceneName, string editorScenePath)
        {
            if (Application.CanStreamedLevelBeLoaded(sceneName)) return sceneName;
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(editorScenePath)) return editorScenePath;
#endif
            return sceneName;
        }
    }

    /// <summary>イベントバスの動作確認用メッセージ</summary>
    public readonly struct SampleScored
    {
        public readonly int Total;
        public SampleScored(int total) => Total = total;
    }
}
