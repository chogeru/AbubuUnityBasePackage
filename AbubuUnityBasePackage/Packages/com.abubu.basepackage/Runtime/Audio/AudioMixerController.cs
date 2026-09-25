using System;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Audio
{
    /// <summary>
    /// AudioVolumeModel の値を AudioMixer の Exposed Parameter (dB) に反映する。
    /// SoundSettings.Mixer が未設定なら何もしない (各プレイヤーが AudioSource.volume で制御する)。
    /// </summary>
    public sealed class AudioMixerController : IInitializable, IDisposable
    {
        private const float MinDecibel = -80f;

        private readonly SoundSettings _settings;
        private readonly AudioVolumeModel _volume;
        private readonly CompositeDisposable _disposables = new();

        public AudioMixerController(SoundSettings settings, AudioVolumeModel volume)
        {
            _settings = settings;
            _volume = volume;
        }

        public void Initialize()
        {
            if (!_settings.UseMixerVolume) return;

            // Mute は Master に集約する
            _volume.Master.CombineLatest(_volume.Mute, static (v, mute) => mute ? 0f : v)
                .Subscribe(this, static (v, self) => self.Apply(self._settings.MasterVolumeParameter, v))
                .AddTo(_disposables);
            Bind(_volume.Bgm, _settings.BgmVolumeParameter);
            Bind(_volume.Se, _settings.SeVolumeParameter);
            Bind(_volume.Ambient, _settings.AmbientVolumeParameter);

            // AudioMixer.SetFloat は起動直後のフレームで無視されることがあるため、1フレーム後にもう一度反映する
            Observable.NextFrame().Subscribe(this, static (_, self) => self.ApplyAll()).AddTo(_disposables);
        }

        /// <summary>0〜1 の線形値を dB (-80〜0) に変換</summary>
        public static float LinearToDecibel(float linear) =>
            linear <= 0.0001f ? MinDecibel : Mathf.Max(MinDecibel, 20f * Mathf.Log10(linear));

        private void Bind(ReactiveProperty<float> property, string parameter) =>
            property.Subscribe((self: this, parameter), static (v, s) => s.self.Apply(s.parameter, v)).AddTo(_disposables);

        private void ApplyAll()
        {
            Apply(_settings.MasterVolumeParameter, _volume.Mute.Value ? 0f : _volume.Master.Value);
            Apply(_settings.BgmVolumeParameter, _volume.Bgm.Value);
            Apply(_settings.SeVolumeParameter, _volume.Se.Value);
            Apply(_settings.AmbientVolumeParameter, _volume.Ambient.Value);
        }

        private void Apply(string parameter, float linear)
        {
            if (string.IsNullOrEmpty(parameter)) return;
            if (!_settings.Mixer.SetFloat(parameter, LinearToDecibel(linear)))
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
        public static Observable<float> GetSourceVolume(this AudioVolumeModel volume, SoundSettings settings, SoundCategory category) =>
            settings.UseMixerVolume ? Observable.Return(1f) : volume.GetEffective(category);
    }
}
