using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using uPools;
using UnityEngine;

namespace Abubu.Pool
{
    /// <summary>プール管理用に自動で付与されるコンポーネント</summary>
    /// <remarks>
    /// <see cref="PoolService"/> がインスタンス生成時に付ける (プレハブに最初から付けておいても良い)。
    /// 役割は次の 3 つ。
    /// ・自分を管理しているプールと、貸し出し中かどうかを覚えておく
    /// ・ReturnAfter の時限返却タイマーを持つ
    /// ・Destroy されたことをプールに知らせる
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        /// <summary>このインスタンスを管理しているプール (管理外になったら null)</summary>
        private PrefabPool _pool;

        /// <summary>ReturnAfter 用タイマーのキャンセル用。タイマーが動いていないときは null</summary>
        private CancellationTokenSource _timer;

        /// <summary>
        /// 貸し出し / 返却を通知する相手 (子を含む)。Rent / Return のたびに探すと配列の確保が発生するため、生成時に 1 回だけ集める。
        /// 生成後に追加したコンポーネントには通知されない
        /// </summary>
        private IPoolCallbackReceiver[] _receivers = Array.Empty<IPoolCallbackReceiver>();

        /// <summary>現在貸し出し中か</summary>
        public bool IsRented { get; private set; }

        internal PrefabPool Pool => _pool;

        /// <summary>自分自身をプールに返す。貸し出し中でなければ何もしない</summary>
        public void ReturnToPool() => _pool?.Return(this);

        internal void Bind(PrefabPool pool)
        {
            _pool = pool;
            _receivers = GetComponentsInChildren<IPoolCallbackReceiver>(true);
        }

        /// <summary>プールの破棄時に管理から外す (この後の OnDestroy でプールへ通知しないため)</summary>
        internal void Unbind()
        {
            CancelTimer();
            _pool = null;
            IsRented = false;
        }

        internal void MarkRented() => IsRented = true;
        internal void MarkReturned() => IsRented = false;

        internal void NotifyRent()
        {
            foreach (var receiver in _receivers) receiver.OnRent();
        }

        internal void NotifyReturn()
        {
            foreach (var receiver in _receivers) receiver.OnReturn();
        }

        /// <summary>
        /// 指定秒数後に自動で返却するタイマーを開始する。動作中のタイマーがあれば止めてから開始し直す。
        /// </summary>
        internal void StartTimer(float seconds, bool ignoreTimeScale)
        {
            CancelTimer();
            _timer = new CancellationTokenSource();
            ReturnLaterAsync(seconds, ignoreTimeScale, _timer.Token).Forget();
        }

        /// <summary>時限返却タイマーを止める (返却時・破棄時)。</summary>
        internal void CancelTimer()
        {
            if (_timer == null) return;
            _timer.Cancel();
            _timer.Dispose();
            _timer = null;
        }

        /// <summary>待機してから返却する。途中でキャンセルされた場合は何もしない。</summary>
        private async UniTaskVoid ReturnLaterAsync(float seconds, bool ignoreTimeScale, CancellationToken ct)
        {
            if (await UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale, cancellationToken: ct).SuppressCancellationThrow()) return;
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
            _pool?.NotifyDestroyed(this);
        }
    }
}
