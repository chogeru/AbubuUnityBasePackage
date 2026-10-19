using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Abubu.UI
{
    /// <summary>
    /// ポップアップ・画面のルートに付けると、表示されたときに指定したボタンなどを選択状態にする (ゲームパッド・キーボード操作用)。
    /// ポップアップを閉じたときは、開く前に選択していたものに選択が戻る。
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
        public GameObject Resolve()
        {
            if (target != null && target.IsActive() && target.IsInteractable()) return target.gameObject;
            foreach (var selectable in GetComponentsInChildren<Selectable>())
            {
                if (selectable.IsActive() && selectable.IsInteractable()) return selectable.gameObject;
            }
            return null;
        }

        /// <summary>root に付いていれば、その選択先を選択する。EventSystem が無ければ何もしない</summary>
        internal static void Apply(GameObject root)
        {
            if (root == null || EventSystem.current == null) return;
            var selection = root.GetComponent<UiInitialSelection>();
            if (selection == null) return;

            var selected = selection.Resolve();
            if (selected != null) EventSystem.current.SetSelectedGameObject(selected);
        }

        /// <summary>選択を戻す (戻し先が破棄・非表示になっていれば何もしない)</summary>
        internal static void Restore(GameObject previous)
        {
            if (previous == null || !previous.activeInHierarchy || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(previous);
        }

        /// <summary>現在選択されているもの</summary>
        internal static GameObject Current => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
    }
}
