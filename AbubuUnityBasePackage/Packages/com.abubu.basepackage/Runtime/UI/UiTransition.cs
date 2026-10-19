using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abubu.UI
{
    /// <summary>
    /// ポップアップ・画面に付けると、開くとき・閉じるときにアニメーションを再生できる (任意)。
    /// 閉じるときは、ここで返した UniTask が終わってから破棄 (または非表示) される。
    /// <code>
    /// public sealed class FadePopup : MonoBehaviour, IUiTransition
    /// {
    ///     [SerializeField] private CanvasGroup group;
    ///     public UniTask PlayShowAsync(CancellationToken ct) => LMotion.Create(0f, 1f, 0.2f).BindToAlpha(group).ToUniTask(ct);
    ///     public UniTask PlayHideAsync(CancellationToken ct) => LMotion.Create(1f, 0f, 0.2f).BindToAlpha(group).ToUniTask(ct);
    /// }
    /// </code>
    /// </summary>
    /// <remarks>
    /// 閉じるアニメーションの途中で同じ画面がまた表示された場合 (戻る操作など) は、ct がキャンセルされる。
    /// ポーズ中 (timeScale 0) にも動かしたい場合は、unscaled time で再生すること。
    /// </remarks>
    public interface IUiTransition
    {
        UniTask PlayShowAsync(CancellationToken ct);
        UniTask PlayHideAsync(CancellationToken ct);
    }

    /// <summary><see cref="IUiTransition"/> の再生と、閉じるアニメーションの途中で再表示されたときのキャンセルを受け持つ</summary>
    internal sealed class UiTransitionRunner
    {
        private readonly Dictionary<GameObject, CancellationTokenSource> _running = new();

        /// <summary>開くアニメーションを再生する (終わりを待たない)。閉じるアニメーション中ならそれを取り消す</summary>
        public void Show(GameObject target)
        {
            if (target == null) return;
            Cancel(target);

            var transitions = target.GetComponents<IUiTransition>();
            if (transitions.Length == 0) return;
            var cts = Begin(target);
            foreach (var transition in transitions) Run(transition.PlayShowAsync(cts.Token), target).Forget();
        }

        /// <summary>閉じるアニメーションを再生し、終わったら onCompleted を呼ぶ。アニメーションが無ければすぐに呼ぶ</summary>
        public void Hide(GameObject target, Action onCompleted)
        {
            if (target == null)
            {
                onCompleted();
                return;
            }

            Cancel(target);
            var transitions = target.GetComponents<IUiTransition>();
            if (transitions.Length == 0 || !target.activeInHierarchy)
            {
                onCompleted();
                return;
            }

            HideAsync(target, transitions, Begin(target), onCompleted).Forget();
        }

        public void CancelAll()
        {
            foreach (var cts in _running.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _running.Clear();
        }

        private async UniTaskVoid HideAsync(GameObject target, IUiTransition[] transitions, CancellationTokenSource cts, Action onCompleted)
        {
            var tasks = new UniTask[transitions.Length];
            for (var i = 0; i < transitions.Length; i++) tasks[i] = Run(transitions[i].PlayHideAsync(cts.Token), target);
            await UniTask.WhenAll(tasks);

            // 再表示されて取り消された場合は、閉じる処理を行わない (破棄されて止まった場合は後片付けのために続ける)
            if (cts.IsCancellationRequested && target != null) return;
            End(target, cts);
            onCompleted();
        }

        /// <summary>アニメーションの例外で閉じる処理が止まらないよう、ログに出して握りつぶす</summary>
        private static async UniTask Run(UniTask task, GameObject target)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e, target);
            }
        }

        private CancellationTokenSource Begin(GameObject target)
        {
            // 破棄されたら止める
            var cts = CancellationTokenSource.CreateLinkedTokenSource(target.GetCancellationTokenOnDestroy());
            _running[target] = cts;
            return cts;
        }

        private void End(GameObject target, CancellationTokenSource cts)
        {
            if (_running.TryGetValue(target, out var current) && current == cts) _running.Remove(target);
            cts.Dispose();
        }

        private void Cancel(GameObject target)
        {
            if (!_running.Remove(target, out var cts)) return;
            cts.Cancel();
            cts.Dispose();
        }
    }
}
