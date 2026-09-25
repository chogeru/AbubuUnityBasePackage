using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Abubu.Audio
{
    [Serializable]
    public sealed class SoundSettings
    {
        public SoundLibrary Library;

        public AudioVolumeDefaults DefaultVolumes = new();

        [Header("Fade")]
        [Min(0f)] public float BgmFadeDuration = 1f;
        [Min(0f)] public float AmbientFadeDuration = 1.5f;

        [Header("SE Pool")]
        [Tooltip("起動時にあらかじめ生成しておく SE 用 AudioSource の数")]
        [Min(0)] public int SePoolPrewarm = 8;
        [Tooltip("SE の最大同時発音数。超えた場合は最も古い SE を止めて鳴らす")]
        [Min(1)] public int MaxSeVoices = 32;

        [Header("Mixer (任意)")]
        [Tooltip("設定すると音量をミキサーの Exposed Parameter で制御する。未設定なら AudioSource.volume で制御")]
        public AudioMixer Mixer;
        [Tooltip("未設定なら Mixer 内の \"BGM\" グループを自動で使う")]
        public AudioMixerGroup BgmMixerGroup;
        [Tooltip("未設定なら Mixer 内の \"SE\" グループを自動で使う")]
        public AudioMixerGroup SeMixerGroup;
        [Tooltip("未設定なら Mixer 内の \"Ambient\" グループを自動で使う")]
        public AudioMixerGroup AmbientMixerGroup;

        [Tooltip("ミキサーで公開 (Expose) した音量パラメータ名。空欄の項目は制御しない")]
        public string MasterVolumeParameter = "MasterVolume";
        public string BgmVolumeParameter = "BgmVolume";
        public string SeVolumeParameter = "SeVolume";
        public string AmbientVolumeParameter = "AmbientVolume";

        public bool UseMixerVolume => Mixer != null;

        public AudioMixerGroup GetMixerGroup(SoundCategory category)
        {
            var group = category switch
            {
                SoundCategory.Bgm => BgmMixerGroup,
                SoundCategory.Se => SeMixerGroup,
                SoundCategory.Ambient => AmbientMixerGroup,
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
            };
            if (group != null || Mixer == null) return group;

            var name = category switch
            {
                SoundCategory.Bgm => "BGM",
                SoundCategory.Se => "SE",
                SoundCategory.Ambient => "Ambient",
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
            };
            foreach (var candidate in Mixer.FindMatchingGroups(name))
            {
                if (string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return null;
        }

        public string GetVolumeParameter(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => BgmVolumeParameter,
            SoundCategory.Se => SeVolumeParameter,
            SoundCategory.Ambient => AmbientVolumeParameter,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
    }
}
