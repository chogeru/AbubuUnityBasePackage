using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.UI
{
    /// <summary>ダイアログのボタンの文言。ローカライズが有効なら、翻訳テーブルの abubu.common.ok / abubu.common.cancel が使われる</summary>
    public interface IDialogLabels
    {
        string Ok { get; }
        string Cancel { get; }
    }

    /// <summary>AbubuUiSettings の Ok Label / Cancel Label を使う既定の文言</summary>
    public sealed class DialogLabels : IDialogLabels
    {
        private readonly AbubuUiSettings _settings;

        public DialogLabels(AbubuUiSettings settings) => _settings = settings;

        public string Ok => _settings.OkLabel;
        public string Cancel => _settings.CancelLabel;
    }

    /// <summary>
    /// 確認・お知らせのダイアログを 1 行で出す。中身はポップアップ (<see cref="IPopupService"/>) なので、
    /// 背面のブロック・Cancel 入力で閉じる・ポーズ連動などはポップアップの設定に従う。
    /// <code>
    /// if (await _dialogs.ConfirmAsync("セーブデータを削除しますか？")) DeleteSave();
    /// await _dialogs.AlertAsync("通信に失敗しました", title: "エラー");
    /// </code>
    /// 見た目は AbubuUiSettings > Confirm Dialog Prefab で差し替えられる (未設定なら組み込みの見た目)。
    /// </summary>
    public interface IDialogService
    {
        /// <summary>OK / Cancel のダイアログ。OK なら true、それ以外 (Cancel・戻る・キャンセル) なら false</summary>
        UniTask<bool> ConfirmAsync(string message, string title = null, string ok = null, string cancel = null, CancellationToken ct = default);

        /// <summary>OK だけのダイアログ。閉じられるまで待つ</summary>
        UniTask AlertAsync(string message, string title = null, string ok = null, CancellationToken ct = default);
    }

    public sealed class DialogService : IDialogService, IDisposable
    {
        private readonly IPopupService _popups;
        private readonly AbubuUiSettings _settings;
        private readonly IDialogLabels _labels;
        private readonly IUiFontProvider _fonts;
        private GameObject _templateHolder;
        private UiConfirmDialog _defaultTemplate;

        public DialogService(IPopupService popups, [InjectOptional] AbubuUiSettings settings = null, [InjectOptional] IDialogLabels labels = null,
            [InjectOptional] IUiFontProvider fonts = null)
        {
            _popups = popups;
            _settings = settings != null ? settings : AbubuUiSettings.Default;
            _labels = labels ?? new DialogLabels(_settings);
            _fonts = fonts ?? new SettingsUiFontProvider(_settings);
        }

        public UniTask<bool> ConfirmAsync(string message, string title = null, string ok = null, string cancel = null, CancellationToken ct = default) =>
            ShowAsync(message, title, ok ?? _labels.Ok, cancel ?? _labels.Cancel, ct);

        public async UniTask AlertAsync(string message, string title = null, string ok = null, CancellationToken ct = default) =>
            await ShowAsync(message, title, ok ?? _labels.Ok, null, ct);

        private async UniTask<bool> ShowAsync(string message, string title, string ok, string cancel, CancellationToken ct)
        {
            var usesDefault = _settings.ConfirmDialogPrefab == null;
            var dialog = _popups.Push(GetTemplate());
            if (dialog == null) return false;
            if (usesDefault) UiUtility.ApplyFont(dialog.gameObject, _fonts);
            dialog.Setup(message, title, ok, cancel, _popups.Close, _settings.ConfirmDefaultButton);

            try
            {
                return await dialog.Result.AttachExternalCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                if (dialog != null) _popups.Close(dialog.gameObject);
                return false;
            }
        }

        private UiConfirmDialog GetTemplate()
        {
            if (_settings.ConfirmDialogPrefab != null) return _settings.ConfirmDialogPrefab;
            if (_defaultTemplate != null) return _defaultTemplate;

            // 組み込みの見た目を一度だけ組み立てて、複製元として持っておく (非アクティブな親の下なので表示されない)
            _templateHolder = new GameObject("[Abubu.UI.Templates]");
            _templateHolder.SetActive(false);
            Object.DontDestroyOnLoad(_templateHolder);
            return _defaultTemplate = UiDefaultWidgets.CreateConfirmDialog(_templateHolder.transform, _settings.WidgetStyle);
        }

        public void Dispose()
        {
            if (_templateHolder != null) Object.Destroy(_templateHolder);
        }
    }
}
