using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Abubu.Pool
{
    /// <summary>
    /// <see cref="IPoolService"/> の標準実装。プレハブ → <see cref="PrefabPool"/> の表を持ち、インスタンスの配置 (親 / シーン) を決める。
    /// </summary>
    /// <remarks>
    /// 待機中のインスタンスは「非アクティブなストレージ」の子に置く。
    /// ストレージ自体が非アクティブなので、生成直後の Awake / OnEnable は初回の貸し出しまで走らない。
    /// ストレージは最初に必要になったときに作る (使わなければ何も生成しない)。
    /// </remarks>
    public sealed class PoolService : IPoolService, IDisposable
    {
        private const string StorageName = "[Abubu.Pool]";

        /// <summary>プレハブ → プール</summary>
        private readonly Dictionary<GameObject, PrefabPool> _pools = new();

        /// <summary>Rent より前に Configure された設定 (プール生成時に使う)</summary>
        private readonly Dictionary<GameObject, PoolOptions> _options = new();

        /// <summary>列挙しながら変更しないための作業用リスト (GC を減らすため使い回す)</summary>
        private readonly List<PooledObject> _buffer = new();

        private Transform _storage;
        private bool _disposed;

        /// <inheritdoc />
        public GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            ThrowIfDisposed();

            var item = GetPool(prefab).Take();
            var t = item.transform;
            if (parent != null)
            {
                t.SetParent(parent, false);
            }
            else
            {
                // 親なしはアクティブシーンのルートへ置く (DontDestroyOnLoad に混ぜない)。
                // シーンローカルの物理・ライティング・GetRootGameObjects の前提を崩さないため
                t.SetParent(null, false);
                var active = SceneManager.GetActiveScene();
                if (active.IsValid() && active.isLoaded && item.gameObject.scene != active)
                {
                    SceneManager.MoveGameObjectToScene(item.gameObject, active);
                }
            }
            t.SetPositionAndRotation(position, rotation);

            PrefabPool.Activate(item);
            return item.gameObject;
        }

        /// <inheritdoc />
        public T Rent<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            return Rent(prefab.gameObject, position, rotation, parent).GetComponent<T>();
        }

        /// <inheritdoc />
        public void Return(GameObject instance)
        {
            if (instance == null) return;

            if (!instance.TryGetComponent<PooledObject>(out var pooled))
            {
                // プール管理外のものは破棄する
                Object.Destroy(instance);
                return;
            }

            // 返却済み (二重返却) や別の PoolService のものは何もしない
            if (pooled.IsRented && IsOwned(pooled)) pooled.Pool.Return(pooled);
        }

        /// <inheritdoc />
        public void ReturnAfter(GameObject instance, float seconds, bool ignoreTimeScale = false)
        {
            if (instance == null) return;
            if (!instance.TryGetComponent<PooledObject>(out var pooled) || !IsOwned(pooled))
            {
                Object.Destroy(instance, seconds);
                return;
            }
            if (pooled.IsRented) pooled.StartTimer(seconds, ignoreTimeScale);
        }

        /// <inheritdoc />
        public void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            ThrowIfDisposed();
            if (count > 0) GetPool(prefab).Prewarm(count);
        }

        /// <inheritdoc />
        public void Configure(GameObject prefab, PoolOptions options)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            _options[prefab] = options;
            if (_pools.TryGetValue(prefab, out var pool)) pool.Options = options;
        }

        /// <inheritdoc />
        public void ReturnAll()
        {
            foreach (var pool in _pools.Values) pool.ReturnWhere(static _ => true, _buffer);
        }

        /// <inheritdoc />
        public void ReturnAll(UnityScene scene)
        {
            if (!scene.IsValid()) return;
            foreach (var pool in _pools.Values) pool.ReturnWhere(item => item.gameObject.scene == scene, _buffer);
        }

        /// <inheritdoc />
        public void Clear(GameObject prefab)
        {
            if (prefab != null && _pools.TryGetValue(prefab, out var pool)) pool.Clear();
        }

        /// <inheritdoc />
        public PoolStats GetStats(GameObject prefab) =>
            prefab != null && _pools.TryGetValue(prefab, out var pool) ? pool.Stats : default;

        /// <summary>全プールのインスタンス (貸し出し中を含む) とストレージを破棄する (DI コンテナの破棄時に呼ばれる)。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var pool in _pools.Values) pool.Dispose();
            _pools.Clear();
            _options.Clear();
            if (_storage != null)
            {
                if (Application.isPlaying) Object.Destroy(_storage.gameObject);
                else Object.DestroyImmediate(_storage.gameObject);
            }
        }

        private PrefabPool GetPool(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out var pool))
            {
                _options.TryGetValue(prefab, out var options);
                pool = new PrefabPool(this, prefab, GetStorage(), options);
                _pools.Add(prefab, pool);
            }
            return pool;
        }

        private Transform GetStorage()
        {
            if (_storage != null) return _storage;

            var go = new GameObject(StorageName);
            go.SetActive(false);
            // EditMode テストなど再生中でないときは DontDestroyOnLoad できない
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            _storage = go.transform;
            return _storage;
        }

        private bool IsOwned(PooledObject pooled) => pooled.Pool != null && ReferenceEquals(pooled.Pool.Owner, this);

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PoolService));
        }
    }
}
