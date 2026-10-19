using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// 割り当て変更画面 (<see cref="RebindView"/>) の 1 行の uGUI 部品。プレハブにして RebindView の Row Prefab に設定する。
    /// 使わない部品は未設定のままで良い。
    /// <code>
    /// // 行プレハブ: [Action Label] [Binding Label / Glyph Image] [Rebind Button] [Reset Button] [Conflict Indicator]
    /// </code>
    /// </summary>
    /// <remarks>
    /// TextMeshPro を使う場合は、On Action Name Changed / On Binding Text Changed に TMP_Text.text を登録する。
    /// </remarks>
    public sealed class RebindRowView : MonoBehaviour
    {
        [SerializeField] private Text actionLabel;
        [SerializeField] private Text bindingLabel;
        [Tooltip("割り当てのボタンアイコン (任意)。アイコンが無い割り当てでは非表示になる")]
        [SerializeField] private Image glyphImage;
        [Tooltip("アイコンを表示しているときは Binding Label を隠す")]
        [SerializeField] private bool hideTextWhenGlyph = true;
        [Tooltip("押すと割り当て変更を始める (行全体をボタンにしてもよい)")]
        [SerializeField] private Button rebindButton;
        [SerializeField] private Button resetButton;
        [Tooltip("操作がかぶっているときに表示する (警告アイコンなど)")]
        [SerializeField] private GameObject conflictIndicator;
        [SerializeField] private Text conflictLabel;

        [Header("TextMeshPro などを使う場合")]
        [SerializeField] private UnityEvent<string> onActionNameChanged = new();
        [SerializeField] private UnityEvent<string> onBindingTextChanged = new();
        [SerializeField] private UnityEvent<string> onConflictTextChanged = new();

        public Button RebindButton => rebindButton;
        public Button ResetButton => resetButton;

        /// <summary>行の内容を表示する</summary>
        public void Apply(in RebindRowState state)
        {
            if (actionLabel != null) actionLabel.text = state.ActionName;
            onActionNameChanged.Invoke(state.ActionName);

            var showGlyph = state.Glyph != null && !state.IsListening && glyphImage != null;
            if (glyphImage != null)
            {
                glyphImage.sprite = showGlyph ? state.Glyph : null;
                glyphImage.enabled = showGlyph;
            }

            var bindingText = showGlyph && hideTextWhenGlyph ? string.Empty : state.BindingText;
            if (bindingLabel != null)
            {
                bindingLabel.text = bindingText;
                bindingLabel.enabled = !string.IsNullOrEmpty(bindingText);
            }
            onBindingTextChanged.Invoke(bindingText);

            if (rebindButton != null) rebindButton.interactable = state.CanRebind;
            if (resetButton != null) resetButton.interactable = state.CanRebind && state.IsOverridden;

            if (conflictIndicator != null) conflictIndicator.SetActive(state.HasConflict);
            if (conflictLabel != null) conflictLabel.text = state.ConflictText;
            onConflictTextChanged.Invoke(state.ConflictText);
        }
    }
}
