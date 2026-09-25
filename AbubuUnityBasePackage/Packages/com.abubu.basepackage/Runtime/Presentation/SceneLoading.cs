using System;
using Abubu.Scene;
using R3;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.Presentation
{
    /// <summary>
    /// ロード画面の View。プレハブ化して AbubuSettings の Loading View Prefab に設定すると、
    /// シーン遷移中に自動で表示される。生成と Presenter の結線は SceneLoadingScreen が行う。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class SceneLoadingView : MonoBehaviour
    {
        [SerializeField] private Slider progressBar;
        [Tooltip("Filled タイプの Image でも可")]
        [SerializeField] private Image progressFill;

        private CanvasGroup _group;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
        }

        public void SetProgress(float value)
        {
            if (progressBar != null) progressBar.SetValueWithoutNotify(value);
            if (progressFill != null) progressFill.fillAmount = value;
        }
    }

    public sealed class SceneLoadingPresenter : PresenterBase
    {
        private readonly ISceneService _scene;
        private readonly SceneLoadingView _view;

        public SceneLoadingPresenter(ISceneService scene, SceneLoadingView view)
        {
            _scene = scene;
            _view = view;
        }

        protected override void OnInitialize() =>
            _scene.Progress.Subscribe(_view, static (p, view) => view.SetProgress(p)).AddTo(Disposables);
    }

    /// <summary>
    /// ISceneService.ShowLoadingScreen を購読し、設定されたロード画面プレハブを表示する。
    /// SceneService 側は View を知らない (Scene → Presentation の依存を作らない)。
    /// </summary>
    public sealed class SceneLoadingScreen : IInitializable, IDisposable
    {
        private readonly ISceneService _scene;
        private readonly SceneSettings _settings;
        private readonly DiContainer _container;
        private readonly CompositeDisposable _disposables = new();
        private SceneLoadingView _view;
        private SceneLoadingPresenter _presenter;
        private bool _failed;

        public SceneLoadingScreen(ISceneService scene, SceneSettings settings, DiContainer container)
        {
            _scene = scene;
            _settings = settings;
            _container = container;
        }

        public void Initialize()
        {
            if (_settings.LoadingViewPrefab == null) return;
            _scene.ShowLoadingScreen.Subscribe(this, static (show, self) => self.SetVisible(show)).AddTo(_disposables);
        }

        private void SetVisible(bool visible)
        {
            if (visible && _view == null && !_failed)
            {
                var instance = Object.Instantiate(_settings.LoadingViewPrefab);
                Object.DontDestroyOnLoad(instance);
                if (!instance.TryGetComponent(out _view))
                {
                    Debug.LogError("[Abubu.Scene] Loading View Prefab に SceneLoadingView コンポーネントがありません。");
                    Object.Destroy(instance);
                    _failed = true;
                    return;
                }
                _presenter = _container.Instantiate<SceneLoadingPresenter>(new object[] { _view });
                _presenter.Initialize();
            }

            if (_view != null) _view.SetVisible(visible);
        }

        public void Dispose()
        {
            _disposables.Dispose();
            _presenter?.Dispose();
            if (_view != null) Object.Destroy(_view.gameObject);
        }
    }
}
