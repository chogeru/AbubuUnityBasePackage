using System;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Audio
{
    /// <summary>
    /// IAudioVolumeModel の値を AudioMixer の Exposed Parameter (dB) に反映する。
    /// SoundSettings.Mixer が未設定なら何もしない (各プレイヤーが AudioSource.volume で制御する)。
    /// </summary>
    public sealed class AudioMixerController : IInitializable, IDisposable
    {
        /// <summary>SoundSettings.MixerMinDecibel の既定値</summary>
        public const float DefaultMinDecibel = -80f;

        private readonly SoundSettings _settings;
        private readonly IAudioVolumeModel _volume;
        private readonly CompositeDisposable _disposables = new();
        private readonly System.Collections.Generic.HashSet<string> _warned = new();

        public AudioMixerController(SoundSettings settings, IAudioVolumeModel volume)
        {
            _settings = settings;
            _volume = volume;
        }

        public void Initialize()
        {
            if (!_settings.UseMixerVolume) return;

            // Mute は Master に集約する
            _volume.Get(VolumeChannel.Master).CombineLatest(_volume.Mute, static (v, mute) => mute ? 0f : v)
                .Subscribe(this, static (v, self) => self.Apply(self._settings.MasterVolumeParameter, v))
                .AddTo(_disposables);
            Bind(_volume.Get(VolumeChannel.Bgm), _settings.BgmVolumeParameter);
            Bind(_volume.Get(VolumeChannel.Se), _settings.SeVolumeParameter);
            Bind(_volume.Get(VolumeChannel.Ambient), _settings.AmbientVolumeParameter);
            Bind(_volume.Get(VolumeChannel.Voice), _settings.VoiceVolumeParameter);

            // AudioMixer.SetFloat は起動直後のフレームで無視されることがあるため、1フレーム後にもう一度反映する
            Observable.NextFrame().Subscribe(this, static (_, self) => self.ApplyAll()).AddTo(_disposables);
        }

        /// <summary>0〜1 の線形値を dB (minDecibel〜0) に変換。範囲外の値は 0〜1 に丸める</summary>
        public static float LinearToDecibel(float linear, float minDecibel = DefaultMinDecibel)
        {
            linear = Mathf.Clamp01(linear);
            if (minDecibel > 0f) minDecibel = 0f;
            return linear <= 0f ? minDecibel : Mathf.Max(minDecibel, 20f * Mathf.Log10(linear));
        }

        private void Bind(ReadOnlyReactiveProperty<float> property, string parameter) =>
            property.Subscribe((self: this, parameter), static (v, s) => s.self.Apply(s.parameter, v)).AddTo(_disposables);

        private void ApplyAll()
        {
            Apply(_settings.MasterVolumeParameter, _volume.Mute.CurrentValue ? 0f : _volume.Get(VolumeChannel.Master).CurrentValue);
            Apply(_settings.BgmVolumeParameter, _volume.Get(VolumeChannel.Bgm).CurrentValue);
            Apply(_settings.SeVolumeParameter, _volume.Get(VolumeChannel.Se).CurrentValue);
            Apply(_settings.AmbientVolumeParameter, _volume.Get(VolumeChannel.Ambient).CurrentValue);
            Apply(_settings.VoiceVolumeParameter, _volume.Get(VolumeChannel.Voice).CurrentValue);
        }

        private void Apply(string parameter, float linear)
        {
            if (string.IsNullOrEmpty(parameter)) return;
            if (!_settings.Mixer.SetFloat(parameter, LinearToDecibel(linear, _settings.MixerMinDecibel)) && _warned.Add(parameter))
            {
                Debug.LogWarning($"[Abubu.Audio] AudioMixer に Exposed Parameter \"{parameter}\" がありません。ミキサーで Expose するか SoundSettings のパラメータ名を空にしてください。");
            }
        }

        public void Dispose() => _disposables.Dispose();
    }

    internal static class AudioVolumeRouting
    {
        /// <summary>
        /// AudioSource.volume に掛けるカテゴリ音量。
        /// ミキサー制御時はミキサー側で減衰するので常に 1 を返す (二重減衰を防ぐ)。
        /// </summary>
        public static Observable<float> GetSourceVolume(this IAudioVolumeModel volume, SoundSettings settings, SoundCategory category) =>
            settings.UseMixerVolume ? Observable.Return(1f) : volume.GetEffective(category);
    }
}
