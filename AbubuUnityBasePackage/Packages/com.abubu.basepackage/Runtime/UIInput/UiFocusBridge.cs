using System;
using Abubu.Input;
using R3;
using Zenject;

namespace Abubu.UI
{
    /// <summary>
    /// ゲームパッド・キーボードで方向入力したとき、または操作がゲームパッドに切り替わったときに、
    /// 選択が無い・一番手前の UI の外にあれば選び直す (<see cref="UiNavigation.EnsureSelection(IPopupService, IScreenService)"/>)。
    /// マウスで背景をクリックして選択が外れた後でも、スティックを倒せばすぐ操作に戻れるようにする。
    /// AbubuUiSettings.KeepFocusOnNavigate がオンのときだけ有効。
    /// </summary>
    public sealed class UiFocusBridge : IInitializable, IDisposable
    {
        private readonly IInputService _input;
        private readonly IPopupService _popups;
        private readonly IScreenService _screens;
        private readonly string _navigateActionPath;
        private readonly CompositeDisposable _subscriptions = new();

        /// <param name="input">Input モジュールが無効なら null (何もしない)</param>
        public UiFocusBridge([InjectOptional] IInputService input = null, [InjectOptional] IPopupService popups = null,
            [InjectOptional] IScreenService screens = null, [InjectOptional] AbubuUiSettings settings = null)
        {
            _input = input;
            _popups = popups;
            _screens = screens;
            _navigateActionPath = settings != null ? settings.NavigateActionPath : "UI/Navigate";
        }

        public void Initialize()
        {
            if (_input == null) return;

            if (!string.IsNullOrEmpty(_navigateActionPath))
            {
                _input.OnPerformed(_navigateActionPath).Subscribe(this, static (_, self) => self.Ensure()).AddTo(_subscriptions);
            }

            // マウスからゲームパッドに持ち替えた瞬間にも、選択を用意しておく
            _input.CurrentDevice
                .Where(static device => device is InputDeviceKind.Gamepad or InputDeviceKind.Joystick)
                .Subscribe(this, static (_, self) => self.Ensure())
                .AddTo(_subscriptions);
        }

        private void Ensure() => UiNavigation.EnsureSelection(_popups, _screens);

        public void Dispose() => _subscriptions.Dispose();
    }
}
