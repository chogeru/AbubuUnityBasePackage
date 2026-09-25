using UnityEngine;
using Zenject;

namespace Abubu.Presentation
{
    /// <summary>
    /// 自分用の Presenter を自動生成する View 基底。
    /// Installer を書かなくても、シーンに置くだけで MVP が成立する (ゲームジャム向け)。
    /// Presenter の依存 (Model など) は同じシーンの SceneContext → ProjectContext の順で解決される。
    /// Installer で明示的に Presenter をバインドしたい場合は Auto Create Presenter をオフにする。
    /// </summary>
    public abstract class ViewBase<TPresenter> : MonoBehaviour where TPresenter : PresenterBase
    {
        [SerializeField] private bool autoCreatePresenter = true;

        private TPresenter _presenter;

        protected virtual void Start()
        {
            if (!autoCreatePresenter || !AbubuServices.IsAvailable) return;
            _presenter = AbubuServices.ResolveContainer(gameObject.scene).Instantiate<TPresenter>(new object[] { this });
            _presenter.Initialize();
        }

        protected virtual void OnDestroy() => _presenter?.Dispose();
    }
}
