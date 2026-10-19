using Abubu.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    /// <summary>
    /// 一時停止の前に要求され、一時停止中に Clip の読み込みが終わった音は、一時停止した状態で始まる (勝手に鳴り出さない)
    /// </summary>
    /// <remarks>
    /// EditMode では AudioSource が実際に鳴るかは環境次第なので、
    /// 「isPlaying が false (一時停止)」と「Tick で終わったとみなされない」の両方で確認する
    /// </remarks>
    public sealed class AudioPauseTests
    {
        private AudioTestRig _rig;
        private FakeClipProvider _fake;

        [SetUp]
        public void SetUp()
        {
            _rig = new AudioTestRig();
            _fake = new FakeClipProvider();
            _rig.Settings.SeLoadTimeout = 0f; // 時間切れにしない
        }

        [TearDown]
        public void TearDown() => _rig.Dispose();

        private AudioSource[] Sources() => _rig.Root.Transform.GetComponentsInChildren<AudioSource>(includeInactive: true);

        [Test]
        public void Se_LoadedWhilePaused_StartsPaused_AndResumes()
        {
            var entry = _rig.Add(SoundCategory.Se, "engine");
            var player = _rig.Track(new SePlayer(_rig.CreateRoot(_fake), _rig.Settings, _rig.Volume));
            var target = new GameObject("Target");
            try
            {
                var handle = player.PlayAttached("engine", target.transform);
                player.SetPaused(true);
                _fake.Complete(entry);

                var source = Sources()[0];
                Assert.That(source.clip, Is.SameAs(entry.Clips[0]));
                Assert.That(source.isPlaying, Is.False, "一時停止中に鳴り出してはいけない");
                ((ITickable)player).Tick();
                Assert.That(player.IsPlaying(handle), Is.True, "一時停止中の SE は終わったとみなさない");
                Assert.That(_fake.RefCount(entry), Is.EqualTo(1));

                Assert.DoesNotThrow(() => player.SetPaused(false));
                player.Stop(handle);
                Assert.That(_fake.RefCount(entry), Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void Se_StillLoadingAfterResume_PlaysNormally()
        {
            var entry = _rig.Add(SoundCategory.Se, "hit");
            var player = _rig.Track(new SePlayer(_rig.CreateRoot(_fake), _rig.Settings, _rig.Volume));

            player.Play("hit");
            player.SetPaused(true);
            player.SetPaused(false);
            _fake.Complete(entry);

            Assert.That(Sources()[0].clip, Is.SameAs(entry.Clips[0]));
            Assert.That(_fake.RefCount(entry), Is.EqualTo(1));
        }

        [Test]
        public void Voice_LoadedWhilePaused_StartsPaused()
        {
            var entry = _rig.Add(SoundCategory.Voice, "a");
            var player = _rig.Track(new VoicePlayer(_rig.CreateRoot(_fake), _rig.Settings, _rig.Volume));

            player.Play("a");
            player.SetPaused(true);
            _fake.Complete(entry);

            var source = Sources()[0];
            Assert.That(source.clip, Is.SameAs(entry.Clips[0]));
            Assert.That(source.isPlaying, Is.False, "一時停止中に鳴り出してはいけない");
            ((ITickable)player).Tick();
            Assert.That(player.CurrentKey.CurrentValue, Is.EqualTo("a"), "一時停止中のボイスは終わったとみなさない");
            Assert.That(player.IsPlaying, Is.True);

            Assert.DoesNotThrow(() => player.SetPaused(false));
        }

        [Test]
        public void Voice_RequestedWhilePaused_IsNotPaused()
        {
            var entry = _rig.Add(SoundCategory.Voice, "a");
            var player = _rig.Track(new VoicePlayer(_rig.CreateRoot(_fake), _rig.Settings, _rig.Volume));
            var started = 0;
            using var subscription = player.OnVoiceStarted.Subscribe(_ => started++);

            player.SetPaused(true);
            player.Play("a"); // ポーズメニューでの案内音声など
            _fake.Complete(entry);

            Assert.That(started, Is.EqualTo(1));
            Assert.That(player.CurrentKey.CurrentValue, Is.EqualTo("a"));
        }

        [Test]
        public void Ambient_LoadedWhilePaused_StartsPaused()
        {
            var entry = _rig.Add(SoundCategory.Ambient, "wind");
            var player = _rig.Track(new AmbientPlayer(_rig.CreateRoot(_fake), _rig.Settings, _rig.Volume));

            player.PlayAsync("wind", 0f).Forget();
            player.SetPaused(true);
            _fake.Complete(entry);

            var source = Sources()[0];
            Assert.That(source.clip, Is.SameAs(entry.Clips[0]));
            Assert.That(source.isPlaying, Is.False, "一時停止中に鳴り出してはいけない");
            Assert.That(player.IsPlaying("wind"), Is.True);
            Assert.That(_fake.RefCount(entry), Is.EqualTo(1));

            Assert.DoesNotThrow(() => player.SetPaused(false));
        }

        [Test]
        public void Bgm_LoadedWhilePaused_StartsPaused()
        {
            var entry = _rig.Add(SoundCategory.Bgm, "stage");
            var player = _rig.Track(new BgmPlayer(_rig.CreateRoot(_fake), _rig.Settings, _rig.Volume));

            player.PlayAsync("stage", 0f).Forget();
            player.Pause();
            _fake.Complete(entry);

            Assert.That(player.CurrentKey.CurrentValue, Is.EqualTo("stage"));
            foreach (var source in Sources()) Assert.That(source.isPlaying, Is.False, source.name);
            // 一時停止中も「再生中の BGM」とみなす (同じキーの再要求で読み込み直さない)
            player.PlayAsync("stage", 0f).Forget();
            Assert.That(_fake.RefCount(entry), Is.EqualTo(1));

            Assert.DoesNotThrow(() => player.Resume());
        }
    }
}
