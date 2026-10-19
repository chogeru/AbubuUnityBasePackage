using System.Collections.Generic;
using Abubu.DisplaySettings;
using Abubu.Save;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace Abubu.Tests
{
    public sealed class GraphicsSettingsModelTests
    {
        internal static GraphicsPlatformInfo CreatePlatform(bool supportsResolution = true) => new()
        {
            SupportsResolution = supportsResolution,
            Resolutions = new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) },
            QualityNames = new[] { "Low", "Medium", "High" },
            CurrentResolution = new Vector2Int(1920, 1080),
            CurrentFullScreenMode = FullScreenMode.FullScreenWindow,
            CurrentQualityLevel = 1,
            CurrentVSync = false,
            CurrentTargetFrameRate = -1,
        };

        [Test]
        public void InitialValues_ComeFromPlatform_AndAreNotExplicit()
        {
            using var model = new GraphicsSettingsModel(null, CreatePlatform());

            Assert.That(model.Resolution.CurrentValue, Is.EqualTo(new Vector2Int(1920, 1080)));
            Assert.That(model.QualityLevel.CurrentValue, Is.EqualTo(1));
            Assert.That(model.VSync.CurrentValue, Is.False);
            Assert.That(model.TargetFrameRate.CurrentValue, Is.EqualTo(-1));
            foreach (GraphicsSettingKind kind in System.Enum.GetValues(typeof(GraphicsSettingKind)))
            {
                Assert.That(model.IsExplicit(kind), Is.False, kind.ToString());
            }
            Assert.That(model.ToData().ExplicitMask, Is.EqualTo(0));
        }

        [Test]
        public void InvalidValues_AreRejected()
        {
            using var model = new GraphicsSettingsModel(null, CreatePlatform());

            Assert.That(model.SetQualityLevel(3), Is.False);
            Assert.That(model.SetQualityLevel(-1), Is.False);
            Assert.That(model.SetTargetFrameRate(0), Is.False);
            Assert.That(model.SetTargetFrameRate(-2), Is.False);
            Assert.That(model.SetResolution(new Vector2Int(0, 600)), Is.False);
            Assert.That(model.SetFullScreenMode((FullScreenMode)99), Is.False);

            Assert.That(model.QualityLevel.CurrentValue, Is.EqualTo(1));
            Assert.That(model.IsExplicit(GraphicsSettingKind.QualityLevel), Is.False);
            Assert.That(model.IsExplicit(GraphicsSettingKind.TargetFrameRate), Is.False);
        }

        [Test]
        public void Resolution_IsRejected_WhenPlatformDoesNotSupportIt()
        {
            using var model = new GraphicsSettingsModel(null, CreatePlatform(supportsResolution: false));

            Assert.That(model.SetResolution(new Vector2Int(1280, 720)), Is.False);
            Assert.That(model.SetFullScreenMode(FullScreenMode.Windowed), Is.False);
            Assert.That(model.SetQualityLevel(2), Is.True);
        }

        [Test]
        public void ValueNotInOptions_IsInserted()
        {
            var options = new GraphicsSettingsOptions { FrameRateOptions = new List<int> { 30, 60, -1 } };
            using var model = new GraphicsSettingsModel(options, CreatePlatform());
            var before = model.FrameRateOptions;

            Assert.That(model.SetTargetFrameRate(75), Is.True);
            Assert.That(model.FrameRateOptions, Is.EqualTo(new[] { 30, 60, 75, -1 }));
            Assert.That(model.FrameRateOptions, Is.Not.SameAs(before), "選択肢が増えたら参照が変わる");

            Assert.That(model.SetResolution(new Vector2Int(1600, 900)), Is.True);
            Assert.That(model.ResolutionOptions, Is.EqualTo(new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) }));

            Assert.That(model.SetFullScreenMode(FullScreenMode.ExclusiveFullScreen), Is.True);
            Assert.That(model.WindowModeOptions, Does.Contain(FullScreenMode.ExclusiveFullScreen));
        }

        [Test]
        public void CurrentValue_IsAlwaysInOptions()
        {
            var platform = CreatePlatform();
            platform.CurrentTargetFrameRate = 50;
            platform.CurrentResolution = new Vector2Int(1000, 700);
            using var model = new GraphicsSettingsModel(new GraphicsSettingsOptions { FrameRateOptions = new List<int> { 60 } }, platform);

            Assert.That(model.FrameRateOptions, Does.Contain(50));
            Assert.That(model.ResolutionOptions, Does.Contain(new Vector2Int(1000, 700)));
        }

        [Test]
        public void Changed_IsRaised_OnlyWhenValueOrExplicitStateChanges()
        {
            using var model = new GraphicsSettingsModel(null, CreatePlatform());
            var changes = new List<GraphicsSettingKind>();
            using var _ = model.Changed.Subscribe(changes.Add);

            model.SetQualityLevel(1); // 同じ値だが未設定 → 設定済み
            model.SetQualityLevel(1); // 変化なし
            model.SetQualityLevel(2);

            Assert.That(changes, Is.EqualTo(new[] { GraphicsSettingKind.QualityLevel, GraphicsSettingKind.QualityLevel }));
        }

        [Test]
        public void Restore_OnlyAppliesMaskedFields()
        {
            using var model = new GraphicsSettingsModel(null, CreatePlatform());
            model.Restore(new GraphicsSettingsData
            {
                QualityLevel = 2,
                VSync = true,
                TargetFrameRate = 30,
                ExplicitMask = 1 << (int)GraphicsSettingKind.QualityLevel,
            });

            Assert.That(model.QualityLevel.CurrentValue, Is.EqualTo(2));
            Assert.That(model.IsExplicit(GraphicsSettingKind.QualityLevel), Is.True);
            Assert.That(model.VSync.CurrentValue, Is.False);
            Assert.That(model.IsExplicit(GraphicsSettingKind.VSync), Is.False);
            Assert.That(model.TargetFrameRate.CurrentValue, Is.EqualTo(-1));
        }

        [Test]
        public void Restore_IgnoresInvalidSavedValues()
        {
            using var model = new GraphicsSettingsModel(null, CreatePlatform());
            model.Restore(new GraphicsSettingsData { Width = 0, Height = 0, QualityLevel = 10, VSync = true, TargetFrameRate = 0 });

            Assert.That(model.IsExplicit(GraphicsSettingKind.Resolution), Is.False);
            Assert.That(model.IsExplicit(GraphicsSettingKind.QualityLevel), Is.False);
            Assert.That(model.IsExplicit(GraphicsSettingKind.TargetFrameRate), Is.False);
            Assert.That(model.IsExplicit(GraphicsSettingKind.VSync), Is.True);
        }

        [Test]
        public void LegacyJson_WithoutMask_IsTreatedAsAllExplicit()
        {
            var data = JsonUtility.FromJson<GraphicsSettingsData>("{\"Width\":800,\"Height\":600,\"QualityLevel\":0,\"VSync\":false,\"TargetFrameRate\":60}");
            Assert.That(data.ExplicitMask, Is.EqualTo(-1));
        }
    }

    public sealed class GraphicsSettingsStoreTests
    {
        private static ISaveService CreateSave() => new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer());

        [Test]
        public void Save_WritesOnlyExplicitFields_AndInitializeRestoresThem()
        {
            var save = CreateSave();
            var options = new GraphicsSettingsOptions();

            using (var model = new GraphicsSettingsModel(options, GraphicsSettingsModelTests.CreatePlatform()))
            {
                var store = new GraphicsSettingsStore(model, save);
                store.Initialize();
                model.SetTargetFrameRate(30);
                store.Save();
                store.Dispose();
            }

            Assert.That(save.TryLoad<GraphicsSettingsData>(options.SaveKey, out var data), Is.True);
            Assert.That(data.Has(GraphicsSettingKind.TargetFrameRate), Is.True);
            Assert.That(data.Has(GraphicsSettingKind.QualityLevel), Is.False);

            using var restored = new GraphicsSettingsModel(options, GraphicsSettingsModelTests.CreatePlatform());
            var restoredStore = new GraphicsSettingsStore(restored, save);
            restoredStore.Initialize();
            Assert.That(restored.TargetFrameRate.CurrentValue, Is.EqualTo(30));
            Assert.That(restored.IsExplicit(GraphicsSettingKind.TargetFrameRate), Is.True);
            Assert.That(restored.IsExplicit(GraphicsSettingKind.QualityLevel), Is.False);
            restoredStore.Dispose();
        }

        [Test]
        public void NothingExplicit_IsNotSaved()
        {
            var save = CreateSave();
            using var model = new GraphicsSettingsModel(null, GraphicsSettingsModelTests.CreatePlatform());
            var store = new GraphicsSettingsStore(model, save);
            store.Initialize();
            store.Save();
            store.Dispose();

            Assert.That(save.Exists(model.Options.SaveKey), Is.False);
        }

        [Test]
        public void RequestSave_SavesImmediately_AndCustomKeyIsUsed()
        {
            var save = CreateSave();
            var options = new GraphicsSettingsOptions { SaveKey = "custom.graphics" };
            using var model = new GraphicsSettingsModel(options, GraphicsSettingsModelTests.CreatePlatform());
            var store = new GraphicsSettingsStore(model, save);
            store.Initialize();
            model.SetVSync(true);
            model.RequestSave();

            Assert.That(save.Exists("custom.graphics"), Is.True);
            store.Dispose();
        }

        [Test]
        public void Persist_False_DoesNotLoadOrSave()
        {
            var save = CreateSave();
            save.Save("abubu.graphics", new GraphicsSettingsData { QualityLevel = 2 });
            var options = new GraphicsSettingsOptions { Persist = false };
            using var model = new GraphicsSettingsModel(options, GraphicsSettingsModelTests.CreatePlatform());
            var store = new GraphicsSettingsStore(model, save);
            store.Initialize();

            Assert.That(model.QualityLevel.CurrentValue, Is.EqualTo(1));
            store.Dispose();
        }
    }

    public sealed class GraphicsSettingsApplierTests
    {
        /// <summary>Unity に反映する代わりに呼び出しを記録する Applier</summary>
        private sealed class RecordingApplier : GraphicsSettingsApplier
        {
            public readonly List<string> Calls = new();
            public RecordingApplier(GraphicsSettingsModel model) : base(model) { }
            protected override void ApplyResolution(Vector2Int resolution, FullScreenMode mode) => Calls.Add($"res:{resolution.x}x{resolution.y}:{mode}");
            protected override void ApplyQualityLevel(int level) => Calls.Add($"quality:{level}");
            protected override void ApplyVSync(bool enabled) => Calls.Add($"vsync:{enabled}");
            protected override void ApplyTargetFrameRate(int frameRate) => Calls.Add($"fps:{frameRate}");
        }

        [Test]
        public void Initialize_DoesNotForceDefaults_WhenNothingIsExplicit()
        {
            using var model = new GraphicsSettingsModel(null, GraphicsSettingsModelTests.CreatePlatform());
            var applier = new RecordingApplier(model);
            applier.Initialize();

            Assert.That(applier.Calls, Is.Empty);
            applier.Dispose();
        }

        [Test]
        public void QualityChange_ReappliesVSync_OnlyWhenVSyncIsExplicit()
        {
            using var model = new GraphicsSettingsModel(null, GraphicsSettingsModelTests.CreatePlatform());
            var applier = new RecordingApplier(model);
            applier.Initialize();

            model.SetQualityLevel(2);
            Assert.That(applier.Calls, Is.EqualTo(new[] { "quality:2" }));

            applier.Calls.Clear();
            model.SetVSync(true);
            model.SetQualityLevel(0);
            Assert.That(applier.Calls, Is.EqualTo(new[] { "vsync:True", "quality:0", "vsync:True" }));
            applier.Dispose();
        }

        [Test]
        public void Initialize_AppliesExplicitValues()
        {
            using var model = new GraphicsSettingsModel(null, GraphicsSettingsModelTests.CreatePlatform());
            model.SetTargetFrameRate(60);
            model.SetFullScreenMode(FullScreenMode.Windowed);
            var applier = new RecordingApplier(model);
            applier.Initialize();

            Assert.That(applier.Calls, Is.EqualTo(new[] { "fps:60", "res:1920x1080:Windowed" }));
            applier.Dispose();
        }
    }
}
