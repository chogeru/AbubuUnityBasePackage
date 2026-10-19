using Zenject;

namespace Abubu.DisplaySettings
{
    /// <summary>
    /// 画面設定機能 (Model / Applier / Store) をまとめてコンテナに登録する。
    /// <code>
    /// GraphicsSettingsInstaller.Install(Container, settings.Graphics);
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>すでに同じ型がバインド済みなら、その項目はスキップする (独自実装への差し替え用)。</para>
    /// <para>
    /// Unity への反映を止めたい場合は <paramref name="applyToUnity"/> を false にする
    /// (設定画面だけ使い、反映は自前で行う場合など)。保存を止めたい場合は <see cref="GraphicsSettingsOptions.Persist"/> を false にする。
    /// </para>
    /// </remarks>
    public static class GraphicsSettingsInstaller
    {
        /// <param name="container">登録先のコンテナ</param>
        /// <param name="options">構成。null なら既定値</param>
        /// <param name="applyToUnity">false なら <see cref="GraphicsSettingsApplier"/> を登録しない</param>
        public static void Install(DiContainer container, GraphicsSettingsOptions options = null, bool applyToUnity = true)
        {
            options ??= new GraphicsSettingsOptions();

            if (!container.HasBinding<GraphicsSettingsModel>())
            {
                container.BindInterfacesAndSelfTo<GraphicsSettingsModel>().AsSingle().WithArguments(options);
            }
            if (!container.HasBinding<GraphicsSettingsStore>())
            {
                container.BindInterfacesAndSelfTo<GraphicsSettingsStore>().AsSingle().WithArguments(options).NonLazy();
            }
            if (applyToUnity && !container.HasBinding<GraphicsSettingsApplier>())
            {
                container.BindInterfacesAndSelfTo<GraphicsSettingsApplier>().AsSingle().NonLazy();
            }

            // 保存データの復元 → 反映の順になるよう、Store を先に初期化する (逆順でも変更通知で反映されるが、二重適用を避ける)
            container.BindInitializableExecutionOrder<GraphicsSettingsStore>(-100);
        }
    }
}
