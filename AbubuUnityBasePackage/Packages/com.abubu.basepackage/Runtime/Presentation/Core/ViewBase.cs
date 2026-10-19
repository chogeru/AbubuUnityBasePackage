using UnityEngine;
using Zenject;

namespace Abubu.Presentation
{
    /// <summary>
    /// 自分用の Presenter を自動生成する View 基底。
    /// Installer を書かなくても、シーンに置くだけで MVP が成立する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Presenter の生成に使うコンテナの決め方:
    /// Zenject に Inject された View (SceneContext 配下のシーンオブジェクトや InstantiatePrefab したもの) は Inject 元のコンテナ、
    /// そうでなければ <see cref="AbubuServices.ResolveContainer"/> (同じシーンの SceneContext → ProjectContext)。
    /// どちらも無い場合は理由を警告して何もしない。
    /// </para>
    /// <para>
    /// コンテナに <typeparamref name="TPresenter"/> がすでにバインドされている場合は自動生成しない
    /// (Installer 側で生成・初期化される前提。二重生成を防ぐため)。Auto Create Presenter をオフにしても同じ。
    /// </para>
    /// <para>
    /// Start / OnDestroy は基底で使っているため、派生クラスでは <see cref="OnViewStart"/> / <see cref="OnViewDestroy"/> を使う。
    /// </para>
    /// </remarks>
    public abstract class ViewBase<TPresenter> : MonoBehaviour where TPresenter : PresenterBase
    {
        [Tooltip("オンなら Start で Presenter を自動生成する。Installer で Presenter をバインドする場合はオフにする (バインド済みなら自動でスキップされる)")]
        [SerializeField] private bool autoCreatePresenter = true;

        private DiContainer _container;
        private TPresenter _presenter;

        /// <summary>自動生成した Presenter (生成前・生成しなかった場合は null)</summary>
        protected TPresenter Presenter => _presenter;

        /// <summary>Zenject から Inject されたときに呼ばれる。Presenter の生成にこのコンテナを使う</summary>
        /// <remarks>派生クラスの [Inject] メソッドと名前がぶつからないよう private にしている (Zenject は基底の private メソッドにも Inject する)</remarks>
        [Inject]
        private void InjectViewContainer(DiContainer container) => _container = container;

        private void Start()
        {
            OnViewStart();
            if (autoCreatePresenter) CreatePresenter();
        }

        private void OnDestroy()
        {
            _presenter?.Dispose();
            _presenter = null;
            OnViewDestroy();
        }

        /// <summary>Start のタイミングで呼ばれる (Presenter の生成より前)。UI 部品の準備などに使う</summary>
        protected virtual void OnViewStart() { }

        /// <summary>OnDestroy のタイミングで呼ばれる (Presenter の破棄より後)</summary>
        protected virtual void OnViewDestroy() { }

        private void CreatePresenter()
        {
            var container = _container;
            if (container == null)
            {
                if (!AbubuServices.IsAvailable)
                {
                    ViewBaseWarnings.WarnOnce(
                        $"[Abubu.Presentation] {GetType().Name}: DI コンテナが見つからないため {typeof(TPresenter).Name} を生成できません。" +
                        "SceneContext に Inject させるか、Tools > Abubu > Setup Project で ProjectContext を用意してください。", this);
                    return;
                }
                container = AbubuServices.ResolveContainer(gameObject.scene);
            }

            // Installer 側でバインド済みなら、そちらが生成・初期化を担当する
            if (container.HasBinding<TPresenter>()) return;

            _presenter = container.Instantiate<TPresenter>(new object[] { this });
            _presenter.Initialize();
        }
    }

    /// <summary>ViewBase の警告を 1 回だけ出すための管理 (ジェネリック型ごとに分かれないよう非ジェネリックに置く)</summary>
    internal static class ViewBaseWarnings
    {
        private static bool _warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => _warned = false;

        public static void WarnOnce(string message, Object context)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning(message, context);
        }
    }
}
