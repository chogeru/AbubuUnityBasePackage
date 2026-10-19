using Abubu.Audio;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Presentation
{
    /// <summary>
    /// 音量設定画面の uGUI 実装。使わないスライダーは未設定のままで良い。
    /// Canvas 上のオブジェクトに付けてスライダーを割り当てるだけで動作する。
    /// GameObject > Abubu > Volume Settings Panel で自動生成できる。
    /// </summary>
    /// <remarks>
    /// TextMeshPro や UI Toolkit で作る場合は <see cref="IVolumeSettingsView"/> を実装し、
    /// <c>ViewBase&lt;VolumeSettingsPresenter&gt;</c> を継承すれば同じ Presenter がそのまま使える。
    /// </remarks>
    public sealed class VolumeSettingsView : ViewBase<VolumeSettingsPresenter>, IVolumeSettingsView
    {
        [SerializeField] private Slider master;
        [SerializeField] private Slider bgm;
        [SerializeField] private Slider se;
        [SerializeField] private Slider ambient;
        [SerializeField] private Slider voice;
        [SerializeField] private Toggle mute;

        [Tooltip("SE スライダーを操作したときに鳴らす確認用 SE のキー (任意)")]
        [SerializeField] private string sePreviewKey;

        public Slider Master => master;
        public Slider Bgm => bgm;
        public Slider Se => se;
        public Slider Ambient => ambient;
        public Slider Voice => voice;
        public Toggle Mute => mute;

        /// <inheritdoc />
        public string SePreviewKey => sePreviewKey;

        /// <inheritdoc />
        public bool HasMute => mute != null;

        /// <inheritdoc />
        public Observable<bool> OnMuteChanged => mute != null ? mute.onValueChanged.AsObservable() : Observable.Empty<bool>();

        protected override void OnViewStart()
        {
            // 0〜1 の範囲で扱う
            foreach (var slider in new[] { master, bgm, se, ambient, voice })
            {
                if (slider == null) continue;
                slider.minValue = 0f;
                slider.maxValue = 1f;
            }
        }

        /// <inheritdoc />
        public bool HasChannel(VolumeChannel channel) => GetSlider(channel) != null;

        /// <inheritdoc />
        public void SetVolume(VolumeChannel channel, float value)
        {
            var slider = GetSlider(channel);
            if (slider != null) slider.SetValueWithoutNotify(value);
        }

        /// <inheritdoc />
        public Observable<float> OnVolumeChanged(VolumeChannel channel)
        {
            var slider = GetSlider(channel);
            // onValueChanged は SetValueWithoutNotify では発火しないので、ユーザー操作だけが流れる
            return slider != null ? slider.onValueChanged.AsObservable() : Observable.Empty<float>();
        }

        /// <inheritdoc />
        public void SetMute(bool value)
        {
            if (mute != null) mute.SetIsOnWithoutNotify(value);
        }

        private Slider GetSlider(VolumeChannel channel) => channel switch
        {
            VolumeChannel.Master => master,
            VolumeChannel.Bgm => bgm,
            VolumeChannel.Se => se,
            VolumeChannel.Ambient => ambient,
            VolumeChannel.Voice => voice,
            _ => null,
        };
    }
}
