namespace Abubu.Audio
{
    /// <summary>
    /// AudioVolumeModel で個別に設定できる音量の種類。
    /// SoundCategory (再生カテゴリ) に全体音量の Master を加えたもの。
    /// </summary>
    public enum VolumeChannel
    {
        /// <summary>全体音量。各カテゴリの音量に掛け合わされる</summary>
        Master,
        Bgm,
        Se,
        Ambient,
        Voice,
    }
}
