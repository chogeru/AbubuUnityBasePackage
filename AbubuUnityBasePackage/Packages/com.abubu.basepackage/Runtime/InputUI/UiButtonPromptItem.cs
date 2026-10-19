using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// <see cref="UiButtonPromptBar"/> の 1 項目 (ボタンアイコン + 文言)。プレハブにしてバーの Item Prefab に設定する。
    /// <code>
    /// // 項目プレハブ: [InputGlyphImage (Image)] [Label (Text)]   TextMeshPro なら On Label Changed に TMP_Text.text を登録
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiButtonPromptItem : MonoBehaviour
    {
        [Tooltip("ボタンアイコン (ActionPath はバーが設定する)")]
        [SerializeField] private InputGlyphImage glyph;
        [SerializeField] private Text label;

        [Header("TextMeshPro などを使う場合")]
        [SerializeField] private UnityEvent<string> onLabelChanged = new();

        /// <summary>ボタンアイコン</summary>
        public InputGlyphImage Glyph
        {
            get => glyph;
            set => glyph = value;
        }

        /// <summary>文言を出す Text</summary>
        public Text Label
        {
            get => label;
            set => label = value;
        }

        /// <summary>表示中の案内</summary>
        public ButtonPrompt Prompt { get; private set; }

        /// <summary>割り当てが無い (アイコンも文字も出せない) か</summary>
        public bool IsUnbound => glyph != null && glyph.Current.IsUnbound;

        /// <summary>
        /// 一時的に隠す / 表示する。GameObject は有効のまま (アイコンの更新は続く) で、見た目とレイアウト上の幅だけを消す
        /// </summary>
        public void SetHidden(bool hidden)
        {
            var group = GetComponent<CanvasGroup>();
            if (group == null)
            {
                if (!hidden) return;
                group = gameObject.AddComponent<CanvasGroup>();
            }
            group.alpha = hidden ? 0f : 1f;
            group.blocksRaycasts = !hidden;

            var layout = GetComponent<LayoutElement>();
            if (layout == null)
            {
                if (!hidden) return;
                layout = gameObject.AddComponent<LayoutElement>();
            }
            layout.ignoreLayout = hidden;
        }

        /// <summary>案内を表示する</summary>
        public void Set(in ButtonPrompt prompt)
        {
            Prompt = prompt;
            var text = prompt.Label ?? string.Empty;
            if (label != null)
            {
                label.text = text;
                label.enabled = text.Length > 0;
            }
            onLabelChanged.Invoke(text);
            if (glyph != null) glyph.ActionPath = prompt.ActionPath;
        }
    }
}
