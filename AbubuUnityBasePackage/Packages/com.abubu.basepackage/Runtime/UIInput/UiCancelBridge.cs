using System;
using System.Collections.Generic;
using Abubu.Inputs;
using Abubu.UI;
using R3;
using UnityEngine.Scripting;
using Zenject;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.UI.UiInputModuleInstaller))]

namespace Abubu.UI
{
    /// <summary>
    /// Cancel 入力 (Esc / ゲームパッドの B など) で「戻る」(<see cref="UiNavigation.Back(IPopupService, IScreenService)"/>) を実行する。
    /// AbubuUiSettings.BackOnCancel がオンのときだけ有効。
    /// </summary>
    /// <remarks>
    /// 次の間は何もしない: <see cref="IUiBackBlocker"/> のどれかが止めている (文字入力中・Dropdown のリストが開いている・デバッグメニューの操作中など)、
    /// 同じフレームですでに「戻る」が処理された。同じキーでポーズメニューを開く場合は <see cref="UiNavigation.HandledBackThisFrame"/> を見て、
    /// 閉じたのと同じフレームで開き直さないようにする。
    /// </remarks>
    public sealed class UiCancelBridge : IInitializable, IDisposable
    {
        private readonly IInputService _input;
        private readonly IPopupService _popups;
        private readonly IScreenService _screens;
        private readonly List<IUiBackBlocker> _blockers;
        private readonly bool _enabled;
        private IDisposable _subscription;

        /// <param name="input">Input モジュールが無効なら null (何もしない)</param>
        public UiCancelBridge([InjectOptional] IInputService input = null, [InjectOptional] IPopupService popups = null,
            [InjectOptional] IScreenService screens = null, [InjectOptional] AbubuUiSettings settings = null,
            [InjectOptional] List<IUiBackBlocker> blockers = null)
        {
            _input = input;
            _popups = popups;
            _screens = screens;
            _blockers = blockers ?? new List<IUiBackBlocker>();
            _enabled = (settings != null ? settings : AbubuUiSettings.Default).BackOnCancel;
        }

        public void Initialize()
        {
            if (_input == null || !_enabled) return;
            _subscription = _input.Cancel.Subscribe(this, static (_, self) => self.OnCancel());
        }

        private void OnCancel()
        {
            if (UiNavigation.HandledBackThisFrame) return;
            foreach (var blocker in _blockers)
            {
                if (blocker.BlocksBack) return;
            }
            UiNavigation.Back(_popups, _screens);
        }

        public void Dispose() => _subscription?.Dispose();
    }

    /// <summary>UI と Input System の両方があるときに AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class UiInputModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public UiInputModuleInstaller() { }

        public void Install(DiContainer container)
        {
            // 有効かどうか (BackOnCancel / KeepFocusOnNavigate) は各ブリッジが設定を見て決める (インストール中に Resolve しない)
            UiModuleInstaller.BindSettings(container);
            container.BindInterfacesTo<UiCancelBridge>().AsSingle().NonLazy();
            container.BindInterfacesTo<UiFocusBridge>().AsSingle().NonLazy();
            container.Bind<IUiInputModeSource>().To<InputServiceUiInputMode>().AsSingle().IfNotBound();
            container.Bind<IUiSubmitHoldSource>().To<InputServiceSubmitHold>().AsSingle().IfNotBound();
            container.Bind<IUiEventSystemFactory>().To<InputSystemEventSystemFactory>().AsSingle().IfNotBound();
            container.Bind<IUiBackBlocker>().To<BuiltInUiBackBlocker>().AsSingle();
            // ゲームパッドが抜けたときの「コントローラーを接続してください」(表示中は戻るで閉じない)
            container.BindInterfacesTo<GamepadReconnectBridge>().AsSingle().NonLazy();
            // 一番手前の UI の操作の案内を、ボタン案内バー (Abubu.Inputs.UI) に渡す
            container.Bind<Abubu.Inputs.UI.IButtonPromptSource>().To<UiPromptButtonSource>().AsSingle().IfNotBound();
        }
    }
}
