using System;
using System.Threading;
using Abubu.Inputs;
using Cysharp.Threading.Tasks;
using R3;
using Zenject;

namespace Abubu.UI
{
    /// <summary>
    /// ゲームパッド・キーボードで方向入力・決定したとき、または操作がゲームパッドに切り替わったときに、
    /// 選択が無い・一番手前の UI の外にあれば選び直す (<see cref="UiNavigation.EnsureSelection(IPopupService, IScreenService)"/>)。
    /// マウスで背景をクリックして選択が外れた後でも、スティックを倒せばすぐ操作に戻れるようにする。
    /// AbubuUiSettings.KeepFocusOnNavigate がオンのときだけ有効。
    /// </summary>
    /// <remarks>
    /// 選び直しはフレームの最後 (EventSystem の処理のあと) に行う。入力を受けたその場で選ぶと、同じ入力で EventSystem が
    /// 選択をもう 1 つ先へ動かしてしまい、2 つ目の項目が選ばれるため。
    /// </remarks>
    public sealed class UiFocusBridge : IInitializable, IDisposable
    {
        private readonly IInputService _input;
        private readonly IPopupService _popups;
        private readonly IScreenService _screens;
        private readonly AbubuUiSettings _settings;
        private readonly CompositeDisposable _subscriptions = new();
        private readonly CancellationTokenSource _cts = new();
        private bool _scheduled;

        /// <param name="input">Input モジュールが無効なら null (何もしない)</param>
        public UiFocusBridge([InjectOptional] IInputService input = null, [InjectOptional] IPopupService popups = null,
            [InjectOptional] IScreenService screens = null, [InjectOptional] AbubuUiSettings settings = null)
        {
            _input = input;
            _popups = popups;
            _screens = screens;
            _settings = settings != null ? settings : AbubuUiSettings.Default;
        }

        public void Initialize()
        {
            if (_input == null || !_settings.KeepFocusOnNavigate) return;

            foreach (var path in new[] { _settings.NavigateActionPath, _settings.SubmitActionPath })
            {
                if (string.IsNullOrEmpty(path)) continue;
                _input.OnPerformed(path).Subscribe(this, static (_, self) => self.Schedule()).AddTo(_subscriptions);
            }

            // マウスからゲームパッドに持ち替えた瞬間にも、選択を用意しておく
            _input.CurrentDevice
                .Where(static device => device is InputDeviceKind.Gamepad or InputDeviceKind.Joystick)
                .Subscribe(this, static (_, self) => self.Schedule())
                .AddTo(_subscriptions);
        }

        private void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EnsureAtEndOfFrameAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid EnsureAtEndOfFrameAsync(CancellationToken ct)
        {
            try
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                // キー割り当ての入力待ち中は、押されたボタンを割り当てに使うので選択を動かさない
                if (_input.IsRebinding.CurrentValue) return;
                UiNavigation.EnsureSelection(_popups, _screens);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _scheduled = false;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _subscriptions.Dispose();
        }
    }
}
