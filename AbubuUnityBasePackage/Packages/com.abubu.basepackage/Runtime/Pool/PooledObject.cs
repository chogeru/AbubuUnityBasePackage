using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abubu.Pool
{
    /// <summary>プール管理用に自動で付与されるコンポーネント</summary>
    /// <remarks>
    /// <see cref="PoolService"/> がインスタンス生成時に付ける。手動で付ける必要はない。
    /// 役割は次の 3 つ。
    /// ・自分を借りたプールを覚えておく
    /// ・ReturnAfter の時限返却タイマーを持つ
    /// ・Destroy されたことをプールに知らせる
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        /// <summary>このインスタンスを管理しているプール</summary>
        private PoolService _owner;

        /// <summary>ReturnAfter 用タイマーのキャンセル用。タイマーが動いていないときは null</summary>
        private CancellationTokenSource _timer;

        /// <summary>管理元のプールを登録する (PoolService からのみ呼ばれる)</summary>
        internal void Bind(PoolService owner) => _owner = owner;

        /// <summary>自分自身をプールに返す</summary>
        public void ReturnToPool() => _owner?.Return(gameObject);

        /// <summary>
        /// 指定秒数後に自動で返却するタイマーを開始する。動作中のタイマーがあれば止めてから開始し直す。
        /// </summary>
        internal void StartTimer(float seconds)
        {
            CancelTimer();
            _timer = new CancellationTokenSource();
            ReturnLaterAsync(seconds, _timer.Token).Forget();
        }

        /// <summary>時限返却タイマーを止める (手動返却時・破棄時)。</summary>
        internal void CancelTimer()
        {
            _timer?.Cancel();
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>待機してから返却する。途中でキャンセルされた場合は何もしない。</summary>
        private async UniTaskVoid ReturnLaterAsync(float seconds, CancellationToken ct)
        {
            if (await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct).SuppressCancellationThrow()) return;
            // 返却の前にタイマーを片付ける (Return 内の CancelTimer で、終わったタイマーを Cancel しないように)
            _timer?.Dispose();
            _timer = null;
            ReturnToPool();
        }

        /// <summary>
        /// ユーザーが直接 Destroy した場合や、シーン遷移で親ごと破棄された場合に呼ばれる。
        /// </summary>
        private void OnDestroy()
        {
            CancelTimer();
            _owner?.NotifyDestroyed(gameObject);
        }
    }
}
