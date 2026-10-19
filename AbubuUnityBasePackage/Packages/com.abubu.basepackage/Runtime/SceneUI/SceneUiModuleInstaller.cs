using Abubu.SceneFlow;
using UnityEngine.Scripting;
using Zenject;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.Presentation.SceneUiModuleInstaller))]
[assembly: AlwaysLinkAssembly]

namespace Abubu.Presentation
{
    /// <summary>
    /// ロード画面 (<see cref="SceneLoadingScreen"/>) を登録するモジュール Installer。
    /// Abubu.Scene.UI アセンブリがあり、ISceneService が登録されている (Scene モジュールが有効な) ときだけ働く。
    /// </summary>
    /// <remarks>
    /// ロード画面は uGUI に依存するため、合成ルート (Abubu.Installer) から切り離してこのアセンブリ側で登録する。
    /// 自前のロード画面を使う場合は、このモジュールを AbubuSettings.Modules.ExcludedModules に "Abubu.Scene.UI" と書いて外す。
    /// </remarks>
    [Preserve]
    public sealed class SceneUiModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public SceneUiModuleInstaller() { }

        public void Install(DiContainer container)
        {
            if (!container.HasBinding<ISceneService>()) return;
            if (container.HasBinding<SceneLoadingScreen>()) return;
            container.BindInterfacesAndSelfTo<SceneLoadingScreen>().AsSingle();
        }
    }
}
