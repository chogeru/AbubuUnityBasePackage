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
        private IDisposable _subscription;

        /// <param name="input">Input モジュールが無効なら null (何もしない)</param>
        public UiCancelBridge([InjectOptional] IInputService input = null, [InjectOptional] IPopupService popups = null,
            [InjectOptional] IScreenService screens = null)
        {
            _input = input;
            _popups = popups;
            _screens = screens;
        }

        public void Initialize()
        {
            if (_input == null) return;
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
            var settings = UiModuleInstaller.ResolveSettings(container);
            if (settings != null && settings.BackOnCancel) container.BindInterfacesTo<UiCancelBridge>().AsSingle().NonLazy();
            // 設定アセットが無いときは既定値 (KeepFocusOnNavigate = オン) に従う
            if (settings == null || settings.KeepFocusOnNavigate) container.BindInterfacesTo<UiFocusBridge>().AsSingle().NonLazy();
        }
    }
}
