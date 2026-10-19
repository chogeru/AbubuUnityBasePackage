using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Abubu.Audio
{
    /// <summary>
    /// サウンド機能の設定 (AbubuSettings.Sound)。
    /// <code>
    /// settings.Sound.UseAddressableClips = true;   // SoundEntry.Address の Clip を必要なときに読み込む (Addressables 導入時)
    /// settings.Sound.PauseSnapshotName = "Paused"; // ポーズ中にミキサーのスナップショットを切り替える
    /// </code>
    /// </summary>
    [Serializable]
    public sealed class SoundSettings
    {
        public SoundLibrary Library;

        public AudioVolumeDefaults DefaultVolumes = new();

        [Tooltip("音量設定の保存キーと、保存までの待ち時間")]
        public AudioVolumeSaveOptions VolumeSave = new();

        [Header("Fade")]
        [Min(0f)] public float BgmFadeDuration = 1f;
        [Min(0f)] public float AmbientFadeDuration = 1.5f;

        [Header("SE Pool")]
        [Tooltip("起動時にあらかじめ生成しておく SE 用 AudioSource の数")]
        [Min(0)] public int SePoolPrewarm = 8;
        [Tooltip("SE の最大同時発音数。超えた場合は最も古い SE を止めて鳴らす")]
        [Min(1)] public int MaxSeVoices = 32;

        [Header("Clip の読み込み")]
        [Tooltip("オンにすると SoundEntry.Address の Clip を Addressables から必要なときに読み込む (Addressables 導入時のみ有効。未導入なら無視して直接参照で鳴らす)。" +
                 "オフ (既定) なら SoundEntry.Clips の直接参照で鳴らす (ライブラリの Clip は常駐する)")]
        public bool UseAddressableClips;
        [Tooltip("SE を鳴らそうとしたときに Clip が読み込まれていなかった場合の扱い")]
        public SeNotLoadedPolicy SeNotLoaded = SeNotLoadedPolicy.LoadThenPlay;
        [Tooltip("LoadThenPlay で、要求からこの秒数以内に読み込みが終わった場合だけ鳴らす (遅れて鳴ると不自然なため)。0 以下なら常に鳴らす")]
        public float SeLoadTimeout = 0.25f;

        [Header("AudioSource (任意)")]
        [Tooltip("カテゴリごとの AudioSource のひな形 (プレハブ)。3D 設定 (Rolloff / Min・Max Distance / Spread / Doppler / Priority 等) を指定したいときに使う。未設定なら Unity の既定値で生成する")]
        public AudioSourceTemplates SourceTemplates = new();

        [Header("Ducking")]
        [Tooltip("ボイス再生中に BGM の音量を下げる")]
        public bool DuckBgmOnVoice = true;
        [Tooltip("ボイス再生中の BGM 音量の倍率 (0〜1)")]
        [Range(0f, 1f)] public float VoiceDuckLevel = 0.4f;
        [Tooltip("音量を下げる / 戻すのにかける秒数")]
        [Min(0f)] public float DuckFadeDuration = 0.25f;

        [Header("Pause (IPauseService と連動)")]
        [Tooltip("ポーズ中に BGM を一時停止する (オフならポーズ中も流れ続ける)")]
        public bool PauseBgm;
        [Tooltip("ポーズ中に再生中の SE を一時停止する (ポーズ中に新しく鳴らす SE は鳴る)")]
        public bool PauseSe = true;
        [Tooltip("ポーズ中に環境音を一時停止する")]
        public bool PauseAmbient = true;
        [Tooltip("ポーズ中に再生中のボイスを一時停止する")]
        public bool PauseVoice = true;

        [Header("Mixer (任意)")]
        [Tooltip("設定すると音量をミキサーの Exposed Parameter で制御する。未設定なら AudioSource.volume で制御")]
        public AudioMixer Mixer;
        [Tooltip("未設定なら Mixer 内の BgmMixerGroupName のグループを自動で使う")]
        public AudioMixerGroup BgmMixerGroup;
        [Tooltip("未設定なら Mixer 内の SeMixerGroupName のグループを自動で使う")]
        public AudioMixerGroup SeMixerGroup;
        [Tooltip("未設定なら Mixer 内の AmbientMixerGroupName のグループを自動で使う")]
        public AudioMixerGroup AmbientMixerGroup;
        [Tooltip("未設定なら Mixer 内の VoiceMixerGroupName のグループを自動で使う")]
        public AudioMixerGroup VoiceMixerGroup;

        [Tooltip("*MixerGroup が未設定のときに Mixer 内から探すグループ名 (大文字小文字は区別しない)。空欄なら自動検出しない")]
        public string BgmMixerGroupName = "BGM";
        public string SeMixerGroupName = "SE";
        public string AmbientMixerGroupName = "Ambient";
        public string VoiceMixerGroupName = "Voice";

        [Tooltip("IAudioMixerSnapshotService.ResetSnapshot で戻すスナップショット名 (Unity の既定名は \"Snapshot\")")]
        public string DefaultSnapshotName = "Snapshot";
        [Tooltip("ポーズ中に遷移するスナップショット名 (こもった音など)。空欄なら切り替えない")]
        public string PauseSnapshotName = "";
        [Tooltip("ポーズ時のスナップショット遷移秒数")]
        [Min(0f)] public float PauseSnapshotDuration = 0.2f;

        [Tooltip("音量 0 のときにミキサーへ設定する dB (無音扱いの下限)")]
        [Range(-144f, 0f)] public float MixerMinDecibel = AudioMixerController.DefaultMinDecibel;

        [Tooltip("ミキサーで公開 (Expose) した音量パラメータ名。空欄の項目は制御しない")]
        public string MasterVolumeParameter = "MasterVolume";
        public string BgmVolumeParameter = "BgmVolume";
        public string SeVolumeParameter = "SeVolume";
        public string AmbientVolumeParameter = "AmbientVolume";
        public string VoiceVolumeParameter = "VoiceVolume";

        public bool UseMixerVolume => Mixer != null;

        public AudioMixerGroup GetMixerGroup(SoundCategory category)
        {
            var group = category switch
            {
                SoundCategory.Bgm => BgmMixerGroup,
                SoundCategory.Se => SeMixerGroup,
                SoundCategory.Ambient => AmbientMixerGroup,
                SoundCategory.Voice => VoiceMixerGroup,
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
            };
            if (group != null || Mixer == null) return group;

            var name = category switch
            {
                SoundCategory.Bgm => BgmMixerGroupName,
                SoundCategory.Se => SeMixerGroupName,
                SoundCategory.Ambient => AmbientMixerGroupName,
                SoundCategory.Voice => VoiceMixerGroupName,
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
            };
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var candidate in Mixer.FindMatchingGroups(name))
            {
                if (string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return null;
        }

        /// <summary>カテゴリの AudioSource ひな形 (未設定なら null)</summary>
        public AudioSource GetSourceTemplate(SoundCategory category) => SourceTemplates?.Get(category);

        public string GetVolumeParameter(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => BgmVolumeParameter,
            SoundCategory.Se => SeVolumeParameter,
            SoundCategory.Ambient => AmbientVolumeParameter,
            SoundCategory.Voice => VoiceVolumeParameter,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
    }

    /// <summary>
    /// カテゴリごとの AudioSource のひな形。設定したプレハブを複製して AudioSource を作る。
    /// 再生ごとに上書きされる項目 (clip / volume / pitch / loop、SE・ボイスの spatialBlend) 以外の設定がそのまま使われる。
    /// ひな形の Output (AudioMixerGroup) が未設定ならミキサー設定のグループが割り当てられる。
    /// </summary>
    [Serializable]
    public sealed class AudioSourceTemplates
    {
        public AudioSource Bgm;
        public AudioSource Se;
        public AudioSource Ambient;
        public AudioSource Voice;

        public AudioSource Get(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => Bgm,
            SoundCategory.Se => Se,
            SoundCategory.Ambient => Ambient,
            SoundCategory.Voice => Voice,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
    }
}

    /// <summary>SE の Clip が未ロードのときの扱い (SoundSettings.SeNotLoaded)</summary>
    public enum SeNotLoadedPolicy
    {
        /// <summary>
        /// 読み込んでから鳴らす。SoundSettings.SeLoadTimeout 以内に読み込めなければ鳴らさない
        /// (StopAll / Stop(handle) / 追従先の破棄で取り消された場合も鳴らさない)。間に合わなかった場合はキーごとに 1 回警告を出す
        /// </summary>
        LoadThenPlay,
        /// <summary>鳴らさず、キーごとに 1 回警告を出す。PreloadAsync で事前に読み込んでおく前提の運用向け</summary>
        Skip,
    }
