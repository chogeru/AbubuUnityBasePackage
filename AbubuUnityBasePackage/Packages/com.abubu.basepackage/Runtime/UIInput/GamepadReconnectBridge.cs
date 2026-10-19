using System;
using Abubu.Inputs;
using Abubu.Localization;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.UI
{
    /// <summary>
    /// ゲームパッドが抜けてポーズしたとき (GamepadDisconnectPauser) に「コントローラーを接続してください」のダイアログを出し、
    /// 接続し直されたら閉じる。「キーボードで続ける」を押すと待つのをやめる (Dismiss)。
    /// 表示中は Cancel (B / Esc) では閉じない。ポーズ中も表示される。
    /// </summary>
    /// <remarks>文言は翻訳テーブルの abubu.input.reconnect.title / message / keyboard (無ければ英語)。</remarks>
    public sealed class GamepadReconnectBridge : IInitializable, IDisposable, IUiBackBlocker
    {
        public const string TitleKey = "abubu.input.reconnect.title";
        public const string MessageKey = "abubu.input.reconnect.message";
        public const string KeyboardKey = "abubu.input.reconnect.keyboard";

        private readonly GamepadDisconnectPauser _pauser;
        private readonly IDialogService _dialogs;
        private readonly IPopupService _popups;
        private readonly ILocalizationService _localization;
        private IDisposable _subscription;
        private GameObject _dialog;
        private bool _closingBySystem;

        public GamepadReconnectBridge([InjectOptional] GamepadDisconnectPauser pauser = null, [InjectOptional] IDialogService dialogs = null,
            [InjectOptional] IPopupService popups = null, [InjectOptional] ILocalizationService localization = null)
        {
            _pauser = pauser;
            _dialogs = dialogs;
            _popups = popups;
            _localization = localization;
        }

        /// <summary>ダイアログを出している間は「戻る」で閉じない</summary>
        public bool BlocksBack => _dialog != null;

        public void Initialize()
        {
            if (_pauser == null || _dialogs == null || _popups == null) return;
            _subscription = _pauser.IsWaitingForReconnect.Subscribe(this, static (waiting, self) =>
            {
                if (waiting) self.Show();
                else self.Hide();
            });
        }

        private void Show()
        {
            if (_dialog != null) return;
            ShowAsync().Forget();
        }

        private async UniTaskVoid ShowAsync()
        {
            var task = _dialogs.AlertAsync(Text(MessageKey, "Please connect a controller."), Text(TitleKey, "Controller disconnected"),
                Text(KeyboardKey, "Continue with keyboard"));
            _dialog = _popups.Current;
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            _dialog = null;
            var bySystem = _closingBySystem;
            _closingBySystem = false;
            // 接続し直されて閉じた場合以外 (「キーボードで続ける」) は、待つのをやめる
            if (!bySystem && _pauser.IsWaitingForReconnect.CurrentValue) _pauser.Dismiss();
        }

        private void Hide()
        {
            if (_dialog == null) return;
            _closingBySystem = true;
            if (!_popups.Close(_dialog)) _closingBySystem = false;
        }

        private string Text(string key, string fallback) =>
            _localization != null && _localization.TryGet(key, out _) ? _localization.Get(key) : fallback;

        public void Dispose() => _subscription?.Dispose();
    }
}
