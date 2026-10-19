using Abubu.Inputs;
using Abubu.Pause;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace Abubu.Tests
{
    public sealed class HapticsServiceTests
    {
        private FakeRumbleBackend _backend;
        private MemorySaveService _save;
        private PauseService _pause;
        private Subject<float> _ticks;
        private HapticsSettings _settings;
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            _backend = new FakeRumbleBackend();
            _save = new MemorySaveService();
            _pause = new PauseService();
            _ticks = new Subject<float>();
            _settings = new HapticsSettings();
        }

        [TearDown]
        public void TearDown()
        {
            _pause.Dispose();
            _ticks.Dispose();
            Time.timeScale = _timeScale;
        }

        private HapticsService Create(IInputDeviceService devices = null)
        {
            var service = new HapticsService(_backend, _settings, _save, _pause, devices, _ticks);
            service.Initialize();
            return service;
        }

        [Test]
        public void Play_AppliesIntensity()
        {
            using var haptics = Create();
            haptics.SetIntensity(0.5f);
            haptics.Play(1f, 0.6f, 1f);
            Assert.AreEqual(0.5f, _backend.Low, 1e-5f);
            Assert.AreEqual(0.3f, _backend.High, 1e-5f);
        }

        [Test]
        public void Play_StopsAfterDuration()
        {
            using var haptics = Create();
            haptics.Play(1f, 1f, 1f);
            _ticks.OnNext(0.5f);
            Assert.AreEqual(1f, _backend.Low);
            _ticks.OnNext(0.6f);
            Assert.AreEqual(0f, _backend.Low);
            Assert.AreEqual(0f, _backend.High);
        }

        [Test]
        public void OverlappingRumbles_UseStrongestPerMotor()
        {
            using var haptics = Create();
            haptics.Play(0.8f, 0.1f, 0.2f);
            haptics.Play(0.2f, 0.9f, 1f);
            Assert.AreEqual(0.8f, _backend.Low, 1e-5f);
            Assert.AreEqual(0.9f, _backend.High, 1e-5f);

            _ticks.OnNext(0.3f);
            Assert.AreEqual(0.2f, _backend.Low, 1e-5f);
            Assert.AreEqual(0.9f, _backend.High, 1e-5f);
        }

        [Test]
        public void Disabled_DoesNotPlay_AndStopsCurrent()
        {
            using var haptics = Create();
            haptics.Play(1f, 1f, 1f);
            haptics.SetEnabled(false);
            Assert.AreEqual(0f, _backend.Low);

            haptics.Play(1f, 1f, 1f);
            Assert.AreEqual(0f, _backend.Low);
            Assert.IsFalse(haptics.CanPlay);
        }

        [Test]
        public void Pause_StopsAndBlocks()
        {
            using var haptics = Create();
            haptics.Play(1f, 1f, 1f);
            _pause.Pause(this);
            Assert.AreEqual(0f, _backend.Low);

            haptics.Play(1f, 1f, 1f);
            Assert.AreEqual(0f, _backend.Low);

            _pause.Resume(this);
            haptics.Play(1f, 1f, 1f);
            Assert.AreEqual(1f, _backend.Low);
        }

        [Test]
        public void Pause_DoesNotStop_WhenDisabledInSettings()
        {
            _settings.StopOnPause = false;
            using var haptics = Create();
            haptics.Play(1f, 1f, 1f);
            _pause.Pause(this);
            Assert.AreEqual(1f, _backend.Low);
        }

        [Test]
        public void FocusLost_Stops()
        {
            using var haptics = Create();
            haptics.Play(1f, 1f, 1f);
            haptics.HandleFocusChanged(false);
            Assert.AreEqual(0f, _backend.Low);
            haptics.Play(1f, 1f, 1f);
            Assert.AreEqual(0f, _backend.Low);

            haptics.HandleFocusChanged(true);
            haptics.Play(1f, 1f, 1f);
            Assert.AreEqual(1f, _backend.Low);
        }

        [Test]
        public void NoGamepad_IsNoOp()
        {
            _backend.Available = false;
            using var haptics = Create();
            haptics.Play(1f, 1f, 1f);
            Assert.AreEqual(0, _backend.CallCount);
        }

        [Test]
        public void KeyboardInUse_IsNoOp_WhenOnlyWhenUsingGamepad()
        {
            var devices = new FakeDeviceService();
            using var haptics = Create(devices);
            haptics.Play(1f, 1f, 1f);
            Assert.AreEqual(0f, _backend.Low);

            devices.Set(InputDeviceKind.Gamepad);
            haptics.Play(1f, 1f, 1f);
            Assert.AreEqual(1f, _backend.Low);

            devices.Set(InputDeviceKind.KeyboardMouse);
            Assert.AreEqual(0f, _backend.Low);
        }

        [Test]
        public void Settings_ArePersisted()
        {
            using (var haptics = Create())
            {
                haptics.SetIntensity(0.25f);
                haptics.SetEnabled(false);
            }

            using var reloaded = Create();
            Assert.AreEqual(0.25f, reloaded.Intensity.CurrentValue, 1e-5f);
            Assert.IsFalse(reloaded.Enabled.CurrentValue);
        }

        [Test]
        public void SetIntensity_IsClamped()
        {
            using var haptics = Create();
            haptics.SetIntensity(3f);
            Assert.AreEqual(1f, haptics.Intensity.CurrentValue);
            haptics.SetIntensity(-1f);
            Assert.AreEqual(0f, haptics.Intensity.CurrentValue);
        }

        [Test]
        public void Preset_UsesConfiguredValues()
        {
            _settings.Presets.Clear();
            _settings.Presets.Add(new HapticPresetDefinition("Heavy", 0.7f, 0.6f, 1f));
            _settings.Presets.Add(new HapticPresetDefinition("Explosion", 0.4f, 0.3f, 1f));
            using var haptics = Create();

            haptics.Play(HapticPreset.Heavy);
            Assert.AreEqual(0.7f, _backend.Low, 1e-5f);
            haptics.Stop();

            Assert.IsTrue(haptics.Play("explosion"));
            Assert.AreEqual(0.4f, _backend.Low, 1e-5f);
            Assert.IsFalse(haptics.Play("Missing"));
        }

        [Test]
        public void Preset_FallsBackToBuiltIn_WhenRemovedFromSettings()
        {
            _settings.Presets.Clear();
            using var haptics = Create();
            haptics.Play(HapticPreset.Light);
            Assert.Greater(_backend.High, 0f);
        }

        [Test]
        public void Dispose_Stops()
        {
            var haptics = Create();
            haptics.Play(1f, 1f, 1f);
            haptics.Dispose();
            Assert.AreEqual(0f, _backend.Low);
        }
    }
}
