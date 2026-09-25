using System;
using R3;
using Zenject;

namespace Abubu.Presentation
{
    /// <summary>
    /// MVP の Presenter 基底。Model と View をコンストラクタで受け取り、OnInitialize で購読を組む。
    /// 購読は Disposables に AddTo しておけば Dispose 時にまとめて解除される。
    /// <code>
    /// public sealed class HpPresenter : PresenterBase
    /// {
    ///     private readonly PlayerModel _model; private readonly HpView _view;
    ///     public HpPresenter(PlayerModel model, HpView view) { _model = model; _view = view; }
    ///     protected override void OnInitialize() =>
    ///         _model.Hp.Subscribe(_view.SetHp).AddTo(Disposables);
    /// }
    /// </code>
    /// </summary>
    public abstract class PresenterBase : IInitializable, IDisposable
    {
        protected CompositeDisposable Disposables { get; } = new();

        private bool _initialized;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            OnInitialize();
        }

        protected abstract void OnInitialize();

        public virtual void Dispose() => Disposables.Dispose();
    }
}
