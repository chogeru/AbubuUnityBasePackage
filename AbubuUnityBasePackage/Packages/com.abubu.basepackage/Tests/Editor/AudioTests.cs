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
}
