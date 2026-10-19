using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Abubu.Pool
{
    /// <summary>
    /// プレハブ 1 種類分のプール。インスタンスの生成・貸し出し・返却・破棄だけを受け持つ。
    /// どこに置くか (親 / シーン) や時限返却は <see cref="PoolService"/> と <see cref="PooledObject"/> の責務。
    /// </summary>
    /// <remarks>
    /// 状態は次の 2 つの集合だけで管理する。1 つのインスタンスは必ずどちらか一方にだけ入る。
    /// ・_inactive … 待機中 (非アクティブ、ストレージの子)。破棄済みの参照が混ざっていても Rent 時に読み飛ばす
    /// ・_active   … 貸し出し中
    /// </remarks>
    internal sealed class PrefabPool : IDisposable
    {
        private readonly GameObject _prefab;
        private readonly Transform _storage;
        private readonly Stack<PooledObject> _inactive = new();
        private readonly HashSet<PooledObject> _active = new();
        private bool _disposed;

        public PoolOptions Options { get; set; }

        /// <summary>このプールを持っている PoolService (別の PoolService のインスタンスを返却されたときの判定用)</summary>
        public object Owner { get; }

        public PrefabPool(object owner, GameObject prefab, Transform storage, PoolOptions options)
        {
            Owner = owner;
            _prefab = prefab;
            _storage = storage;
            Options = options;
        }

        public PoolStats Stats => new(_active.Count, _inactive.Count);

        /// <summary>
        /// 待機中のものを取り出す (無ければ生成する)。まだ非アクティブで、通知もしていない。
        /// 配置してからアクティブ化したいので、<see cref="Activate"/> は呼び出し側が行う。
        /// </summary>
        public PooledObject Take()
        {
            ThrowIfDisposed();

            PooledObject item = null;
            // 待機中にユーザーや親ごと破棄されたものは捨てる
            while (_inactive.Count > 0 && item == null) item = _inactive.Pop();
            if (item == null) item = Create();

            _active.Add(item);
            item.MarkRented();
            return item;
        }

        /// <summary>アクティブ化して OnRent を通知する (Take の後、配置を終えてから呼ぶ)</summary>
        public static void Activate(PooledObject item)
        {
            item.gameObject.SetActive(true);
            item.NotifyRent();
        }

        /// <summary>返却する。このプールの貸し出し中でなければ (二重返却など) false</summary>
        public bool Return(PooledObject item)
        {
            if (_disposed || !_active.Remove(item)) return false;

            item.CancelTimer();
            item.MarkReturned();
            item.NotifyReturn();
            // OnReturn の中で Destroy された場合はここで終わり
            if (item == null) return true;

            if (Options.MaxInactive > 0 && _inactive.Count >= Options.MaxInactive)
            {
                DestroyInstance(item.gameObject);
                return true;
            }

            Store(item);
            return true;
        }

        /// <summary>待機中が <paramref name="count"/> 個になるまで生成する (通知はしない)</summary>
        public void Prewarm(int count)
        {
            ThrowIfDisposed();
            if (Options.MaxInactive > 0) count = Mathf.Min(count, Options.MaxInactive);
            RemoveDestroyedInactive();
            while (_inactive.Count < count) Store(Create());
        }

        /// <summary>貸し出し中のうち条件に合うものを返却する</summary>
        public void ReturnWhere(Func<PooledObject, bool> predicate, List<PooledObject> buffer)
        {
            buffer.Clear();
            foreach (var item in _active)
            {
                // 破棄済みは返却できないので管理から外すだけ
                if (item == null || predicate(item)) buffer.Add(item);
            }
            foreach (var item in buffer)
            {
                if (item == null) _active.Remove(item);
                else Return(item);
            }
        }

        /// <summary>待機中の在庫を破棄する</summary>
        public void Clear()
        {
            while (_inactive.Count > 0)
            {
                var item = _inactive.Pop();
                if (item != null) DestroyInstance(item.gameObject);
            }
        }

        /// <summary>貸し出し中のインスタンスが外部で破棄されたときに PooledObject から呼ばれる</summary>
        public void NotifyDestroyed(PooledObject item) => _active.Remove(item);

        /// <summary>待機中も貸し出し中もすべて破棄する</summary>
        public void Dispose()
        {
            if (_disposed) return;
            Clear();
            foreach (var item in _active)
            {
                if (item == null) continue;
                item.Unbind();
                DestroyInstance(item.gameObject);
            }
            _active.Clear();
            _disposed = true;
        }

        private PooledObject Create()
        {
            // ストレージ (非アクティブ) の下に生成するので、Awake / OnEnable は初回の貸し出し時まで走らない
            var instance = Object.Instantiate(_prefab, _storage, false);
            // "(Clone)" を付けず、プレハブと同じ名前にしておく
            instance.name = _prefab.name;
            instance.SetActive(false);
            // プレハブに最初から付いていても重複させない
            if (!instance.TryGetComponent<PooledObject>(out var pooled)) pooled = instance.AddComponent<PooledObject>();
            pooled.Bind(this);
            return pooled;
        }

        private void Store(PooledObject item)
        {
            item.gameObject.SetActive(false);
            item.transform.SetParent(_storage, false);
            _inactive.Push(item);
        }

        private void RemoveDestroyedInactive()
        {
            if (_inactive.Count == 0) return;
            var alive = _inactive.ToArray();
            _inactive.Clear();
            // ToArray は Pop 順 (新しい順) なので、逆から積み直して順序を保つ
            for (var i = alive.Length - 1; i >= 0; i--)
            {
                if (alive[i] != null) _inactive.Push(alive[i]);
            }
        }

        private static void DestroyInstance(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PrefabPool), $"\"{(_prefab != null ? _prefab.name : "?")}\" のプールは破棄済みです");
        }
    }
}
