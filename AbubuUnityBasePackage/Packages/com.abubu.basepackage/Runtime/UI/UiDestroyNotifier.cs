using System;
using UnityEngine;

namespace Abubu.UI
{
    /// <summary>
    /// ポップアップ・画面が外から Destroy されたことをサービスに知らせる (サービスが自動で付ける)。
    /// </summary>
    /// <remarks>Unity の仕様で、一度もアクティブにならなかったオブジェクトでは OnDestroy が呼ばれない。</remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class UiDestroyNotifier : MonoBehaviour
    {
        private Action<GameObject> _onDestroyed;

        internal static void Attach(GameObject target, Action<GameObject> onDestroyed)
        {
            var notifier = target.GetComponent<UiDestroyNotifier>();
            if (notifier == null) notifier = target.AddComponent<UiDestroyNotifier>();
            notifier._onDestroyed = onDestroyed;
        }

        private void OnDestroy() => _onDestroyed?.Invoke(gameObject);
    }
}
