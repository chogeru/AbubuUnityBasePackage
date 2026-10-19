using System.Collections.Generic;
using Abubu.Audio;
using Abubu.DisplaySettings;
using Abubu.Presentation;
using Abubu.Save;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace Abubu.Tests
{
    public sealed class VolumeSettingsPresenterTests
    {
        /// <summary>uGUI を使わない偽の View</summary>
        private sealed class FakeVolumeView : IVolumeSettingsView
        {
            public readonly HashSet<VolumeChannel> Channels = new();
            public readonly Dictionary<VolumeChannel, float> Shown = new();
            public readonly Dictionary<VolumeChannel, Subject<float>> Inputs = new();
            public readonly Subject<bool> MuteInput = new();
            public bool ShownMute;
            public bool HasMuteControl = true;

            public bool HasChannel(VolumeChannel channel) => Channels.Contains(channel);
            public void SetVolume(VolumeChannel channel, float value) => Shown[channel] = value;

            public Observable<float> OnVolumeChanged(VolumeChannel channel)
            {
                if (!Inputs.TryGetValue(channel, out var subject)) Inputs[channel] = subject = new Subject<float>();
                return subject;
            }

            public bool HasMute => HasMuteControl;
            public void SetMute(bool mute) => ShownMute = mute;
            public Observable<bool> OnMuteChanged => MuteInput;
            public string SePreviewKey => null;
        }

        private static AudioVolumeModel CreateModel() =>
            new(new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer()));

        [Test]
        public void ModelToView_AndViewToModel()
        {
            using var model = CreateModel();
            var view = new FakeVolumeView();
            view.Channels.Add(VolumeChannel.Master);
            view.Channels.Add(VolumeChannel.Bgm);
            using var presenter = new VolumeSettingsPresenter(model, view);
            presenter.Initialize();

            Assert.That(view.Shown[VolumeChannel.Master], Is.EqualTo(model.Get(VolumeChannel.Master).CurrentValue));

            model.Set(VolumeChannel.Bgm, 0.25f);
            Assert.That(view.Shown[VolumeChannel.Bgm], Is.EqualTo(0.25f).Within(1e-5));

            view.Inputs[VolumeChannel.Master].OnNext(0.4f);
            Assert.That(model.Get(VolumeChannel.Master).CurrentValue, Is.EqualTo(0.4f).Within(1e-5));

            view.MuteInput.OnNext(true);
            Assert.That(model.Mute.CurrentValue, Is.True);
            model.SetMute(false);
            Assert.That(view.ShownMute, Is.False);
        }

        [Test]
        public void MissingChannels_AreNotBound()
        {
            using var model = CreateModel();
            var view = new FakeVolumeView { HasMuteControl = false };
            view.Channels.Add(VolumeChannel.Se);
            using var presenter = new VolumeSettingsPresenter(model, view);
            presenter.Initialize();

            Assert.That(view.Shown.Keys, Is.EquivalentTo(new[] { VolumeChannel.Se }));
            model.SetMute(true);
            Assert.That(view.ShownMute, Is.False);
        }

        [Test]
        public void Dispose_StopsBinding()
        {
            using var model = CreateModel();
            var view = new FakeVolumeView();
            view.Channels.Add(VolumeChannel.Master);
            var presenter = new VolumeSettingsPresenter(model, view);
            presenter.Initialize();
            presenter.Dispose();

            model.Set(VolumeChannel.Master, 0.1f);
            Assert.That(view.Shown[VolumeChannel.Master], Is.Not.EqualTo(0.1f).Within(1e-5));
        }
    }

    public sealed class GraphicsSettingsPresenterTests
    {
        private sealed class FakeGraphicsView : IGraphicsSettingsView
        {
            public readonly HashSet<GraphicsSettingsField> Fields = new()
            {
                GraphicsSettingsField.Resolution, GraphicsSettingsField.WindowMode,
                GraphicsSettingsField.Quality, GraphicsSettingsField.FrameRate,
            };
            public readonly Dictionary<GraphicsSettingsField, IReadOnlyList<string>> Options = new();
            public readonly Dictionary<GraphicsSettingsField, int> Selected = new();
            public readonly Dictionary<GraphicsSettingsField, bool> Visible = new();
            public readonly Dictionary<GraphicsSettingsField, Subject<int>> Inputs = new();
            public readonly Subject<bool> VSyncInput = new();
            public bool ShownVSync;

            public bool HasSelector(GraphicsSettingsField field) => Fields.Contains(field);
            public void SetSelectorVisible(GraphicsSettingsField field, bool visible) => Visible[field] = visible;
            public void SetOptions(GraphicsSettingsField field, IReadOnlyList<string> options) => Options[field] = options;
            public void SetSelectedIndex(GraphicsSettingsField field, int index) => Selected[field] = index;

            public Observable<int> OnSelected(GraphicsSettingsField field)
            {
                if (!Inputs.TryGetValue(field, out var subject)) Inputs[field] = subject = new Subject<int>();
                return subject;
            }

            public bool HasVSync => true;
            public void SetVSync(bool enabled) => ShownVSync = enabled;
            public Observable<bool> OnVSyncChanged => VSyncInput;
            public IGraphicsSettingsLabels Labels { get; set; }
        }

        private sealed class JapaneseLabels : IGraphicsSettingsLabels
        {
            public string Resolution(Vector2Int r) => $"{r.x}×{r.y}";
            public string WindowMode(FullScreenMode mode) => mode == FullScreenMode.Windowed ? "ウィンドウ" : "全画面";
            public string Quality(int level, string name) => name;
            public string FrameRate(int f) => f < 0 ? "無制限" : $"{f}fps";
        }

        private static GraphicsSettingsModel CreateModel(bool supportsResolution = true) =>
            new(new GraphicsSettingsOptions { FrameRateOptions = new List<int> { 30, 60, -1 } },
                GraphicsSettingsModelTests.CreatePlatform(supportsResolution));

        [Test]
        public void Options_UseDefaultEnglishLabels()
        {
            using var model = CreateModel();
            var view = new FakeGraphicsView();
            using var presenter = new GraphicsSettingsPresenter(model, view);
            presenter.Initialize();

            Assert.That(view.Options[GraphicsSettingsField.FrameRate], Is.EqualTo(new[] { "30 FPS", "60 FPS", "Unlimited" }));
            Assert.That(view.Selected[GraphicsSettingsField.FrameRate], Is.EqualTo(2));
            Assert.That(view.Options[GraphicsSettingsField.Resolution], Is.EqualTo(new[] { "1280 x 720", "1920 x 1080" }));
            Assert.That(view.Selected[GraphicsSettingsField.Resolution], Is.EqualTo(1));
            Assert.That(view.Options[GraphicsSettingsField.Quality], Is.EqualTo(new[] { "Low", "Medium", "High" }));
            Assert.That(view.Selected[GraphicsSettingsField.Quality], Is.EqualTo(1));
        }

        [Test]
        public void InjectedLabels_TakePriorityOverViewLabels()
        {
            using var model = CreateModel();
            var view = new FakeGraphicsView { Labels = new GraphicsSettingsLabels { UnlimitedFrameRate = "View" } };
            using var presenter = new GraphicsSettingsPresenter(model, view, new JapaneseLabels());
            presenter.Initialize();

            Assert.That(view.Options[GraphicsSettingsField.FrameRate][2], Is.EqualTo("無制限"));
        }

        [Test]
        public void Selection_UpdatesModel_AndVSyncIsTwoWay()
        {
            using var model = CreateModel();
            var view = new FakeGraphicsView();
            using var presenter = new GraphicsSettingsPresenter(model, view);
            presenter.Initialize();

            view.Inputs[GraphicsSettingsField.FrameRate].OnNext(1);
            Assert.That(model.TargetFrameRate.CurrentValue, Is.EqualTo(60));
            Assert.That(model.IsExplicit(GraphicsSettingKind.TargetFrameRate), Is.True);

            view.Inputs[GraphicsSettingsField.Quality].OnNext(2);
            Assert.That(model.QualityLevel.CurrentValue, Is.EqualTo(2));

            view.VSyncInput.OnNext(true);
            Assert.That(model.VSync.CurrentValue, Is.True);
            model.SetVSync(false);
            Assert.That(view.ShownVSync, Is.False);
        }

        [Test]
        public void ValueOutsideOptions_RebuildsViewOptions()
        {
            using var model = CreateModel();
            var view = new FakeGraphicsView();
            using var presenter = new GraphicsSettingsPresenter(model, view);
            presenter.Initialize();

            model.SetTargetFrameRate(90);

            Assert.That(view.Options[GraphicsSettingsField.FrameRate], Is.EqualTo(new[] { "30 FPS", "60 FPS", "90 FPS", "Unlimited" }));
            Assert.That(view.Selected[GraphicsSettingsField.FrameRate], Is.EqualTo(2));
        }

        [Test]
        public void ResolutionSelectors_AreHidden_WhenUnsupported()
        {
            using var model = CreateModel(supportsResolution: false);
            var view = new FakeGraphicsView();
            using var presenter = new GraphicsSettingsPresenter(model, view);
            presenter.Initialize();

            Assert.That(view.Visible[GraphicsSettingsField.Resolution], Is.False);
            Assert.That(view.Visible[GraphicsSettingsField.WindowMode], Is.False);
            Assert.That(view.Options.ContainsKey(GraphicsSettingsField.Resolution), Is.False);
        }
    }
}
