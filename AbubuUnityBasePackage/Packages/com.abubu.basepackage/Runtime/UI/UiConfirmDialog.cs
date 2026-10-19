using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.UI
{
    /// <summary>
    /// 確認ダイアログ。<see cref="IDialogService.ConfirmAsync"/> (Popups.Confirm) が使う。
    /// 自分のデザインのプレハブを使う場合は、このコンポーネントを付けて各参照を設定し、AbubuUiSettings > Confirm Dialog Prefab に指定する。
    /// <code>
    /// if (await Popups.Confirm("セーブデータを削除しますか？")) DeleteSave();
    /// </code>
    /// </summary>
    /// <remarks>Cancel ボタン・Cancel 入力 (戻る)・背面クリックなど、OK 以外で閉じられた場合は false になる。</remarks>
    [AddComponentMenu("Abubu/UI Confirm Dialog")]
    [DisallowMultipleComponent]
    public sealed class UiConfirmDialog : MonoBehaviour, IPopup
    {
        [Tooltip("タイトル (Text / TextMeshPro)。空のときは非表示になる")]
        [SerializeField] private Graphic title;

        [Tooltip("本文 (Text / TextMeshPro)")]
        [SerializeField] private Graphic message;

        [SerializeField] private Button okButton;
        [SerializeField] private Graphic okLabel;

        [Tooltip("Alert (OK だけ) のときは非表示になる")]
        [SerializeField] private Button cancelButton;
        [SerializeField] private Graphic cancelLabel;

        private readonly UniTaskCompletionSource<bool> _result = new();
        private Func<GameObject, bool> _close;

        /// <summary>OK で閉じたら true、それ以外で閉じたら false</summary>
        public UniTask<bool> Result => _result.Task;

        /// <summary>表示内容を設定する。cancel が null なら Cancel ボタンを隠す (Alert)</summary>
        /// <param name="close">ボタンで閉じるときの処理 (開いた IPopupService の Close)。null なら Popups.Close</param>
        public void Setup(string messageText, string titleText, string ok, string cancel, Func<GameObject, bool> close = null)
        {
            _close = close;
            UiText.SetOrHide(title, titleText);
            UiText.Set(message, messageText);
            UiText.Set(okLabel, ok);
            UiText.Set(cancelLabel, cancel);
            if (cancelButton != null) cancelButton.gameObject.SetActive(cancel != null);
        }

        /// <summary>コードで組み立てるときに参照を設定する (<see cref="UiDefaultWidgets"/> が使う)</summary>
        internal void Bind(Graphic titleText, Graphic messageText, Button ok, Graphic okText, Button cancel, Graphic cancelText)
        {
            title = titleText;
            message = messageText;
            okButton = ok;
            okLabel = okText;
            cancelButton = cancel;
            cancelLabel = cancelText;
        }

        private void Awake()
        {
            if (okButton != null) okButton.onClick.AddListener(() => Close(true));
            if (cancelButton != null) cancelButton.onClick.AddListener(() => Close(false));
        }

        private void Close(bool result)
        {
            _result.TrySetResult(result);
            // 開いたポップアップのスタックから外して閉じる (見つからない場合は自分で消える)
            var closed = _close != null ? _close(gameObject) : Popups.Close(gameObject);
            if (!closed) Destroy(gameObject);
        }

        public void OnOpened()
        {
        }

        public void OnClosing() => _result.TrySetResult(false);

        private void OnDestroy() => _result.TrySetResult(false);
    }
}
