using System;
using System.Collections.Generic;
using Abubu.Pause;
using Abubu.Save;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Inputs
{
    /// <summary>
    /// <see cref="IHapticsService"/> の標準実装。InputModuleInstaller が自動でバインドする。
    /// <code>
    /// GameInput.Rumble(HapticPreset.Heavy);                  // どこからでも
    /// [Inject] IHapticsService _haptics;                     // DI で受け取る場合
    /// _haptics.Play(0.5f, 0.5f, 0.2f);
    /// optionsToggle.onValueChanged.AddListener(_haptics.SetEnabled);
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>有効/無効・強さは <see cref="HapticsSettings.SaveKey"/> で ISaveService に保存する (ISaveService が無ければ保存しない)</item>
    /// <item>ポーズ中 (<see cref="HapticsSettings.StopOnPause"/>)・フォーカス喪失時 (<see cref="HapticsSettings.StopOnFocusLost"/>)・Dispose 時に止める</item>
    /// <item>振動の秒数は <see cref="HapticsSettings.IgnoreTimeScale"/> に従って数える</item>
    /// </list>
    /// </remarks>
    public sealed class HapticsService : IHapticsService, IInitializable, IDisposable
    {
        /// <summary>保存する内容</summary>
        [Serializable]
        public sealed class SaveData
        {
            public bool Enabled = true;
            public float Intensity = 1f;
        }

        private struct ActiveRumble
        {
            public float Low;
            public float High;
            public float Remaining;
        }

        private static readonly HapticsSettings DefaultSettings = new();

        private readonly IRumbleBackend _backend;
        private readonly HapticsSettings _settings;
        private readonly ISaveService _save;
        private readonly IPauseService _pause;
        private readonly IInputDeviceService _devices;
        private readonly Observable<float> _deltaTime;
        private readonly ReactiveProperty<bool> _enabled;
        private readonly ReactiveProperty<float> _intensity;
        private readonly List<ActiveRumble> _active = new();
        private readonly CompositeDisposable _disposables = new();
        private IDisposable _tick;
        private bool _outputting;
        private bool _focusLost;
        private bool _focusHooked;
        private bool _initialized;
        private bool _disposed;

        public ReadOnlyReactiveProperty<bool> Enabled => _enabled;
        public ReadOnlyReactiveProperty<float> Intensity => _intensity;

        /// <inheritdoc />
        public bool CanPlay
        {
            get
            {
                if (_disposed || !_enabled.Value || _focusLost || !_backend.IsAvailable) return false;
                if (_settings.StopOnPause && _pause != null && _pause.IsPaused.CurrentValue) return false;
                if (_settings.OnlyWhenUsingGamepad && _devices != null && _devices.CurrentDevice.CurrentValue != InputDeviceKind.Gamepad) return false;
                return true;
            }
        }

        /// <inheritdoc />
        public bool SupportsRumble => !_disposed && _backend.SupportsRumble;

        /// <param name="backend">振動の出力先</param>
        /// <param name="save">有効/無効・強さの保存先 (任意)</param>
        /// <param name="pause">ポーズ中に止める場合に必要 (任意)</param>
        /// <param name="devices">キーボード使用中に止める場合に必要 (任意)</param>
        /// <param name="settings">無ければ Resources/AbubuInputSettings、それも無ければ既定値</param>
        [Inject]
        public HapticsService(IRumbleBackend backend,
            [InjectOptional] ISaveService save = null,
            [InjectOptional] IPauseService pause = null,
            [InjectOptional] IInputDeviceService devices = null,
            [InjectOptional] AbubuInputSettings settings = null)
            : this(backend, (settings != null ? settings : AbubuInputSettings.FindDefault())?.Haptics, save, pause, devices)
        {
        }

        /// <param name="backend">振動の出力先</param>
        /// <param name="settings">振動の設定 (null なら既定値)</param>
        /// <param name="save">有効/無効・強さの保存先 (任意)</param>
        /// <param name="pause">ポーズ中に止める場合に必要 (任意)</param>
        /// <param name="devices">キーボード使用中に止める場合に必要 (任意)</param>
        /// <param name="deltaTime">経過時間 (秒) の通知元。null なら毎フレームの Time.unscaledDeltaTime / Time.deltaTime (テスト用)</param>
        public HapticsService(IRumbleBackend backend, HapticsSettings settings, ISaveService save = null, IPauseService pause = null,
            IInputDeviceService devices = null, Observable<float> deltaTime = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _settings = settings ?? new HapticsSettings();
            _save = save;
            _pause = pause;
            _devices = devices;
            _deltaTime = deltaTime;

            var enabled = _settings.EnabledByDefault;
            var intensity = Mathf.Clamp01(_settings.DefaultIntensity);
            if (_save != null && !string.IsNullOrEmpty(_settings.SaveKey) && _save.TryLoad<SaveData>(_settings.SaveKey, out var data) && data != null)
            {
                enabled = data.Enabled;
                intensity = Mathf.Clamp01(data.Intensity);
            }
            _enabled = new ReactiveProperty<bool>(enabled);
            _intensity = new ReactiveProperty<float>(intensity);
        }

        public void Initialize()
        {
            if (_initialized || _disposed) return;
            _initialized = true;

            if (_pause != null && _settings.StopOnPause)
            {
                _pause.IsPaused.Subscribe(this, static (paused, self) =>
                {
                    if (paused) self.Stop();
                }).AddTo(_disposables);
            }
            if (_devices != null && _settings.OnlyWhenUsingGamepad)
            {
                _devices.CurrentDevice.Subscribe(this, static (kind, self) =>
                {
                    if (kind != InputDeviceKind.Gamepad) self.Stop();
                }).AddTo(_disposables);
            }
            if (_settings.StopOnFocusLost)
            {
                Application.focusChanged += OnApplicationFocusChanged;
                _focusHooked = true;
            }
        }

        public void SetEnabled(bool enabled)
        {
            if (_disposed || _enabled.Value == enabled) return;
            _enabled.Value = enabled;
            if (!enabled) Stop();
            Save();
        }

        public void SetIntensity(float intensity)
        {
            if (_disposed) return;
            intensity = Mathf.Clamp01(intensity);
            if (Mathf.Approximately(_intensity.Value, intensity)) return;
            _intensity.Value = intensity;
            if (_active.Count > 0) Apply();
            Save();
        }

        public void Play(float lowFrequency, float highFrequency, float duration)
        {
            if (duration <= 0f || !CanPlay) return;
            lowFrequency = Mathf.Clamp01(lowFrequency);
            highFrequency = Mathf.Clamp01(highFrequency);
            if (lowFrequency <= 0f && highFrequency <= 0f) return;

            _active.Add(new ActiveRumble { Low = lowFrequency, High = highFrequency, Remaining = duration });
            Apply();
            _tick ??= (_deltaTime ?? DefaultDeltaTime()).Subscribe(this, static (dt, self) => self.Tick(dt));
        }

        public void Play(HapticPreset preset)
        {
            var name = preset.ToString();
            var definition = _settings.FindPreset(name) ?? DefaultSettings.FindPreset(name);
            if (definition != null) Play(definition.LowFrequency, definition.HighFrequency, definition.Duration);
        }

        public bool Play(string presetName)
        {
            var definition = _settings.FindPreset(presetName);
            if (definition == null) return false;
            Play(definition.LowFrequency, definition.HighFrequency, definition.Duration);
            return true;
        }

        public void Stop()
        {
            _active.Clear();
            _tick?.Dispose();
            _tick = null;
            if (_outputting)
            {
                _outputting = false;
                _backend.SetMotorSpeeds(0f, 0f);
            }
        }

        /// <summary>経過時間を進める (通常は毎フレーム自動で呼ばれる)</summary>
        private void Tick(float deltaTime)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var rumble = _active[i];
                rumble.Remaining -= deltaTime;
                if (rumble.Remaining <= 0f) _active.RemoveAt(i);
                else _active[i] = rumble;
            }

            if (_active.Count == 0) Stop();
            else Apply();
        }

        /// <summary>鳴っている振動のうちモーターごとに一番強いものを、全体の強さを掛けて出力する</summary>
        private void Apply()
        {
            if (!CanPlay)
            {
                Stop();
                return;
            }

            float low = 0f, high = 0f;
            foreach (var rumble in _active)
            {
                if (rumble.Low > low) low = rumble.Low;
                if (rumble.High > high) high = rumble.High;
            }
            var intensity = _intensity.Value;
            _backend.SetMotorSpeeds(low * intensity, high * intensity);
            _outputting = true;
        }

        private Observable<float> DefaultDeltaTime() => _settings.IgnoreTimeScale
            ? Observable.EveryUpdate(UnityFrameProvider.Update).Select(static _ => Time.unscaledDeltaTime)
            : Observable.EveryUpdate(UnityFrameProvider.Update).Select(static _ => Time.deltaTime);

        private void OnApplicationFocusChanged(bool focused) => HandleFocusChanged(focused);

        /// <summary>アプリのフォーカスが変わった (テストから呼べるよう internal)</summary>
        internal void HandleFocusChanged(bool focused)
        {
            _focusLost = !focused;
            if (_focusLost) Stop();
        }

        private void Save()
        {
            if (_save == null || string.IsNullOrEmpty(_settings.SaveKey)) return;
            _save.Save(_settings.SaveKey, new SaveData { Enabled = _enabled.Value, Intensity = _intensity.Value });
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            if (_focusHooked) Application.focusChanged -= OnApplicationFocusChanged;
            _disposables.Dispose();
            _enabled.Dispose();
            _intensity.Dispose();
        }
    }
}
