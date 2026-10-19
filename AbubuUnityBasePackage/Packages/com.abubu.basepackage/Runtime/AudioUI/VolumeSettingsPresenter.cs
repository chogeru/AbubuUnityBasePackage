using System;
using Abubu.Audio;
using R3;

namespace Abubu.Presentation
{
    /// <summary>
    /// <see cref="AudioVolumeModel"/> と <see cref="IVolumeSettingsView"/> を双方向に結線する Presenter。
    /// </summary>
    /// <remarks>
    /// SE 音量をユーザーが変更したとき、操作が止まってから <see cref="SePreviewDelay"/> 後に確認用 SE を鳴らす
    /// (Time.timeScale の影響を受けないので、ポーズメニューでも鳴る)。ISePlayer が無ければ鳴らさない。
    /// </remarks>
    public class VolumeSettingsPresenter : PresenterBase
    {
        private static readonly VolumeChannel[] Channels = (VolumeChannel[])Enum.GetValues(typeof(VolumeChannel));

        protected AudioVolumeModel Model { get; }
        protected IVolumeSettingsView View { get; }
        protected ISePlayer SePlayer { get; }

        /// <param name="model">音量の Model</param>
        /// <param name="view">表示先</param>
        /// <param name="sePlayer">確認用 SE の再生先 (任意)</param>
        public VolumeSettingsPresenter(AudioVolumeModel model, IVolumeSettingsView view, ISePlayer sePlayer = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            View = view ?? throw new ArgumentNullException(nameof(view));
            SePlayer = sePlayer;
        }

        /// <summary>SE 音量の操作が止まってから確認用 SE を鳴らすまでの時間</summary>
        protected virtual TimeSpan SePreviewDelay => TimeSpan.FromSeconds(0.15);

        /// <summary>確認用 SE の待ち時間に使う時間。既定はタイムスケールを無視する Update タイミング</summary>
        protected virtual TimeProvider SePreviewTimeProvider => UnityTimeProvider.UpdateIgnoreTimeScale;

        protected override void OnInitialize()
        {
            foreach (var channel in Channels)
            {
                if (View.HasChannel(channel)) BindChannel(channel);
            }

            if (View.HasMute)
            {
                Model.Mute.Subscribe(View, static (v, view) => view.SetMute(v)).AddTo(Disposables);
                View.OnMuteChanged.Subscribe(Model, static (v, model) => model.SetMute(v)).AddTo(Disposables);
            }

            if (SePlayer != null && View.HasChannel(VolumeChannel.Se) && !string.IsNullOrEmpty(View.SePreviewKey))
            {
                // Model の変更ではなくユーザー操作だけを拾う (ロードや他画面からの変更では鳴らさない)
                View.OnVolumeChanged(VolumeChannel.Se)
                    .Debounce(SePreviewDelay, SePreviewTimeProvider)
                    .Subscribe(this, static (_, self) => self.PlaySePreview())
                    .AddTo(Disposables);
            }
        }

        /// <summary>1 チャンネル分の双方向バインド (Model → View / View → Model)</summary>
        protected virtual void BindChannel(VolumeChannel channel)
        {
            Model.Get(channel).Subscribe((view: View, channel), static (v, s) => s.view.SetVolume(s.channel, v)).AddTo(Disposables);
            View.OnVolumeChanged(channel).Subscribe((model: Model, channel), static (v, s) => s.model.Set(s.channel, v)).AddTo(Disposables);
        }

        /// <summary>確認用 SE を鳴らす</summary>
        protected virtual void PlaySePreview()
        {
            var key = View.SePreviewKey;
            if (!string.IsNullOrEmpty(key)) SePlayer?.Play(key);
        }
    }
}
