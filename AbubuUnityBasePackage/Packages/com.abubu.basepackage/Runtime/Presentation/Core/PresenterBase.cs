using System;
using R3;
using Zenject;

namespace Abubu.Presentation
{
    /// <summary>
    /// MVP の Presenter 基底。Model と View (のインターフェース) をコンストラクタで受け取り、OnInitialize で購読を組む。
    /// 購読は Disposables に AddTo しておけば Dispose 時にまとめて解除される。
    /// <code>
    /// public interface IHpView { void SetHp(int hp); }
    /// public class HpPresenter : PresenterBase
    /// {
    ///     private readonly PlayerModel _model; private readonly IHpView _view;
    ///     public HpPresenter(PlayerModel model, IHpView view) { _model = model; _view = view; }
    ///     protected override void OnInitialize() =>
    ///         _model.Hp.Subscribe(_view.SetHp).AddTo(Disposables);
    /// }
    /// </code>
    /// </summary>
    /// <remarks>
    /// View は具象クラスではなくインターフェースで受け取ること。uGUI / TextMeshPro / UI Toolkit などの実装を
    /// Presenter を変えずに差し替えられ、テストでは偽の View を渡せる。
    /// </remarks>
    public abstract class PresenterBase : IInitializable, IDisposable
    {
        protected CompositeDisposable Disposables { get; } = new();

        private bool _initialized;
        private bool _disposed;

        /// <summary>初期化済みか</summary>
        public bool IsInitialized => _initialized;

        /// <summary>購読を組む。2 回目以降の呼び出しは無視される</summary>
        public void Initialize()
        {
            if (_initialized || _disposed) return;
            _initialized = true;
            OnInitialize();
        }

        protected abstract void OnInitialize();

        /// <summary>購読を解除する。オーバーライドする場合は base.Dispose() を呼ぶこと</summary>
        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disposables.Dispose();
        }
    }
}
