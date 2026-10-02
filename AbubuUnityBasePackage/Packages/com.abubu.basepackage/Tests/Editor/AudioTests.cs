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
        [Test]
        public void Effective_IsMasterTimesCategory_AndMuteIsZero()
        {
            using var model = new AudioVolumeModel(new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer()));
            model.Master.Value = 0.5f;
            model.Bgm.Value = 0.5f;
            Assert.That(model.EffectiveBgm.CurrentValue, Is.EqualTo(0.25f).Within(1e-5));

            model.Mute.Value = true;
            Assert.That(model.EffectiveBgm.CurrentValue, Is.EqualTo(0f));
        }

        [Test]
        public void Defaults_Used_WhenNoSave_AndSavedValues_Restored()
        {
            var storage = new MemorySaveStorage();
            var save = new SaveService(storage, new JsonUtilitySaveSerializer());

            var model = new AudioVolumeModel(save, new AudioVolumeDefaults { Bgm = 0.3f });
            Assert.That(model.Bgm.Value, Is.EqualTo(0.3f).Within(1e-5));
            model.Bgm.Value = 0.9f;
            model.Dispose(); // Dispose 時に保存される

            using var restored = new AudioVolumeModel(save, new AudioVolumeDefaults { Bgm = 0.3f });
            Assert.That(restored.Bgm.Value, Is.EqualTo(0.9f).Within(1e-5));
        }

        [TestCase(1f, 0f)]
        [TestCase(0.5f, -6.0206f)]
        [TestCase(0f, -80f)]
        public void LinearToDecibel(float linear, float expected) =>
            Assert.That(AudioMixerController.LinearToDecibel(linear), Is.EqualTo(expected).Within(1e-3));
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
}
