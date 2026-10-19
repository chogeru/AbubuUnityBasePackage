using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Abubu.UI
{
    /// <summary>
    /// ゲームパッド・キーボードの選択を、このオブジェクトの中に閉じ込める (モーダルなダイアログ用)。
    /// 有効なスコープが複数あるときは、最後に有効になったものだけが働く。
    /// 選択が外に出たら (uGUI の自動ナビゲーションで裏の画面のボタンに移った、など)、直前の中の選択に戻す。
    /// ポップアップには自動で付く。自分のモーダルな UI に付けて使うこともできる。
    /// </summary>
    [AddComponentMenu("Abubu/UI Focus Scope")]
    [DisallowMultipleComponent]
    public sealed class UiFocusScope : MonoBehaviour
    {
        private static readonly List<UiFocusScope> Active = new();

        private GameObject _lastInside;

        /// <summary>今働いているスコープ (無ければ null)</summary>
        public static UiFocusScope Current => Active.Count > 0 ? Active[^1] : null;

        private void OnEnable()
        {
            Active.Remove(this);
            Active.Add(this);
        }

        private void OnDisable() => Active.Remove(this);

        // ナビゲーションの処理 (EventSystem.Update) のあとに見るので、外に出た選択はその場で戻され、次の決定入力で押されることはない
        private void LateUpdate()
        {
            if (Current != this) return;
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return;

            var selected = eventSystem.currentSelectedGameObject;
            if (selected == null) return;
            if (selected.transform.IsChildOf(transform))
            {
                _lastInside = selected;
                return;
            }

            var back = _lastInside != null && _lastInside.activeInHierarchy ? _lastInside : UiInitialSelection.FindFirstSelectable(gameObject);
            eventSystem.SetSelectedGameObject(back);
        }

        // Domain Reload 無効時に前回の再生のスコープが残らないようにする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active.Clear();
    }
}
