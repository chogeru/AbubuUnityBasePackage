using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Abubu.Effects;
using Abubu.Pool;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    public sealed class EffectServiceTests
    {
        private sealed class RecordingHook : IEffectPlayHook
        {
            public readonly List<string> Keys = new();
            public void OnPlay(EffectEntry entry, GameObject instance, Vector3 position) => Keys.Add(entry.Key);
        }

        private PoolService _pool;
        private GameObject _prefab;
        private EffectLibrary _library;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _prefab = new GameObject("Fx");
            _prefab.SetActive(false);
            _library = ScriptableObject.CreateInstance<EffectLibrary>();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Dispose();
            Object.Destroy(_prefab);
            Object.Destroy(_library);
        }

        private EffectService Create(params IEffectPlayHook[] hooks) =>
            new(_pool, new EffectSettings { Library = _library }, new List<IEffectPlayHook>(hooks));

        /// <summary>SerializeField の 'effects' に直接追加する (Inspector での登録に相当)</summary>
        private void Register(EffectEntry entry)
        {
            var field = typeof(EffectLibrary).GetField("effects", BindingFlags.NonPublic | BindingFlags.Instance);
            ((List<EffectEntry>)field.GetValue(_library)).Add(entry);
        }

        [Test]
        public void Play_ByPrefab_ReturnsActiveInstance()
        {
            var fx = Create();
            var instance = fx.Play(_prefab, new Vector3(0, 1, 0), lifetime: 0f);

            Assert.That(instance.activeSelf, Is.True);
            Assert.That(instance.transform.position, Is.EqualTo(new Vector3(0, 1, 0)));
        }

        [Test]
        public void Play_UnknownKey_ReturnsNullAndWarns()
        {
            var fx = Create();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Abubu\.Effect\].*missing"));

            Assert.That(fx.Play("missing", Vector3.zero), Is.Null);
        }

        [Test]
        public void Play_ByKey_CallsHooks()
        {
            Register(new EffectEntry { Key = "boom", Prefab = _prefab, Lifetime = 5f });
            var hook = new RecordingHook();
            var fx = Create(hook);

            var instance = fx.Play("boom", Vector3.zero);

            Assert.That(instance, Is.Not.Null);
            Assert.That(hook.Keys, Is.EqualTo(new[] { "boom" }));
        }

        [Test]
        public void Play_UnknownKey_SuggestsNearbyKey()
        {
            Register(new EffectEntry { Key = "explosion", Prefab = _prefab });
            var fx = Create();
            LogAssert.Expect(LogType.Warning, new Regex("explosoin.*explosion"));

            fx.Play("explosoin", Vector3.zero);
        }

        [UnityTest]
        public IEnumerator Play_WithLifetime_ReturnsToPoolAfterwards()
        {
            var fx = Create();
            var instance = fx.Play(_prefab, Vector3.zero, lifetime: 0.05f);

            yield return new WaitForSecondsRealtime(0.3f);

            Assert.That(instance.activeSelf, Is.False);
        }

        [Test]
        public void Stop_ReturnsInstanceImmediately()
        {
            var fx = Create();
            var instance = fx.Play(_prefab, Vector3.zero, lifetime: 0f);

            fx.Stop(instance);

            Assert.That(instance.activeSelf, Is.False);
        }
    }
}
