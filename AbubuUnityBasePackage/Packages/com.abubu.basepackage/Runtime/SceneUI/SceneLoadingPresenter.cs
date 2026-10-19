using System;
using Abubu.SceneFlow;
using R3;

namespace Abubu.Presentation
{
    /// <summary>ISceneService の進捗を <see cref="ISceneLoadingView"/> に表示する Presenter</summary>
    public class SceneLoadingPresenter : PresenterBase
    {
        protected ISceneService Scene { get; }
        protected ISceneLoadingView View { get; }

        public SceneLoadingPresenter(ISceneService scene, ISceneLoadingView view)
        {
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            View = view ?? throw new ArgumentNullException(nameof(view));
        }

        protected override void OnInitialize() =>
            Scene.Progress.Subscribe(View, static (p, view) => view.SetProgress(p)).AddTo(Disposables);
    }
}
