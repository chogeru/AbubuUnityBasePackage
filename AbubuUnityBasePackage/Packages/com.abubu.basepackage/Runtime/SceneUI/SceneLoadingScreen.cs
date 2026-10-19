using System;
using Abubu.SceneFlow;
using R3;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.Presentation
{
    /// <summary>
    /// ISceneService.ShowLoadingScreen を購読し、設定されたロード画面プレハブを表示する。
    /// SceneService 側は View を知らない (Scene → Presentation の依存を作らない)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ロード中はフェード用 Canvas (sortingOrder = <see cref="SceneSettings.SortingOrder"/>) が画面を覆っているため、
    /// プレハブのルートに Canvas があれば sortingOrder を SceneSettings.SortingOrder + <see cref="SortingOrderOffset"/> に設定して手前に出す。
    /// ルートに Canvas が無い (子の Canvas だけ) 場合は設定しないので、自分で十分大きな値にしておくこと。
    /// </para>
    /// <para>生成したインスタンスにはコンテナから Inject するので、独自の View でも [Inject] が使える。</para>
    /// </remarks>
    public class SceneLoadingScreen : IInitializable, IDisposable
    {
        /// <summary>フェード用 Canvas に対して、ロード画面をどれだけ手前に置くか</summary>
        public const int SortingOrderOffset = 1;

        private readonly ISceneService _scene;
        private readonly SceneSettings _settings;
        private readonly DiContainer _container;
        private readonly CompositeDisposable _disposables = new();
        private GameObject _instance;
        private ISceneLoadingView _view;
        private SceneLoadingPresenter _presenter;
        private bool _failed;

        public SceneLoadingScreen(ISceneService scene, SceneSettings settings, DiContainer container)
        {
            _scene = scene;
            _settings = settings;
            _container = container;
        }

        /// <summary>ロード画面のルート Canvas に設定する sortingOrder</summary>
        protected virtual int LoadingSortingOrder => _settings.SortingOrder + SortingOrderOffset;

        public void Initialize()
        {
            if (_settings.LoadingViewPrefab == null) return;
            _scene.ShowLoadingScreen.Subscribe(this, static (show, self) => self.SetVisible(show)).AddTo(_disposables);
        }

        private void SetVisible(bool visible)
        {
            // 外部から破棄されていた場合に備えて、インスタンスは Unity の null 判定で確認する (破棄されていたら作り直す)
            if (visible && _instance == null && !_failed && !TryCreate()) return;
            if (_instance != null) _view.SetVisible(visible);
        }

        private bool TryCreate()
        {
            _presenter?.Dispose();
            _presenter = null;

            var instance = Object.Instantiate(_settings.LoadingViewPrefab);
            Object.DontDestroyOnLoad(instance);

            if (!instance.TryGetComponent(out ISceneLoadingView view))
            {
                Debug.LogError($"[Abubu.Scene] Loading View Prefab のルートに {nameof(ISceneLoadingView)} を実装したコンポーネント (SceneLoadingView など) がありません。");
                Object.Destroy(instance);
                _failed = true;
                return false;
            }

            if (instance.TryGetComponent(out Canvas canvas))
            {
                canvas.sortingOrder = LoadingSortingOrder;
            }

            _container.InjectGameObject(instance);

            _instance = instance;
            _view = view;
            _presenter = _container.Instantiate<SceneLoadingPresenter>(new object[] { view });
            _presenter.Initialize();
            return true;
        }

        public virtual void Dispose()
        {
            _disposables.Dispose();
            _presenter?.Dispose();
            if (_instance != null) Object.Destroy(_instance);
        }
    }
}
