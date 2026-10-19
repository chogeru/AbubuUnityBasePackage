using System.Collections.Generic;
using System.Reflection;
using Abubu.Audio;
using Abubu.Save;
using NUnit.Framework;
using UnityEngine;

namespace Abubu.Tests
{
    public sealed class SoundLibraryTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private SoundLibrary CreateLibrary(string clipName, params SoundLibrary[] includes)
        {
            var library = ScriptableObject.CreateInstance<SoundLibrary>();
            var clip = AudioClip.Create(clipName, 10, 1, 22050, false);
            _created.Add(library);
            _created.Add(clip);

            SetField(library, "se", new List<SoundEntry> { new() { Key = "hit", Clips = new[] { clip } } });
            SetField(library, "includes", new List<SoundLibrary>(includes));
            return library;
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        [Test]
        public void OwnEntry_WinsOverIncluded()
        {
            var common = CreateLibrary("common");
            var stage = CreateLibrary("stage", common);

            Assert.That(stage.TryGet(SoundCategory.Se, "hit", out var entry), Is.True);
            Assert.That(entry.PickClip().name, Is.EqualTo("stage"));
        }

        [Test]
        public void IncludedEntry_IsFound()
        {
            var common = CreateLibrary("common");
            var stage = ScriptableObject.CreateInstance<SoundLibrary>();
            _created.Add(stage);
            SetField(stage, "includes", new List<SoundLibrary> { common });

            Assert.That(stage.TryGet(SoundCategory.Se, "hit", out var entry), Is.True);
            Assert.That(entry.PickClip().name, Is.EqualTo("common"));
        }

        [Test]
        public void CyclicIncludes_DoNotHang()
        {
            var a = CreateLibrary("a");
            var b = CreateLibrary("b", a);
            SetField(a, "includes", new List<SoundLibrary> { b });

            Assert.That(a.TryGet(SoundCategory.Se, "hit", out _), Is.True);
            Assert.That(a.TryGet(SoundCategory.Bgm, "hit", out _), Is.False);
        }
    }

    public sealed class AudioVolumeTests
    {
        private static SaveService CreateSave(MemorySaveStorage storage = null) =>
            new(storage ?? new MemorySaveStorage(), new JsonUtilitySaveSerializer());

        [Test]
        public void Effective_IsMasterTimesCategory_AndMuteIsZero()
        {
            using var model = new AudioVolumeModel(CreateSave());
            model.Set(VolumeChannel.Master, 0.5f);
            model.Set(VolumeChannel.Bgm, 0.5f);
            Assert.That(model.EffectiveBgm.CurrentValue, Is.EqualTo(0.25f).Within(1e-5));

            model.SetMute(true);
            Assert.That(model.Mute.CurrentValue, Is.True);
            Assert.That(model.EffectiveBgm.CurrentValue, Is.EqualTo(0f));

            model.SetMute(false);
            Assert.That(model.EffectiveBgm.CurrentValue, Is.EqualTo(0.25f).Within(1e-5));
        }

        [TestCase(1.5f, 1f)]
        [TestCase(-0.5f, 0f)]
        [TestCase(0.3f, 0.3f)]
        public void Set_ClampsToZeroOne(float input, float expected)
        {
            using var model = new AudioVolumeModel(CreateSave());
            foreach (VolumeChannel channel in System.Enum.GetValues(typeof(VolumeChannel)))
            {
                model.Set(channel, input);
                Assert.That(model.Get(channel).CurrentValue, Is.EqualTo(expected).Within(1e-5), channel.ToString());
            }
        }

        [Test]
        public void Get_ReturnsSamePropertyAsNamedAccessor()
        {
            using var model = new AudioVolumeModel(CreateSave());
            Assert.That(model.Get(VolumeChannel.Master), Is.SameAs(model.Master));
            Assert.That(model.Get(VolumeChannel.Bgm), Is.SameAs(model.Bgm));
            Assert.That(model.Get(VolumeChannel.Se), Is.SameAs(model.Se));
            Assert.That(model.Get(VolumeChannel.Ambient), Is.SameAs(model.Ambient));
            Assert.That(model.Get(VolumeChannel.Voice), Is.SameAs(model.Voice));
        }

        [Test]
        public void Defaults_Used_WhenNoSave_AndSavedValues_Restored()
        {
            var save = CreateSave();

            var model = new AudioVolumeModel(save, new AudioVolumeDefaults { Bgm = 0.3f });
            Assert.That(model.Bgm.CurrentValue, Is.EqualTo(0.3f).Within(1e-5));
            model.Set(VolumeChannel.Bgm, 0.9f);
            model.SetMute(true);
            model.Dispose(); // Dispose 時に保存される

            using var restored = new AudioVolumeModel(save, new AudioVolumeDefaults { Bgm = 0.3f });
            Assert.That(restored.Bgm.CurrentValue, Is.EqualTo(0.9f).Within(1e-5));
            Assert.That(restored.Mute.CurrentValue, Is.True);
        }

        [Test]
        public void SaveKey_IsConfigurable()
        {
            var save = CreateSave();
            var options = new AudioVolumeSaveOptions { SaveKey = "custom.volume" };

            var model = new AudioVolumeModel(save, null, options);
            model.Set(VolumeChannel.Se, 0.2f);
            model.Save();
            Assert.That(save.Exists("custom.volume"), Is.True);
            Assert.That(save.Exists(AudioVolumeModel.DefaultSaveKey), Is.False);
            model.Dispose();
        }

        [Test]
        public void Reload_ReadsLatestSavedValues()
        {
            var save = CreateSave();
            using var model = new AudioVolumeModel(save);

            // 別経路 (クラウド同期やスロット切り替え) で保存データが差し替わった想定
            save.Save(AudioVolumeModel.DefaultSaveKey, new AudioVolumeData { Master = 0.4f, Bgm = 2f, Mute = true });
            model.Reload();

            Assert.That(model.Master.CurrentValue, Is.EqualTo(0.4f).Within(1e-5));
            Assert.That(model.Bgm.CurrentValue, Is.EqualTo(1f).Within(1e-5)); // 範囲外は丸められる
            Assert.That(model.Mute.CurrentValue, Is.True);
        }

        [Test]
        public void ModelWithoutStore_StartsWithDefaults_AndSaveReloadAreNoOp()
        {
            using var model = new AudioVolumeModel(new AudioVolumeDefaults { Bgm = 0.3f });
            IAudioVolumeModel volume = model;
            Assert.That(volume.Get(VolumeChannel.Bgm).CurrentValue, Is.EqualTo(0.3f).Within(1e-5));
            Assert.That(model.CurrentSaveKey, Is.EqualTo(AudioVolumeModel.DefaultSaveKey));

            volume.Set(VolumeChannel.Bgm, 0.9f);
            Assert.DoesNotThrow(() => model.Save());
            model.Reload();
            Assert.That(model.Bgm.CurrentValue, Is.EqualTo(0.9f).Within(1e-5));

            volume.ResetToDefaults();
            Assert.That(model.Bgm.CurrentValue, Is.EqualTo(0.3f).Within(1e-5));
        }

        [Test]
        public void Store_RestoresOnInitialize_AndSavesChanges()
        {
            var save = CreateSave();
            save.Save(AudioVolumeModel.DefaultSaveKey, new AudioVolumeData { Master = 0.4f, Bgm = 0.6f });
            using var model = new AudioVolumeModel();
            var store = new AudioVolumeStore(model, save, new AudioVolumeSaveOptions { SaveDebounceSeconds = 0f });

            store.Initialize();
            Assert.That(model.Master.CurrentValue, Is.EqualTo(0.4f).Within(1e-5));
            Assert.That(model.Bgm.CurrentValue, Is.EqualTo(0.6f).Within(1e-5));
            Assert.That(store.IsDirty, Is.False, "復元は変更として保存しない");

            model.Set(VolumeChannel.Se, 0.2f); // 待ち時間 0 なので即保存
            Assert.That(save.Load<AudioVolumeData>(AudioVolumeModel.DefaultSaveKey).Se, Is.EqualTo(0.2f).Within(1e-5));
            store.Dispose();
        }

        [Test]
        public void Store_FlushesPendingChange_OnDispose_AndReloadDiscardsIt()
        {
            var save = CreateSave();
            var model = new AudioVolumeModel();
            var store = new AudioVolumeStore(model, save, new AudioVolumeSaveOptions { SaveDebounceSeconds = 10f });
            store.Initialize();

            model.Set(VolumeChannel.Bgm, 0.1f);
            Assert.That(store.IsDirty, Is.True);
            store.Reload(); // セーブが無いので既定値に戻り、保存待ちの変更は捨てる
            Assert.That(store.IsDirty, Is.False);
            Assert.That(model.Bgm.CurrentValue, Is.EqualTo(new AudioVolumeDefaults().Bgm).Within(1e-5));
            Assert.That(save.Exists(AudioVolumeModel.DefaultSaveKey), Is.False);

            model.Set(VolumeChannel.Bgm, 0.25f);
            model.Save(); // Model の Save は Store に転送される
            Assert.That(store.IsDirty, Is.False);
            model.Set(VolumeChannel.Bgm, 0.35f);

            // Model を先に Dispose しても、保存待ちの変更は書き出される (Zenject の破棄順に依存しない)
            model.Dispose();
            store.Dispose();
            Assert.That(save.Load<AudioVolumeData>(AudioVolumeModel.DefaultSaveKey).Bgm, Is.EqualTo(0.35f).Within(1e-5));
        }

        [Test]
        public void Model_AfterDispose_IgnoresChanges()
        {
            var model = new AudioVolumeModel(CreateSave());
            model.Dispose();
            Assert.DoesNotThrow(() => model.Dispose());
            Assert.DoesNotThrow(() => model.Set(VolumeChannel.Bgm, 0.5f));
            Assert.DoesNotThrow(() => model.SetMute(true));
            Assert.DoesNotThrow(() => model.Save());
            Assert.DoesNotThrow(() => model.Reload());
        }

        [Test]
        public void Installer_BindsModelAndStore_AsOneInstance()
        {
            var container = new Zenject.DiContainer();
            var save = CreateSave();
            save.Save("custom.volume", new AudioVolumeData { Voice = 0.5f });
            container.Bind<ISaveService>().FromInstance(save);

            AudioVolumeInstaller.Install(container, new AudioVolumeDefaults { Bgm = 0.3f }, new AudioVolumeSaveOptions { SaveKey = "custom.volume" });

            var model = container.Resolve<AudioVolumeModel>();
            Assert.That(container.Resolve<IAudioVolumeModel>(), Is.SameAs(model));
            var store = container.Resolve<AudioVolumeStore>();
            Assert.That(store.SaveKey, Is.EqualTo("custom.volume"));
            Assert.That(model.CurrentSaveKey, Is.EqualTo("custom.volume"));
            Assert.That(model.Bgm.CurrentValue, Is.EqualTo(0.3f).Within(1e-5), "Initialize 前は既定値");

            store.Initialize();
            Assert.That(model.Voice.CurrentValue, Is.EqualTo(0.5f).Within(1e-5));

            // 2 回目の Install は何もしない (差し替え済みの扱い)
            Assert.DoesNotThrow(() => AudioVolumeInstaller.Install(container));
            store.Dispose();
            model.Dispose();
        }

        [TestCase(1f, 0f)]
        [TestCase(0.5f, -6.0206f)]
        [TestCase(0f, -80f)]
        [TestCase(2f, 0f)]
        [TestCase(-1f, -80f)]
        public void LinearToDecibel(float linear, float expected) =>
            Assert.That(AudioMixerController.LinearToDecibel(linear), Is.EqualTo(expected).Within(1e-3));

        [Test]
        public void LinearToDecibel_UsesMinDecibel() =>
            Assert.That(AudioMixerController.LinearToDecibel(0f, -60f), Is.EqualTo(-60f));
    }

    public sealed class SoundEntryTests
    {
        private static SoundEntry Entry(int clipCount, bool avoidRepeat)
        {
            var clips = new AudioClip[clipCount];
            for (var i = 0; i < clipCount; i++) clips[i] = AudioClip.Create("c" + i, 10, 1, 22050, false);
            return new SoundEntry { Key = "k", Clips = clips, AvoidRepeat = avoidRepeat };
        }

        private static void Destroy(SoundEntry entry)
        {
            foreach (var clip in entry.Clips) Object.DestroyImmediate(clip);
        }

        [Test]
        public void AvoidRepeat_NeverPicksSameClipTwiceInARow()
        {
            var entry = Entry(3, avoidRepeat: true);
            try
            {
                var previous = entry.PickClip();
                for (var i = 0; i < 200; i++)
                {
                    var next = entry.PickClip();
                    Assert.That(next, Is.Not.SameAs(previous));
                    previous = next;
                }
            }
            finally
            {
                Destroy(entry);
            }
        }

        [Test]
        public void SingleClip_AlwaysReturnsIt()
        {
            var entry = Entry(1, avoidRepeat: true);
            try
            {
                Assert.That(entry.PickClip(), Is.SameAs(entry.Clips[0]));
                Assert.That(entry.PickClip(), Is.SameAs(entry.Clips[0]));
            }
            finally
            {
                Destroy(entry);
            }
        }
    }

    public sealed class VoicePlayerTests
    {
        private readonly System.Collections.Generic.List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private VoicePlayer CreatePlayer(out SoundSettings settings)
        {
            var library = ScriptableObject.CreateInstance<SoundLibrary>();
            var clip = AudioClip.Create("voice", 22050, 1, 22050, false);
            _created.Add(library);
            _created.Add(clip);
            typeof(SoundLibrary).GetField("voice", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(library, new System.Collections.Generic.List<SoundEntry>
                {
                    new() { Key = "a", Clips = new[] { clip } },
                    new() { Key = "b", Clips = new[] { clip } },
                });

            settings = new SoundSettings { Library = library };
            var root = new AudioRoot(settings);
            var volume = new AudioVolumeModel(new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer()));
            _created.Add(root.Transform.gameObject);
            return new VoicePlayer(root, settings, volume);
        }

        [Test]
        public void Play_ReplacesPreviousVoice()
        {
            var player = CreatePlayer(out _);
            player.Play("a");
            Assert.That(player.CurrentKey.CurrentValue, Is.EqualTo("a"));
            Assert.That(player.IsPlaying, Is.True);

            player.Play("b");
            Assert.That(player.CurrentKey.CurrentValue, Is.EqualTo("b"));
        }

        [Test]
        public void Stop_ClearsCurrentKey()
        {
            var player = CreatePlayer(out _);
            player.Play("a");
            player.Stop();
            Assert.That(player.CurrentKey.CurrentValue, Is.Null);
            Assert.That(player.IsPlaying, Is.False);
        }

        [Test]
        public void UnknownKey_DoesNothing()
        {
            var player = CreatePlayer(out _);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Voice"));
            player.Play("missing");
            Assert.That(player.IsPlaying, Is.False);
        }
    }

    public sealed class SePlayerTests
    {
        private readonly List<Object> _created = new();
        private readonly List<System.IDisposable> _disposables = new();

        [TearDown]
        public void TearDown()
        {
            for (var i = _disposables.Count - 1; i >= 0; i--) _disposables[i].Dispose();
            _disposables.Clear();
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private SePlayer CreatePlayer(int prewarm, out AudioRoot root)
        {
            var library = ScriptableObject.CreateInstance<SoundLibrary>();
            var clip = AudioClip.Create("se", 22050, 1, 22050, false);
            _created.Add(library);
            _created.Add(clip);
            typeof(SoundLibrary).GetField("se", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(library, new List<SoundEntry> { new() { Key = "hit", Clips = new[] { clip } } });
            library.ClearCache();

            var settings = new SoundSettings { Library = library, SePoolPrewarm = prewarm };
            root = new AudioRoot(settings);
            var volume = new AudioVolumeModel(new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer()));
            var player = new SePlayer(root, settings, volume);
            _disposables.Add(root);
            _disposables.Add(volume);
            _disposables.Add(player);
            return player;
        }

        [Test]
        public void SourcesBeyondPrewarm_AreActiveWhenPlayed()
        {
            var player = CreatePlayer(prewarm: 1, out var root);
            for (var i = 0; i < 4; i++) player.Play("hit");

            var sources = root.Transform.GetComponentsInChildren<AudioSource>(includeInactive: true);
            Assert.That(sources.Length, Is.EqualTo(4));
            foreach (var source in sources)
            {
                Assert.That(source.gameObject.activeSelf, Is.True, source.name);
                Assert.That(source.clip, Is.Not.Null, source.name);
            }
        }

        [Test]
        public void PlayClip_UsesPooledSource()
        {
            var player = CreatePlayer(prewarm: 0, out var root);
            var clip = AudioClip.Create("direct", 22050, 1, 22050, false);
            _created.Add(clip);

            player.Play(clip, 0.5f);
            var source = root.Transform.GetComponentInChildren<AudioSource>();
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.SameAs(clip));
            Assert.That(source.volume, Is.EqualTo(0.5f).Within(1e-5));
        }

        [Test]
        public void Dispose_IsIdempotent_AndStopAllAfterDisposeDoesNotThrow()
        {
            var player = CreatePlayer(prewarm: 2, out _);
            player.Play("hit");
            player.Dispose();
            Assert.DoesNotThrow(() => player.Dispose());
            Assert.DoesNotThrow(() => player.Play("hit"));
        }
    }
}
