using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Abubu.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    /// <summary>
    /// 読み込みをテストから操作できる AddressAudioClipProviderBase。
    /// Ready に入っているアドレスは同期で完了し (Addressables で読み込み済みのアセット相当)、それ以外は Complete / Fail まで終わらない
    /// </summary>
    internal sealed class FakeAddressClipProvider : AddressAudioClipProviderBase
    {
        public readonly Dictionary<string, AudioClip> Ready = new();
        public readonly List<string> Released = new();
        public int LoadCount;
        private readonly Dictionary<string, UniTaskCompletionSource<AudioClip>> _pending = new();

        protected override UniTask<AudioClip> LoadClipAsync(string address)
        {
            LoadCount++;
            if (Ready.TryGetValue(address, out var clip)) return UniTask.FromResult(clip);
            var source = new UniTaskCompletionSource<AudioClip>();
            _pending.Add(address, source);
            return source.Task;
        }

        protected override void ReleaseClip(string address, AudioClip clip) => Released.Add(address);

        public void Complete(string address, AudioClip clip)
        {
            var source = _pending[address];
            _pending.Remove(address);
            source.TrySetResult(clip);
        }

        public void Fail(string address)
        {
            var source = _pending[address];
            _pending.Remove(address);
            source.TrySetException(new InvalidOperationException("not found"));
        }
    }

    public sealed class AudioAddressClipProviderTests
    {
        private AudioClip _clip;
        private FakeAddressClipProvider _provider;
        private SoundEntry _entry;

        [SetUp]
        public void SetUp()
        {
            _clip = AudioClip.Create("addr", 22050, 1, 22050, false);
            _provider = new FakeAddressClipProvider();
            _entry = new SoundEntry { Key = "boss", Address = "Audio/Boss" };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_clip);

        private static AudioClip Await(UniTask<AudioClip> task) => task.GetAwaiter().GetResult();

        [Test]
        public void AlreadyLoadedAddress_CompletesSynchronously_AndHoldsRef()
        {
            // IAssetService.PreloadAsync などで読み込み済み = LoadClipAsync が同期で完了する場合
            _provider.Ready[_entry.Address] = _clip;

            var task = _provider.AcquireAsync(_entry);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(Await(task), Is.SameAs(_clip));
            Assert.That(_provider.GetRefCount(_entry.Address), Is.EqualTo(1));
            Assert.That(_provider.IsLoaded(_entry), Is.True);
            Assert.That(_provider.Released, Is.Empty, "取得直後に解放されてはいけない");

            // 2 回目以降は同期取得できる
            Assert.That(_provider.TryAcquire(_entry, out var again), Is.True);
            Assert.That(again, Is.SameAs(_clip));
            Assert.That(_provider.GetRefCount(_entry.Address), Is.EqualTo(2));

            _provider.Release(_entry);
            Assert.That(_provider.Released, Is.Empty);
            _provider.Release(_entry);
            Assert.That(_provider.Released, Is.EqualTo(new[] { _entry.Address }));
            Assert.That(_provider.IsLoaded(_entry), Is.False);
        }

        [Test]
        public void ConcurrentRequests_ShareOneLoad()
        {
            var a = _provider.AcquireAsync(_entry);
            var b = _provider.AcquireAsync(_entry);
            Assert.That(_provider.LoadCount, Is.EqualTo(1));
            Assert.That(_provider.GetRefCount(_entry.Address), Is.EqualTo(2));

            _provider.Complete(_entry.Address, _clip);
            Assert.That(Await(a), Is.SameAs(_clip));
            Assert.That(Await(b), Is.SameAs(_clip));

            _provider.Release(_entry);
            _provider.Release(_entry);
            Assert.That(_provider.Released, Is.EqualTo(new[] { _entry.Address }));
        }

        [Test]
        public void CanceledWhileLoading_ReleasesAfterLoad()
        {
            using var cts = new CancellationTokenSource();
            var task = _provider.AcquireAsync(_entry, cts.Token);
            cts.Cancel();
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(_provider.GetRefCount(_entry.Address), Is.EqualTo(0));

            _provider.Complete(_entry.Address, _clip);
            Assert.That(_provider.Released, Is.EqualTo(new[] { _entry.Address }));
            Assert.That(_provider.IsLoaded(_entry), Is.False);
        }

        [Test]
        public void FailedLoad_ReturnsNull_AndRetriesNextTime()
        {
            var task = _provider.AcquireAsync(_entry);
            LogAssert.Expect(LogType.Error, new Regex("Audio/Boss"));
            _provider.Fail(_entry.Address);
            Assert.That(Await(task), Is.Null);
            Assert.That(_provider.GetRefCount(_entry.Address), Is.EqualTo(0));

            _provider.Ready[_entry.Address] = _clip;
            Assert.That(Await(_provider.AcquireAsync(_entry)), Is.SameAs(_clip));
            Assert.That(_provider.LoadCount, Is.EqualTo(2));
        }

        [Test]
        public void NullClip_IsTreatedAsFailure_AndReleased()
        {
            _provider.Ready[_entry.Address] = null;
            LogAssert.Expect(LogType.Error, new Regex("Audio/Boss"));
            Assert.That(Await(_provider.AcquireAsync(_entry)), Is.Null);
            Assert.That(_provider.GetRefCount(_entry.Address), Is.EqualTo(0));
            Assert.That(_provider.Released, Is.EqualTo(new[] { _entry.Address }));
        }

        [Test]
        public void Preload_ThenTryAcquire_IsSynchronous_AndUnloadReleases()
        {
            _provider.Ready[_entry.Address] = _clip;
            _provider.PreloadAsync(new[] { _entry }).GetAwaiter().GetResult();
            _provider.PreloadAsync(new[] { _entry }).GetAwaiter().GetResult(); // 2 回目は参照を増やさない
            Assert.That(_provider.GetRefCount(_entry.Address), Is.EqualTo(1));

            Assert.That(_provider.TryAcquire(_entry, out var clip), Is.True);
            Assert.That(clip, Is.SameAs(_clip));
            _provider.Release(_entry);

            _provider.Unload(new[] { _entry });
            Assert.That(_provider.Released, Is.EqualTo(new[] { _entry.Address }));
        }

        [Test]
        public void SePlayer_PlaysAddressAlreadyLoaded()
        {
            // 不具合の再現: 読み込み済みのアドレスで AcquireAsync が null を返し、SE が鳴らなかった
            using var rig = new AudioTestRig();
            // Address だけのエントリにする
            var entry = rig.Add(SoundCategory.Se, "boss");
            entry.Clips = null;
            entry.Address = "Audio/Boss";
            rig.Settings.SeLoadTimeout = 0f;
            _provider.Ready[entry.Address] = _clip;

            var player = rig.Track(new SePlayer(rig.CreateRoot(_provider), rig.Settings, rig.Volume));
            player.Play("boss");

            var source = rig.Root.Transform.GetComponentInChildren<AudioSource>();
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.SameAs(_clip));
            Assert.That(_provider.GetRefCount(entry.Address), Is.EqualTo(1));
        }
    }
}
