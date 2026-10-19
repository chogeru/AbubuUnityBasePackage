using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Abubu.UI
{
    /// <summary>
    /// ポップアップ・画面のルートに付けると、表示されたときに指定したボタンなどを選択状態にする (ゲームパッド・キーボード操作用)。
    /// ポップアップを閉じたときは、開く前に選択していたものに選択が戻る。
    /// <code>
    /// // ルートに付けて Target に最初のボタンを指定するだけ。コードからは:
    /// GetComponent&lt;UiInitialSelection&gt;().Target = okButton;
    /// </code>
    /// </summary>
    [AddComponentMenu("Abubu/UI Initial Selection")]
    [DisallowMultipleComponent]
    public sealed class UiInitialSelection : MonoBehaviour
    {
        [Tooltip("最初に選択するもの。未設定なら子の中で最初に見つかった操作可能な Selectable")]
        [SerializeField] private Selectable target;

        public Selectable Target
        {
            get => target;
            set => target = value;
        }

        /// <summary>選択するもの (Target が未設定・操作不可なら、子の中で最初の操作可能な Selectable)</summary>
        public GameObject Resolve() => target != null && IsUsable(target) ? target.gameObject : FindFirstSelectable(gameObject);

        /// <summary>root に付いていれば、その選択先を選択する (force なら付いていなくても最初の Selectable を選ぶ)。EventSystem が無ければ何もしない</summary>
        internal static void Apply(GameObject root, bool force = false)
        {
            if (root == null || EventSystem.current == null) return;
            var selection = root.GetComponent<UiInitialSelection>();
            if (selection == null && !force) return;

            var selected = selection != null ? selection.Resolve() : FindFirstSelectable(root);
            if (selected != null) EventSystem.current.SetSelectedGameObject(selected);
        }

        /// <summary>
        /// ポップアップ・画面を開いたときの選択 (AbubuUiSettings.SelectOnOpen)。
        /// Never: UI Initial Selection が付いているときだけ選ぶ / Auto: ゲームパッド操作中なら選び、マウス操作中は UI Initial Selection が付いているときだけ選んで裏の UI の選択を外す / Always: 常に選ぶ
        /// </summary>
        internal static void ApplyOnOpen(GameObject root, UiSelectOnOpen mode, IUiInputModeSource input)
        {
            if (root == null || EventSystem.current == null) return;
            switch (mode)
            {
                case UiSelectOnOpen.Never:
                    Apply(root);
                    break;
                case UiSelectOnOpen.Always:
                    Apply(root, force: true);
                    break;
                default:
                    if (input != null && input.IsNavigationMode)
                    {
                        Apply(root, force: true);
                    }
                    else
                    {
                        // マウス操作中: 明示的な初期選択 (UiInitialSelection) だけは Never と同じく適用する
                        Apply(root);
                        // 裏の UI が選ばれたままだと、Enter / 決定ボタンで裏のボタンが押されてしまう
                        var selected = Current;
                        if (selected != null && !selected.transform.IsChildOf(root.transform)) EventSystem.current.SetSelectedGameObject(null);
                    }
                    break;
            }
        }

        /// <summary>選択を戻す (戻し先が破棄・非表示になっていれば何もしない)</summary>
        internal static void Restore(GameObject previous)
        {
            if (previous == null || !previous.activeInHierarchy || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(previous);
        }

        /// <summary>現在選択されているもの</summary>
        internal static GameObject Current => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

        /// <summary>root の子の中で最初の操作可能な Selectable</summary>
        public static GameObject FindFirstSelectable(GameObject root)
        {
            if (root == null) return null;
            foreach (var selectable in root.GetComponentsInChildren<Selectable>())
            {
                if (IsUsable(selectable)) return selectable.gameObject;
            }
            return null;
        }

        private static bool IsUsable(Selectable selectable) =>
            selectable.isActiveAndEnabled && selectable.IsInteractable() && selectable.navigation.mode != Navigation.Mode.None;
    }
}
