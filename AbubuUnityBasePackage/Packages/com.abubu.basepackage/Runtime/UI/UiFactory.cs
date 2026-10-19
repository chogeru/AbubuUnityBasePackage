using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.UI
{
    /// <summary>
    /// ポップアップ・画面のインスタンスを作る / 片付ける処理。
    /// Addressables から読み込む・自前のプールを使う・別の DI コンテナで注入する、などの場合に差し替える。
    /// <code>
    /// // AbubuInstaller より前の Installer で
    /// Container.Bind&lt;IUiFactory&gt;().To&lt;MyUiFactory&gt;().AsSingle();
    /// </code>
    /// </summary>
    public interface IUiFactory
    {
        /// <summary>プレハブから parent の下にインスタンスを作る</summary>
        GameObject Create(GameObject prefab, Transform parent);

        /// <summary>閉じたインスタンスを片付ける (既定は Destroy)</summary>
        void Release(GameObject instance);
    }

    /// <summary>
    /// 既定の <see cref="IUiFactory"/>。DI コンテナ経由で生成するので、ポップアップ・画面のコンポーネントで [Inject] が使える。
    /// シーン側のバインド (Model など) も注入できるよう、アクティブなシーンの SceneContext を優先する。
    /// </summary>
    public sealed class ContainerUiFactory : IUiFactory
    {
        private readonly DiContainer _container;

        /// <param name="container">ProjectContext のコンテナ。null なら注入なしで Instantiate する (テスト用)</param>
        public ContainerUiFactory([InjectOptional] DiContainer container = null) => _container = container;

        /// <summary>生成に使うコンテナ (アクティブなシーンの SceneContext、無ければ ProjectContext)。注入なしの場合は null</summary>
        public DiContainer ResolveContainer()
        {
            if (_container == null) return null;

            var scene = SceneManager.GetActiveScene();
            foreach (var context in Object.FindObjectsByType<SceneContext>(FindObjectsSortMode.None))
            {
                if (context.gameObject.scene == scene && context.HasResolved) return context.Container;
            }
            return _container;
        }

        public GameObject Create(GameObject prefab, Transform parent)
        {
            var container = ResolveContainer();
            return container == null ? Object.Instantiate(prefab, parent) : container.InstantiatePrefab(prefab, parent);
        }

        public void Release(GameObject instance)
        {
            if (instance != null) Object.Destroy(instance);
        }
    }
}
