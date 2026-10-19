using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Abubu.Audio;
using Abubu.Save;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    /// <summary>
    /// 参照カウントを記録し、読み込みの完了をテストから操作できる IAudioClipProvider。
    /// Loaded に入っているエントリは同期で取得でき、それ以外は Complete を呼ぶまで AcquireAsync が終わらない
    /// </summary>
    internal sealed class FakeClipProvider : IAudioClipProvider
    {
        public readonly HashSet<SoundEntry> Loaded = new();
        public readonly List<SoundEntry> Preloaded = new();
        public readonly List<SoundEntry> Unloaded = new();
        private readonly Dictionary<SoundEntry, int> _refs = new();
        private readonly Dictionary<SoundEntry, UniTaskCompletionSource<AudioClip>> _pending = new();

        public int RefCount(SoundEntry entry) => _refs.TryGetValue(entry, out var count) ? count : 0;
        public bool IsPending(SoundEntry entry) => _pending.ContainsKey(entry);

        public bool TryAcquire(SoundEntry entry, out AudioClip clip)
        {
            clip = null;
            if (!Loaded.Contains(entry)) return false;
            _refs[entry] = RefCount(entry) + 1;
            clip = entry.Clips[0];
            return true;
        }

        public async UniTask<AudioClip> AcquireAsync(SoundEntry entry, CancellationToken ct = default)
        {
            if (!Loaded.Contains(entry))
            {
                if (!_pending.TryGetValue(entry, out var source)) _pending.Add(entry, source = new UniTaskCompletionSource<AudioClip>());
                await source.Task;
                Loaded.Add(entry);
            }
            _refs[entry] = RefCount(entry) + 1;
            return entry.Clips[0];
        }

        /// <summary>読み込みを完了させる (待っていた AcquireAsync の続きが同期的に走る)</summary>
        public void Complete(SoundEntry entry)
        {
            var source = _pending[entry];
            _pending.Remove(entry);
            source.TrySetResult(entry.Clips[0]);
        }

        public void Release(SoundEntry entry)
        {
            var count = RefCount(entry) - 1;
            if (count < 0) throw new InvalidOperationException($"\"{entry.Key}\" を取得した回数より多く Release した");
            _refs[entry] = count;
        }

        public bool IsLoaded(SoundEntry entry) => Loaded.Contains(entry);

        public UniTask PreloadAsync(IReadOnlyList<SoundEntry> entries, CancellationToken ct = default)
        {
            Preloaded.AddRange(entries);
            return UniTask.CompletedTask;
        }

        public void Unload(IReadOnlyList<SoundEntry> entries) => Unloaded.AddRange(entries);
    }

    /// <summary>テスト用のサウンド一式 (SoundLibrary / AudioRoot / 音量) を作って後片付けする</summary>
    internal sealed class AudioTestRig : IDisposable
    {
        private readonly List<Object> _created = new();
        private readonly List<IDisposable> _disposables = new();

        public SoundLibrary Library { get; }
        public SoundSettings Settings { get; }
        public AudioRoot Root { get; private set; }
        public AudioVolumeModel Volume { get; }

        public AudioTestRig()
        {
            Library = ScriptableObject.CreateInstance<SoundLibrary>();
            _created.Add(Library);
            Settings = new SoundSettings { Library = Library, SePoolPrewarm = 0 };
            Volume = new AudioVolumeModel(new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer()));
            _disposables.Add(Volume);
        }

        /// <summary>1 秒の Clip を持つエントリを追加する</summary>
        public SoundEntry Add(SoundCategory category, string key, string subtitleKey = null)
        {
            var clip = AudioClip.Create(key, 22050, 1, 22050, false);
            _created.Add(clip);
            var entry = new SoundEntry { Key = key, Clips = new[] { clip }, SubtitleKey = subtitleKey };

            var field = category switch
            {
                SoundCategory.Bgm => "bgm",
                SoundCategory.Se => "se",
                SoundCategory.Ambient => "ambient",
                _ => "voice",
            };
            var list = (List<SoundEntry>)typeof(SoundLibrary).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Library);
            list.Add(entry);
            Library.ClearCache();
            return entry;
        }

        public AudioRoot CreateRoot(IAudioClipProvider clips = null)
        {
            Root = new AudioRoot(Settings, clips);
            _disposables.Add(Root);
            return Root;
        }

        public T Track<T>(T disposable) where T : IDisposable
        {
            _disposables.Add(disposable);
            return disposable;
        }

        public void Dispose()
        {
            for (var i = _disposables.Count - 1; i >= 0; i--) _disposables[i].Dispose();
            foreach (var o in _created) Object.DestroyImmediate(o);
        }
    }

    public sealed class AudioClipProviderTests
    {
        private AudioTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new AudioTestRig();

        [TearDown]
        public void TearDown() => _rig.Dispose();

        private SePlayer CreateSe(IAudioClipProvider clips = null) =>
            _rig.Track(new SePlayer(_rig.CreateRoot(clips), _rig.Settings, _rig.Volume));

        private static void Tick(ITickable tickable) => tickable.Tick();

        private AudioSource[] Sources() => _rig.Root.Transform.GetComponentsInChildren<AudioSource>(includeInactive: true);

        [Test]
        public void DirectProvider_IsDefault_AndPlaysSynchronously()
        {
            var entry = _rig.Add(SoundCategory.Se, "hit");
            var player = CreateSe();

            Assert.That(_rig.Root.Clips, Is.SameAs(DirectAudioClipProvider.Instance));
            player.Play("hit");
            var source = _rig.Root.Transform.GetComponentInChildren<AudioSource>();
            Assert.That(source.clip, Is.SameAs(entry.Clips[0]));
        }

        [Test]
        public void DirectProvider_AddressOnlyEntry_ReturnsNullWithWarning()
        {
            var entry = new SoundEntry { Key = "remote", Address = "Audio/Remote" };
            Assert.That(DirectAudioClipProvider.Instance.TryAcquire(entry, out _), Is.False);
            Assert.That(DirectAudioClipProvider.Instance.IsLoaded(entry), Is.False);

            LogAssert.Expect(LogType.Warning, new Regex("UseAddressableClips"));
            var clip = DirectAudioClipProvider.Instance.AcquireAsync(entry).GetAwaiter().GetResult();
            Assert.That(clip, Is.Null);
        }

        [Test]
        public void SoundEntry_SubtitleKey_FallsBackToKey()
        {
            Assert.That(new SoundEntry { Key = "a" }.GetSubtitleKey(), Is.EqualTo("a"));
            Assert.That(new SoundEntry { Key = "a", SubtitleKey = "sub.a" }.GetSubtitleKey(), Is.EqualTo("sub.a"));
            Assert.That(new SoundEntry { Key = "a", Address = "x" }.HasSource, Is.True);
            Assert.That(new SoundEntry { Key = "a" }.HasSource, Is.False);
        }

        [Test]
        public void Se_LoadedClip_HoldsOneRef_UntilStopped()
        {
            var fake = new FakeClipProvider();
            var entry = _rig.Add(SoundCategory.Se, "hit");
            fake.Loaded.Add(entry);
            var player = CreateSe(fake);

            player.Play("hit");
            player.Play("hit");
            Assert.That(fake.RefCount(entry), Is.EqualTo(2));

            player.StopAll();
            Assert.That(fake.RefCount(entry), Is.EqualTo(0));
        }

        [Test]
        public void Se_NotLoaded_LoadThenPlay_PlaysAfterLoad()
        {
            var fake = new FakeClipProvider();
            var entry = _rig.Add(SoundCategory.Se, "hit");
            _rig.Settings.SeLoadTimeout = 0f; // 時間切れにしない
            var player = CreateSe(fake);

            player.Play("hit");
            Assert.That(fake.IsPending(entry), Is.True);
            // 読み込み中は Tick で返却されない
            Tick(player);
            Assert.That(Sources()[0].gameObject.activeSelf, Is.True);

            fake.Complete(entry);
            Assert.That(fake.RefCount(entry), Is.EqualTo(1));
            Assert.That(Sources()[0].clip, Is.SameAs(entry.Clips[0]));

            player.StopAll();
            Assert.That(fake.RefCount(entry), Is.EqualTo(0));
        }

        [Test]
        public void Se_StopAllWhileLoading_DoesNotPlay_AndReleasesRef()
        {
            var fake = new FakeClipProvider();
            var entry = _rig.Add(SoundCategory.Se, "hit");
            _rig.Settings.SeLoadTimeout = 0f;
            var player = CreateSe(fake);

            player.Play("hit");
            player.StopAll();
            fake.Complete(entry);

            Assert.That(fake.RefCount(entry), Is.EqualTo(0));
            foreach (var source in Sources()) Assert.That(source.clip, Is.Null);
        }

        [Test]
        public void Se_NotLoaded_SkipPolicy_WarnsOnce()
        {
            var fake = new FakeClipProvider();
            var entry = _rig.Add(SoundCategory.Se, "hit");
            _rig.Settings.SeNotLoaded = SeNotLoadedPolicy.Skip;
            var player = CreateSe(fake);

            var warnings = 0;
            void OnLog(string message, string _, LogType type)
            {
                if (type == LogType.Warning && message.Contains("hit")) warnings++;
            }
            Application.logMessageReceived += OnLog;
            try
            {
                player.Play("hit");
                player.Play("hit"); // 2 回目は警告しない
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            Assert.That(warnings, Is.EqualTo(1));

            Assert.That(fake.IsPending(entry), Is.False);
            Assert.That(fake.RefCount(entry), Is.EqualTo(0));
            Assert.That(Sources(), Is.Empty);
        }

        [Test]
        public void Bgm_Switch_ReleasesPreviousClip()
        {
            var fake = new FakeClipProvider();
            var a = _rig.Add(SoundCategory.Bgm, "a");
            var b = _rig.Add(SoundCategory.Bgm, "b");
            fake.Loaded.Add(a);
            fake.Loaded.Add(b);
            var player = _rig.Track(new BgmPlayer(_rig.CreateRoot(fake), _rig.Settings, _rig.Volume));

            player.PlayAsync("a", 0f).Forget();
            Assert.That(fake.RefCount(a), Is.EqualTo(1));
            player.PlayAsync("b", 0f).Forget();
            Assert.That(fake.RefCount(a), Is.EqualTo(0));
            Assert.That(fake.RefCount(b), Is.EqualTo(1));

            player.StopAsync(0f).Forget();
            Assert.That(fake.RefCount(b), Is.EqualTo(0));
        }

        [Test]
        public void Bgm_NewRequestWhileLoading_DiscardsLoadedClip()
        {
            var fake = new FakeClipProvider();
            var a = _rig.Add(SoundCategory.Bgm, "a");
            var b = _rig.Add(SoundCategory.Bgm, "b");
            fake.Loaded.Add(b);
            var player = _rig.Track(new BgmPlayer(_rig.CreateRoot(fake), _rig.Settings, _rig.Volume));

            player.PlayAsync("a", 0f).Forget();
            player.PlayAsync("b", 0f).Forget();
            fake.Complete(a);

            Assert.That(player.CurrentKey.CurrentValue, Is.EqualTo("b"));
            Assert.That(fake.RefCount(a), Is.EqualTo(0));
            Assert.That(fake.RefCount(b), Is.EqualTo(1));
        }

        [Test]
        public void Voice_Replace_ReleasesPreviousClip()
        {
            var fake = new FakeClipProvider();
            var a = _rig.Add(SoundCategory.Voice, "a");
            var b = _rig.Add(SoundCategory.Voice, "b");
            fake.Loaded.Add(a);
            fake.Loaded.Add(b);
            var player = _rig.Track(new VoicePlayer(_rig.CreateRoot(fake), _rig.Settings, _rig.Volume));

            player.Play("a");
            player.Play("b");
            Assert.That(fake.RefCount(a), Is.EqualTo(0));
            Assert.That(fake.RefCount(b), Is.EqualTo(1));
            player.Stop();
            Assert.That(fake.RefCount(b), Is.EqualTo(0));
        }

        [Test]
        public void SoundService_Preload_PassesCategoryEntries()
        {
            var fake = new FakeClipProvider();
            var hit = _rig.Add(SoundCategory.Se, "hit");
            var jump = _rig.Add(SoundCategory.Se, "jump");
            _rig.Add(SoundCategory.Bgm, "title");
            var service = new SoundService(null, null, null, null, _rig.Volume, _rig.CreateRoot(fake));

            service.PreloadAsync(SoundCategory.Se).Forget();
            Assert.That(fake.Preloaded, Is.EquivalentTo(new[] { hit, jump }));

            service.Unload(SoundCategory.Se, new[] { "jump" });
            Assert.That(fake.Unloaded, Is.EquivalentTo(new[] { jump }));
            Assert.That(service.Clips, Is.SameAs(fake));
        }
    }

    public sealed class AudioVoiceEventTests
    {
        private AudioTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new AudioTestRig();

        [TearDown]
        public void TearDown() => _rig.Dispose();

        [Test]
        public void StartedAndEnded_AreNotified_WithSubtitleKeyAndDuration()
        {
            _rig.Add(SoundCategory.Voice, "a", subtitleKey: "sub.a");
            _rig.Add(SoundCategory.Voice, "b");
            var player = _rig.Track(new VoicePlayer(_rig.CreateRoot(), _rig.Settings, _rig.Volume));
            var log = new List<string>();
            using var started = player.OnVoiceStarted.Subscribe(e => log.Add($"start {e.Key} {e.SubtitleKey} {e.Duration:0.0}"));
            using var ended = player.OnVoiceEnded.Subscribe(e => log.Add($"end {e.Key} {e.Interrupted}"));

            player.Play("a");
            player.Play("b");
            player.Stop();

            Assert.That(log, Is.EqualTo(new[]
            {
                "start a sub.a 1.0",
                "end a True",
                "start b b 1.0",
                "end b True",
            }));
        }

        [Test]
        public void Ended_IsNotified_WhenPlaybackFinishes()
        {
            _rig.Add(SoundCategory.Voice, "a");
            var player = _rig.Track(new VoicePlayer(_rig.CreateRoot(), _rig.Settings, _rig.Volume));
            VoiceEvent? ended = null;
            using var subscription = player.OnVoiceEnded.Subscribe(e => ended = e);

            player.Play("a");
            // EditMode では音声が進まないため、再生終了を Stop 相当の状態 (AudioSource 停止) で再現する
            _rig.Root.Transform.GetComponentInChildren<AudioSource>().Stop();
            ((ITickable)player).Tick();

            Assert.That(ended.HasValue, Is.True);
            Assert.That(ended.Value.Key, Is.EqualTo("a"));
            Assert.That(ended.Value.Interrupted, Is.False);
            Assert.That(player.IsPlaying, Is.False);
        }

        [Test]
        public void VoiceLoadedLater_StartsAfterLoad()
        {
            var fake = new FakeClipProvider();
            var a = _rig.Add(SoundCategory.Voice, "a");
            var player = _rig.Track(new VoicePlayer(_rig.CreateRoot(fake), _rig.Settings, _rig.Volume));
            var startedCount = 0;
            using var subscription = player.OnVoiceStarted.Subscribe(_ => startedCount++);

            player.Play("a");
            Assert.That(startedCount, Is.EqualTo(0));
            Assert.That(player.IsPlaying, Is.False);

            fake.Complete(a);
            Assert.That(startedCount, Is.EqualTo(1));
            Assert.That(player.CurrentKey.CurrentValue, Is.EqualTo("a"));
            Assert.That(fake.RefCount(a), Is.EqualTo(1));
        }
    }

    public sealed class AudioPlayAttachedTests
    {
        private AudioTestRig _rig;
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _rig = new AudioTestRig();
            _target = new GameObject("Target");
        }

        [TearDown]
        public void TearDown()
        {
            if (_target != null) Object.DestroyImmediate(_target);
            _rig.Dispose();
        }

        [Test]
        public void Stop_Handle_ReleasesSourceAndRef()
        {
            var fake = new FakeClipProvider();
            var entry = _rig.Add(SoundCategory.Se, "engine");
            fake.Loaded.Add(entry);
            var player = _rig.Track(new SePlayer(_rig.CreateRoot(fake), _rig.Settings, _rig.Volume));
            _target.transform.position = new Vector3(1, 2, 3);

            var handle = player.PlayAttached("engine", _target.transform);
            Assert.That(handle.IsValid, Is.True);
            Assert.That(player.IsPlaying(handle), Is.True);
            var source = _rig.Root.Transform.GetComponentInChildren<AudioSource>();
            Assert.That(source.transform.position, Is.EqualTo(new Vector3(1, 2, 3)));

            // 追従: LateTick で位置が合う
            _target.transform.position = new Vector3(4, 5, 6);
            ((ILateTickable)player).LateTick();
            Assert.That(source.transform.position, Is.EqualTo(new Vector3(4, 5, 6)));

            player.Stop(handle);
            Assert.That(player.IsPlaying(handle), Is.False);
            Assert.That(fake.RefCount(entry), Is.EqualTo(0));
            Assert.That(source.gameObject.activeSelf, Is.False);
            Assert.DoesNotThrow(() => player.Stop(handle)); // 2 回目は何もしない
        }

        [Test]
        public void DestroyedTarget_ReturnsSourceToPool()
        {
            var fake = new FakeClipProvider();
            var entry = _rig.Add(SoundCategory.Se, "engine");
            _rig.Settings.SeLoadTimeout = 0f;
            var player = _rig.Track(new SePlayer(_rig.CreateRoot(fake), _rig.Settings, _rig.Volume));

            // 読み込み中の SE は Tick で返却されないので、追従先の破棄だけが返却の理由になる
            var handle = player.PlayAttached("engine", _target.transform);
            ((ITickable)player).Tick();
            Assert.That(player.IsPlaying(handle), Is.True);

            Object.DestroyImmediate(_target);
            ((ITickable)player).Tick();
            Assert.That(player.IsPlaying(handle), Is.False);
            var source = _rig.Root.Transform.GetComponentInChildren<AudioSource>(includeInactive: true);
            Assert.That(source, Is.Not.Null, "AudioSource 自体は破棄されずプールに残る");
            Assert.That(source.gameObject.activeSelf, Is.False);

            // 後から読み込みが終わっても鳴らさず、参照も返す
            fake.Complete(entry);
            Assert.That(fake.RefCount(entry), Is.EqualTo(0));
            Assert.That(source.clip, Is.Null);
        }

        [Test]
        public void NullTarget_ReturnsInvalidHandle()
        {
            _rig.Add(SoundCategory.Se, "engine");
            var player = _rig.Track(new SePlayer(_rig.CreateRoot(), _rig.Settings, _rig.Volume));
            LogAssert.Expect(LogType.Warning, new Regex("engine"));
            var handle = player.PlayAttached("engine", null);
            Assert.That(handle.IsValid, Is.False);
            Assert.That(player.IsPlaying(handle), Is.False);
        }
    }

    public sealed class AudioMixerSnapshotTests
    {
        [Test]
        public void WithoutMixer_IsNoOp()
        {
            var service = new AudioMixerSnapshotService(new SoundSettings());
            Assert.That(service.IsAvailable, Is.False);
            Assert.That(service.TransitionTo("Paused", 0.2f), Is.False);
            Assert.DoesNotThrow(() => service.Push("Paused").Dispose());
            Assert.DoesNotThrow(() => service.ResetSnapshot());
            Assert.That(service.Current, Is.Null);
        }
    }
}
