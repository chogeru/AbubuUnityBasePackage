using System;
using Abubu.Save;
using R3;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// 音量設定の Model。値は 0〜1 で、ISaveService に自動保存される。
    /// 各プレイヤーは Effective* を購読して実際の AudioSource 音量に反映する。
    /// </summary>
    public sealed class AudioVolumeModel : IDisposable
    {
        public const string SaveKey = "abubu.audio-volume";

        public ReactiveProperty<float> Master { get; }
        public ReactiveProperty<float> Bgm { get; }
        public ReactiveProperty<float> Se { get; }
        public ReactiveProperty<float> Ambient { get; }
        public ReactiveProperty<float> Voice { get; }
        public ReactiveProperty<bool> Mute { get; }

        /// <summary>Master・カテゴリ音量・ミュートを掛け合わせた最終音量</summary>
        public ReadOnlyReactiveProperty<float> EffectiveBgm { get; }
        public ReadOnlyReactiveProperty<float> EffectiveSe { get; }
        public ReadOnlyReactiveProperty<float> EffectiveAmbient { get; }
        public ReadOnlyReactiveProperty<float> EffectiveVoice { get; }

        private readonly ISaveService _save;
        private readonly IDisposable _saveSubscription;

        public AudioVolumeModel(ISaveService save, AudioVolumeDefaults defaults = null)
        {
            _save = save;
            defaults ??= new AudioVolumeDefaults();
            var data = save.Load(SaveKey, new AudioVolumeData
            {
                Master = defaults.Master, Bgm = defaults.Bgm, Se = defaults.Se, Ambient = defaults.Ambient, Voice = defaults.Voice,
            });

            Master = new ReactiveProperty<float>(data.Master);
            Bgm = new ReactiveProperty<float>(data.Bgm);
            Se = new ReactiveProperty<float>(data.Se);
            Ambient = new ReactiveProperty<float>(data.Ambient);
            Voice = new ReactiveProperty<float>(data.Voice);
            Mute = new ReactiveProperty<bool>(data.Mute);

            EffectiveBgm = Combine(Bgm);
            EffectiveSe = Combine(Se);
            EffectiveAmbient = Combine(Ambient);
            EffectiveVoice = Combine(Voice);

            // スライダー操作中に毎フレーム書き込まないよう、少し待ってからまとめて保存
            // 購読時に流れる現在値は各ストリームで Skip(1) して、変更だけを拾う
            _saveSubscription = Observable.Merge(
                    Master.Skip(1).AsUnitObservable(), Bgm.Skip(1).AsUnitObservable(), Se.Skip(1).AsUnitObservable(),
                    Ambient.Skip(1).AsUnitObservable(), Voice.Skip(1).AsUnitObservable(), Mute.Skip(1).AsUnitObservable())
                .Debounce(TimeSpan.FromSeconds(0.5))
                .Subscribe(this, static (_, self) => self.Save());
        }

        public ReadOnlyReactiveProperty<float> GetEffective(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => EffectiveBgm,
            SoundCategory.Se => EffectiveSe,
            SoundCategory.Ambient => EffectiveAmbient,
            SoundCategory.Voice => EffectiveVoice,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        public ReactiveProperty<float> GetCategoryVolume(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => Bgm,
            SoundCategory.Se => Se,
            SoundCategory.Ambient => Ambient,
            SoundCategory.Voice => Voice,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        public void Save() => _save.Save(SaveKey, new AudioVolumeData
        {
            Master = Master.Value, Bgm = Bgm.Value, Se = Se.Value, Ambient = Ambient.Value, Voice = Voice.Value, Mute = Mute.Value,
        });

        private ReadOnlyReactiveProperty<float> Combine(ReactiveProperty<float> category) =>
            Observable.CombineLatest(Master, category, Mute, static (m, c, mute) => mute ? 0f : Mathf.Clamp01(m) * Mathf.Clamp01(c))
                .ToReadOnlyReactiveProperty();

        public void Dispose()
        {
            _saveSubscription.Dispose();
            Save();
            EffectiveBgm.Dispose();
            EffectiveSe.Dispose();
            EffectiveAmbient.Dispose();
            EffectiveVoice.Dispose();
            Master.Dispose();
            Bgm.Dispose();
            Se.Dispose();
            Ambient.Dispose();
            Voice.Dispose();
            Mute.Dispose();
        }
    }

    [Serializable]
    public sealed class AudioVolumeData
    {
        public float Master = 1f;
        public float Bgm = 1f;
        public float Se = 1f;
        public float Ambient = 1f;
        public float Voice = 1f;
        public bool Mute;
    }

    [Serializable]
    public sealed class AudioVolumeDefaults
    {
        [Range(0f, 1f)] public float Master = 1f;
        [Range(0f, 1f)] public float Bgm = 0.7f;
        [Range(0f, 1f)] public float Se = 1f;
        [Range(0f, 1f)] public float Ambient = 0.8f;
        [Range(0f, 1f)] public float Voice = 1f;
    }
}
