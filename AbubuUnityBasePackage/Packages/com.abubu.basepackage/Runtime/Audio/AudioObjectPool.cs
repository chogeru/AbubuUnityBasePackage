using System;
using System.Collections.Generic;

namespace Abubu.Audio
{
    /// <summary>
    /// SE / 環境音の AudioSource を使い回すための最小限のプール。
    /// Get は在庫の有無にかかわらず必ず onGet を呼ぶ (新規生成した要素にも初期化が掛かる)。
    /// Dispose 後に Release された要素はプールに戻さず破棄する (フェード完了が Dispose より後になっても安全)。
    /// </summary>
    internal sealed class AudioObjectPool<T> : IDisposable where T : class
    {
        private readonly Stack<T> _stack = new();
        private readonly Func<T> _create;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;

        public bool IsDisposed { get; private set; }
        /// <summary>プール内で待機している (貸し出されていない) 数</summary>
        public int CountInactive => _stack.Count;

        public AudioObjectPool(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null, Action<T> onDestroy = null)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
        }

        /// <summary>count 個になるまで生成して待機させておく (再生時の生成負荷を避ける)</summary>
        public void Prewarm(int count)
        {
            if (IsDisposed) return;
            while (_stack.Count < count)
            {
                var item = _create();
                _onRelease?.Invoke(item);
                _stack.Push(item);
            }
        }

        public T Get()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(AudioObjectPool<T>));
            var item = _stack.Count > 0 ? _stack.Pop() : _create();
            _onGet?.Invoke(item);
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
            _onRelease?.Invoke(item);
            if (IsDisposed) _onDestroy?.Invoke(item);
            else _stack.Push(item);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            while (_stack.Count > 0) _onDestroy?.Invoke(_stack.Pop());
        }
    }
}
