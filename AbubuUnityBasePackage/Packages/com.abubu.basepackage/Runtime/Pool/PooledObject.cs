using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abubu.Pool
{
    /// <summary>プール管理用に自動で付与されるコンポーネント</summary>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        private PoolService _owner;
        private CancellationTokenSource _timer;

        internal void Bind(PoolService owner) => _owner = owner;

        /// <summary>自分自身をプールに返す</summary>
        public void ReturnToPool() => _owner?.Return(gameObject);

        internal void StartTimer(float seconds)
        {
            CancelTimer();
            _timer = new CancellationTokenSource();
            ReturnLaterAsync(seconds, _timer.Token).Forget();
        }

        internal void CancelTimer()
        {
            _timer?.Cancel();
            _timer?.Dispose();
            _timer = null;
        }

        private async UniTaskVoid ReturnLaterAsync(float seconds, CancellationToken ct)
        {
            if (await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct).SuppressCancellationThrow()) return;
            _timer?.Dispose();
            _timer = null;
            ReturnToPool();
        }

        private void OnDestroy()
        {
            CancelTimer();
            _owner?.NotifyDestroyed(gameObject);
        }
    }
}
