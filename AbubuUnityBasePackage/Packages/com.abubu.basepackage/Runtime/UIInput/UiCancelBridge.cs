using System;
using Abubu.Input;
using Abubu.UI;
using R3;
using UnityEngine;
using UnityEngine.Scripting;
using Zenject;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.UI.UiInputModuleInstaller))]

namespace Abubu.UI
{
    /// <summary>
    /// Cancel 入力 (Esc / ゲームパッドの B など) で「戻る」(<see cref="UiNavigation.Back(IPopupService, IScreenService)"/>) を実行する。
    /// AbubuUiSettings.BackOnCancel がオンのときだけ有効。
    /// </summary>
    public sealed class UiCancelBridge : IInitializable, IDisposable
    {
        private readonly IInputService _input;
        private readonly IPopupService _popups;
        private readonly IScreenService _screens;
        private readonly bool _enabled;
        private IDisposable _subscription;

        /// <param name="input">Input モジュールが無効なら null (何もしない)</param>
        public UiCancelBridge([InjectOptional] IInputService input = null, [InjectOptional] IPopupService popups = null,
            [InjectOptional] IScreenService screens = null, [InjectOptional] AbubuUiSettings settings = null)
        {
            _input = input;
            _popups = popups;
            _screens = screens;
            // 設定アセットが無いときは既定値 (オフ)
            _enabled = settings != null && settings.BackOnCancel;
        }

        public void Initialize()
        {
            if (_input == null || !_enabled) return;
            _subscription = _input.Cancel.Subscribe(this, static (_, self) => UiNavigation.Back(self._popups, self._screens));
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
            // 有効かどうか (BackOnCancel / KeepFocusOnNavigate) は各ブリッジが Initialize で設定を見て決める (インストール中に Resolve しない)
            UiModuleInstaller.BindSettings(container);
            container.BindInterfacesTo<UiCancelBridge>().AsSingle().NonLazy();
            container.BindInterfacesTo<UiFocusBridge>().AsSingle().NonLazy();
        }
    }
}
