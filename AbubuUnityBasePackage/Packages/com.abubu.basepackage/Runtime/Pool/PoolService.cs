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
        /// <summary>
        /// プレハブのインスタンスをプールから借りる。空きが無ければ新しく生成する。
        /// </summary>
        /// <param name="prefab">元になるプレハブ。このプレハブごとに別々のプールが作られる</param>
        /// <param name="parent">親 Transform。null ならプール用のルート (DontDestroyOnLoad) の下に置かれる</param>
        /// <returns>アクティブ化されたインスタンス</returns>
        GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null);

        /// <summary>
        /// コンポーネント型でプレハブを指定して借りる版。戻り値も同じ型で受け取れる。
        /// </summary>
        T Rent<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component;

        /// <summary>プールから借りたものは返却、そうでなければ Destroy する</summary>
        void Return(GameObject instance);

        /// <summary>
        /// 指定秒数後に返却する。プール管理外のオブジェクトなら、同じ秒数後に Destroy する。
        /// 待っている間に手動で Return した場合、タイマーは取り消される。
        /// </summary>
        void ReturnAfter(GameObject instance, float seconds);

        /// <summary>
        /// プール内の在庫が <paramref name="count"/> 個になるまで、先にインスタンスを作っておく。
        /// ゲーム中に初めて Rent したときの Instantiate によるカクつきを防ぐ。
        /// </summary>
        void Prewarm(GameObject prefab, int count);

        /// <summary>貸し出し中のものをすべて返却する</summary>
        void ReturnAll();
    }

    /// <summary>
    /// <see cref="IPoolService"/> の標準実装。プール本体には uPools の ObjectPoolBase を使う。
    /// </summary>
    /// <remarks>
    /// 管理している表は次の 2 つ。
    /// ・_pools  … プレハブ → そのプレハブ用のプール
    /// ・_rented … 貸し出し中のインスタンス → 借りた元のプール (Return 時に返す先を引くため)
    /// </remarks>
    public sealed class PoolService : IPoolService, IDisposable
    {
        /// <summary>1 つのプレハブに対応するプール</summary>
        private sealed class PrefabPool : ObjectPoolBase<GameObject>
        {
            private readonly GameObject _prefab;

            /// <summary>待機中のインスタンスを置いておく親 (DontDestroyOnLoad)</summary>
            private readonly Transform _root;
            private readonly PoolService _owner;

            public PrefabPool(GameObject prefab, Transform root, PoolService owner)
            {
                _prefab = prefab;
                _root = root;
                _owner = owner;
            }

            /// <summary>在庫が無いときに新しいインスタンスを作る。非アクティブ状態で作り、PooledObject を付ける。</summary>
            protected override GameObject CreateInstance()
            {
                var instance = Object.Instantiate(_prefab, _root);
                // "(Clone)" を付けず、プレハブと同じ名前にしておく
                instance.name = _prefab.name;
                instance.SetActive(false);
                instance.AddComponent<PooledObject>().Bind(_owner);
                return instance;
            }

            /// <summary>貸し出し時: アクティブ化し、子を含む IPoolCallbackReceiver に通知する。</summary>
            protected override void OnRent(GameObject instance)
            {
                instance.SetActive(true);
                foreach (var receiver in instance.GetComponentsInChildren<IPoolCallbackReceiver>(true)) receiver.OnRent();
            }

            /// <summary>返却時: 通知してから非アクティブ化し、プール用のルートの下に戻す。</summary>
            protected override void OnReturn(GameObject instance)
            {
                foreach (var receiver in instance.GetComponentsInChildren<IPoolCallbackReceiver>(true)) receiver.OnReturn();
                instance.SetActive(false);
                // シーン側の親と一緒に破棄されないよう DontDestroyOnLoad の root に戻す
                instance.transform.SetParent(_root, false);
            }

            /// <summary>プールの破棄時: インスタンスを破棄する (既に破棄済みなら何もしない)。</summary>
            protected override void OnDestroy(GameObject instance)
            {
                if (instance != null) Object.Destroy(instance);
            }
        }

        /// <summary>全プール共通の待機場所 "[Abubu.Pool]" (シーンをまたいで残る)</summary>
        private readonly Transform _root;

        /// <summary>プレハブ → プール</summary>
        private readonly Dictionary<GameObject, PrefabPool> _pools = new();

        /// <summary>貸し出し中のインスタンス → 借りた元のプール</summary>
        private readonly Dictionary<GameObject, PrefabPool> _rented = new();

        /// <summary>Dictionary を列挙しながら変更しないための作業用リスト (GC を減らすため使い回す)</summary>
        private readonly List<GameObject> _buffer = new();

        /// <summary>プール用のルート GameObject を作り、シーン遷移で破棄されないようにする。</summary>
        public PoolService()
        {
            var go = new GameObject("[Abubu.Pool]");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public T Rent<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component =>
            Rent(prefab.gameObject, position, rotation, parent).GetComponent<T>();

        /// <inheritdoc />
        /// <remarks>
        /// PooledObject は付いているが貸し出し中ではない (返却済みの) インスタンスは、二重返却とみなして何もしない。
        /// </remarks>
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

        /// <inheritdoc />
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

        /// <inheritdoc />
        public void Prewarm(GameObject prefab, int count)
        {
            var pool = GetPool(prefab);
            // 足りない分だけ作る (既に在庫が十分ならなにもしない)
            if (pool.Count < count) pool.Prewarm(count - pool.Count);
        }

        /// <inheritdoc />
        public void ReturnAll()
        {
            // Return が _rented を変更するため、キーを一旦コピーしてから回す
            _buffer.Clear();
            _buffer.AddRange(_rented.Keys);
            foreach (var instance in _buffer) Return(instance);
            PurgeDestroyed();
        }

        /// <summary>プレハブに対応するプールを取得する。初回はここで作成する。</summary>
        private PrefabPool GetPool(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out var pool))
            {
                pool = new PrefabPool(prefab, _root, this);
                _pools.Add(prefab, pool);
            }
            return pool;
        }

        /// <summary>
        /// 貸し出し表から破棄済みのインスタンスを取り除く。
        /// (Unity の == null は破棄済みオブジェクトでも true になるため、それを利用して判定している)
        /// </summary>
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

        /// <summary>全プールと待機中のインスタンスを破棄する (DI コンテナの破棄時に呼ばれる)。</summary>
        public void Dispose()
        {
            foreach (var pool in _pools.Values) pool.Dispose();
            _pools.Clear();
            _rented.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }
}
