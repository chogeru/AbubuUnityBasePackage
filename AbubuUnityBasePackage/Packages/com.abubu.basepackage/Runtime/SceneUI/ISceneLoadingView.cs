namespace Abubu.Presentation
{
    /// <summary>
    /// ロード画面の View。ロード画面プレハブのルートに、これを実装した MonoBehaviour を付ける。
    /// 標準の uGUI 実装は <see cref="SceneLoadingView"/>。
    /// </summary>
    public interface ISceneLoadingView
    {
        /// <summary>表示/非表示を切り替える</summary>
        void SetVisible(bool visible);

        /// <summary>ロードの進捗 (0〜1) を表示する</summary>
        void SetProgress(float value);
    }
}
