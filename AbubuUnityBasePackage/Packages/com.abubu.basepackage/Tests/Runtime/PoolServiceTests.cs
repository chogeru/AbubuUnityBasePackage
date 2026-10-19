using System.Collections;
using Abubu.Pool;
using NUnit.Framework;
using uPools;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    /// <summary>Destroy を伴うため PlayMode で実行する</summary>
    public sealed class PoolServiceTests
    {
        private sealed class Probe : MonoBehaviour, IPoolCallbackReceiver
        {
            public int Rented;
            public int Returned;
            public void OnRent() => Rented++;
            public void OnReturn() => Returned++;
        }

        private PoolService _pool;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _prefab = new GameObject("TestPrefab");
            _prefab.AddComponent<Probe>();
            _prefab.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Dispose();
            Object.Destroy(_prefab);
        }

        [Test]
        public void Rent_ReturnsActiveInstance_AndNotifiesReceiver()
        {
            var instance = _pool.Rent(_prefab, new Vector3(1, 2, 3), Quaternion.identity);

            Assert.That(instance.activeSelf, Is.True);
            Assert.That(instance.transform.position, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(instance.GetComponent<Probe>().Rented, Is.EqualTo(1));
            Assert.That(instance.name, Is.EqualTo(_prefab.name));
        }

        [Test]
        public void Return_DeactivatesAndReusesInstance()
        {
            var first = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            _pool.Return(first);

            Assert.That(first.activeSelf, Is.False);
            Assert.That(first.GetComponent<Probe>().Returned, Is.EqualTo(1));

            var second = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Return_Twice_IsIgnored()
        {
            var instance = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            _pool.Return(instance);
            _pool.Return(instance);

            Assert.That(instance.GetComponent<Probe>().Returned, Is.EqualTo(1));
        }

        [Test]
        public void Rent_NullPrefab_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _pool.Rent((GameObject)null, Vector3.zero, Quaternion.identity));
        }

        [Test]
        public void ReturnAll_ReturnsEveryRentedInstance()
        {
            var a = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            var b = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);

            _pool.ReturnAll();

            Assert.That(a.activeSelf, Is.False);
            Assert.That(b.activeSelf, Is.False);
        }

        [Test]
        public void Prewarm_ThenRent_NotifiesOnlyOnce()
        {
            _pool.Prewarm(_prefab, 3);

            var instance = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            Assert.That(instance.GetComponent<Probe>().Rented, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Return_UnmanagedObject_IsDestroyed()
        {
            var outsider = new GameObject("Outsider");
            _pool.Return(outsider);
            yield return null;

            Assert.That(outsider == null, Is.True);
        }

        [UnityTest]
        public IEnumerator ReturnAfter_ReturnsAfterDelay()
        {
            var instance = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            _pool.ReturnAfter(instance, 0.05f);
            Assert.That(instance.activeSelf, Is.True);

            yield return new WaitForSecondsRealtime(0.3f);

            Assert.That(instance.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator ManualReturn_CancelsTimer()
        {
            var instance = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            _pool.ReturnAfter(instance, 0.1f);
            _pool.Return(instance);
            // 再び借りた後、古いタイマーで勝手に返却されないこと
            var again = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);

            yield return new WaitForSecondsRealtime(0.3f);

            Assert.That(again.activeSelf, Is.True);
            Assert.That(again.GetComponent<Probe>().Returned, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DestroyedWhileRented_DoesNotBreakReturnAll()
        {
            var instance = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            Object.Destroy(instance);
            yield return null;

            Assert.DoesNotThrow(() => _pool.ReturnAll());
            Assert.That(_pool.GetStats(_prefab).Active, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator DestroyedWhileInactive_IsSkippedOnRent()
        {
            var first = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            _pool.Return(first);
            Object.Destroy(first);
            yield return null;

            GameObject next = null;
            Assert.DoesNotThrow(() => next = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity));
            Assert.That(next != null && next.activeSelf, Is.True);
        }

        [Test]
        public void Rent_FromEmptyPool_ActivatesAndNotifiesEveryTime()
        {
            // 在庫が空のときの生成経路でも OnRent が呼ばれること (uPools の Rent はこの経路で OnRent を呼ばない)
            var a = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            var b = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);

            Assert.That(a.activeSelf && b.activeSelf, Is.True);
            Assert.That(a.GetComponent<Probe>().Rented, Is.EqualTo(1));
            Assert.That(b.GetComponent<Probe>().Rented, Is.EqualTo(1));
        }

        [Test]
        public void Prewarm_DoesNotNotify_AndFillsInactive()
        {
            _pool.Prewarm(_prefab, 3);

            Assert.That(_pool.GetStats(_prefab), Is.EqualTo(new PoolStats(0, 3)));
        }

        [UnityTest]
        public IEnumerator Configure_MaxInactive_DestroysOverflowOnReturn()
        {
            _pool.Configure(_prefab, new PoolOptions(1));
            var a = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
            var b = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);

            _pool.Return(a);
            _pool.Return(b);
            yield return null;

            Assert.That(_pool.GetStats(_prefab).Inactive, Is.EqualTo(1));
            Assert.That(b == null, Is.True);
        }

        [Test]
        public void Rent_WithoutParent_PlacesInActiveScene()
        {
            var instance = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);

            Assert.That(instance.scene, Is.EqualTo(UnityEngine.SceneManagement.SceneManager.GetActiveScene()));
        }

        [Test]
        public void ReturnAll_Scene_KeepsInstancesInOtherScenes()
        {
            var persistentParent = new GameObject("Persistent");
            Object.DontDestroyOnLoad(persistentParent);
            try
            {
                var inScene = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity);
                var persistent = _pool.Rent(_prefab, Vector3.zero, Quaternion.identity, persistentParent.transform);

                _pool.ReturnAll(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

                Assert.That(inScene.activeSelf, Is.False);
                Assert.That(persistent.activeSelf, Is.True);
            }
            finally
            {
                _pool.ReturnAll();
                Object.Destroy(persistentParent);
            }
        }

        [UnityTest]
        public IEnumerator Dispose_DestroysRentedInstances()
        {
            var pool = new PoolService();
            var instance = pool.Rent(_prefab, Vector3.zero, Quaternion.identity);

            pool.Dispose();
            yield return null;

            Assert.That(instance == null, Is.True);
        }

        [Test]
        public void Return_InstanceOfAnotherPoolService_IsIgnored()
        {
            var other = new PoolService();
            try
            {
                var instance = other.Rent(_prefab, Vector3.zero, Quaternion.identity);
                _pool.Return(instance);

                Assert.That(instance.activeSelf, Is.True);
            }
            finally
            {
                other.Dispose();
            }
        }

        [Test]
        public void Rent_PrefabWithPooledObject_DoesNotDuplicateComponent()
        {
            var prefab = new GameObject("WithPooled");
            prefab.AddComponent<PooledObject>();
            prefab.SetActive(false);
            try
            {
                var instance = _pool.Rent(prefab, Vector3.zero, Quaternion.identity);
                Assert.That(instance.GetComponents<PooledObject>().Length, Is.EqualTo(1));
            }
            finally
            {
                _pool.ReturnAll();
                Object.Destroy(prefab);
            }
        }
    }
}
