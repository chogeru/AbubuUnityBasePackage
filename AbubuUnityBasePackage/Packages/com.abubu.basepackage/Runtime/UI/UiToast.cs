using UnityEngine;
using UnityEngine.UI;

namespace Abubu.UI
{
    /// <summary>
    /// トースト (数秒で消える短い通知)。<see cref="IToastService"/> (Popups.Toast) が使う。
    /// 自分のデザインのプレハブを使う場合は、このコンポーネントと CanvasGroup を付けて AbubuUiSettings > Toast Prefab に指定する。
    /// <code>
    /// Popups.Toast("保存しました");
    /// </code>
    /// </summary>
    [AddComponentMenu("Abubu/UI Toast")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UiToast : MonoBehaviour
    {
        [Tooltip("本文 (Text / TextMeshPro)")]
        [SerializeField] private Graphic message;

        public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

        public void Setup(string text) => UiText.Set(message, text);

        internal void Bind(Graphic messageText) => message = messageText;
    }
}
