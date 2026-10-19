using Abubu.Audio;
using R3;

namespace Abubu.Presentation
{
    /// <summary>
    /// 音量設定画面の View。<see cref="VolumeSettingsPresenter"/> はこのインターフェースだけを扱うため、
    /// uGUI / TextMeshPro / UI Toolkit など任意の UI で実装できる。
    /// </summary>
    /// <remarks>
    /// Set* は表示の更新だけを行い、ユーザー入力の通知 (On*Changed) を発行してはならない (双方向バインドがループするため)。
    /// </remarks>
    public interface IVolumeSettingsView
    {
        /// <summary>指定チャンネルの操作部品を持っているか。false のチャンネルは結線されない</summary>
        bool HasChannel(VolumeChannel channel);

        /// <summary>音量 (0〜1) を表示する</summary>
        void SetVolume(VolumeChannel channel, float value);

        /// <summary>ユーザーが音量 (0〜1) を変更したときの通知。部品が無ければ何も流れない Observable を返す</summary>
        Observable<float> OnVolumeChanged(VolumeChannel channel);

        /// <summary>ミュートの操作部品を持っているか</summary>
        bool HasMute { get; }

        /// <summary>ミュート状態を表示する</summary>
        void SetMute(bool mute);

        /// <summary>ユーザーがミュートを切り替えたときの通知</summary>
        Observable<bool> OnMuteChanged { get; }

        /// <summary>SE 音量を変えたときに鳴らす確認用 SE のキー。空なら鳴らさない</summary>
        string SePreviewKey { get; }
    }
}
