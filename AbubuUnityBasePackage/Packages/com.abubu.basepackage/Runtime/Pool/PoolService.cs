using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using uPools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Abubu.Pool
{
    /// <summary>
    /// プレハブ単位で GameObject をプールする汎用 PoolManager (弾・敵・エフェクト・ダメージ表記など)。
    /// <code>
    /// var bullet = _pool.Rent(bulletPrefab, muzzle.position, muzzle.rotation);
    /// _pool.Return(bullet);            // 手動で返す
    /// _pool.ReturnAfter(bullet, 2f);   // 2 秒後に返す
    /// </code>
    /// 返却時に IPoolCallbackReceiver (uPools) の OnRent / OnReturn が呼ばれるので、状態リセットはそこに書く。
    /// シーン遷移時の一括回収は、ReturnAll を ISceneService.OnBeforeSceneUnload に繋いで行う (Installer 側で結線)。
    /// </summary>
    public interface IPoolService
    {
        GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null);
        T Rent<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component;
        /// <summary>プールから借りたものは返却、そうでなければ Destroy する</summary>
        void Return(GameObject instance);
        void ReturnAfter(GameObject instance, float seconds);
        void Prewarm(GameObject prefab, int count);
        /// <summary>貸し出し中のものをすべて返却する</summary>
        void ReturnAll();
    }

    public sealed class PoolService : IPoolService, IDisposable
    {
        private sealed class PrefabPool : ObjectPoolBase<GameObject>
        {
            private readonly GameObject _prefab;
            private readonly Transform _root;
            private readonly PoolService _owner;

            public PrefabPool(GameObject prefab, Transform root, PoolService owner)
            {
                _prefab = prefab;
                _root = root;
                _owner = owner;
            }

            protected override GameObject CreateInstance()
            {
                var instance = Object.Instantiate(_prefab, _root);
                instance.name = _prefab.name;
                instance.SetActive(false);
                instance.AddComponent<PooledObject>().Bind(_owner);
                return instance;
            }

            protected override void OnRent(GameObject instance)
            {
                instance.SetActive(true);
                foreach (var receiver in instance.GetComponentsInChildren<IPoolCallbackReceiver>(true)) receiver.OnRent();
            }

            protected override void OnReturn(GameObject instance)
            {
                foreach (var receiver in instance.GetComponentsInChildren<IPoolCallbackReceiver>(true)) receiver.OnReturn();
                instance.SetActive(false);
                // シーン側の親と一緒に破棄されないよう DontDestroyOnLoad の root に戻す
                instance.transform.SetParent(_root, false);
            }

            protected override void OnDestroy(GameObject instance)
            {
                if (instance != null) Object.Destroy(instance);
            }
        }

        private readonly Transform _root;
        private readonly Dictionary<GameObject, PrefabPool> _pools = new();
        private readonly Dictionary<GameObject, PrefabPool> _rented = new();
        private readonly List<GameObject> _buffer = new();

        public PoolService()
        {
            var go = new GameObject("[Abubu.Pool]");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
        }

        public GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            var pool = GetPool(prefab);
            var instance = pool.Rent();
            // 前のシーンの親と一緒に破棄されていたものは捨てて作り直す
            while (instance == null) instance = pool.Rent();

            var t = instance.transform;
            if (parent != null) t.SetParent(parent, false);
            t.SetPositionAndRotation(position, rotation);
            _rented[instance] = pool;
            return instance;
        }

        public T Rent<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component =>
            Rent(prefab.gameObject, position, rotation, parent).GetComponent<T>();

        public void Return(GameObject instance)
        {
            if (instance == null) return;

            if (_rented.Remove(instance, out var pool))
            {
                instance.GetComponent<PooledObject>()?.CancelTimer();
                pool.Return(instance);
            }
            else if (instance.GetComponent<PooledObject>() == null)
            {
                Object.Destroy(instance);
            }
        }

        public void ReturnAfter(GameObject instance, float seconds)
        {
            if (instance == null) return;
            if (!instance.TryGetComponent<PooledObject>(out var pooled))
            {
                Object.Destroy(instance, seconds);
                return;
            }
            pooled.StartTimer(seconds);
        }

        public void Prewarm(GameObject prefab, int count)
        {
            var pool = GetPool(prefab);
            if (pool.Count < count) pool.Prewarm(count - pool.Count);
        }

        public void ReturnAll()
        {
            _buffer.Clear();
            _buffer.AddRange(_rented.Keys);
            foreach (var instance in _buffer) Return(instance);
            PurgeDestroyed();
        }

        private PrefabPool GetPool(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out var pool))
            {
                pool = new PrefabPool(prefab, _root, this);
                _pools.Add(prefab, pool);
            }
            return pool;
        }

        private void PurgeDestroyed()
        {
            _buffer.Clear();
            foreach (var instance in _rented.Keys)
            {
                if (instance == null) _buffer.Add(instance);
            }
            foreach (var instance in _buffer) _rented.Remove(instance);
        }

        /// <summary>貸し出し中のインスタンスがユーザーに Destroy された時に登録を外す</summary>
        internal void NotifyDestroyed(GameObject instance) => _rented.Remove(instance);

        public void Dispose()
        {
            foreach (var pool in _pools.Values) pool.Dispose();
            _pools.Clear();
            _rented.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }
}
