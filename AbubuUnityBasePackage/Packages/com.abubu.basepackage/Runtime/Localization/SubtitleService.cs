using System;
using R3;

namespace Abubu.Localization
{
    /// <summary>
    /// 字幕。ボイス (Sound.PlayVoice) の再生に合わせて、SoundLibrary の Subtitle Key を翻訳した文字列が流れる。
    /// 表示は <see cref="SubtitleText"/> をシーンに置くか、<see cref="Current"/> を購読して自前の View に出す。
    /// <code>
    /// _subtitles.Current.Subscribe(text => label.text = text ?? "").AddTo(this);
    /// _subtitles.Show("story.intro.01", 3f);   // ボイス以外 (イベントシーンの台詞など) から出す
    /// </code>
    /// </summary>
    public interface ISubtitleService
    {
        /// <summary>表示中の字幕 (翻訳済み)。表示していないときは null</summary>
        ReadOnlyReactiveProperty<string> Current { get; }

        /// <summary>字幕を表示する。duration 秒後に消える (0 以下なら Hide まで消えない)</summary>
        void Show(string key, float duration = 0f);

        void Hide();
    }

    /// <summary>
    /// <see cref="ISubtitleService"/> の既定実装。ボイスとの連携は AbubuInstaller (Composition Root) が結線する。
    /// 言語が切り替わったら、表示中の字幕も新しい言語で出し直す。
    /// </summary>
    public sealed class SubtitleService : ISubtitleService, IDisposable
    {
        private readonly ILocalizationService _localization;
        private readonly ReactiveProperty<string> _current = new(null);
        private readonly IDisposable _languageSubscription;
        private IDisposable _hideTimer;
        private string _key;

        public ReadOnlyReactiveProperty<string> Current => _current;

        public SubtitleService(ILocalizationService localization)
        {
            _localization = localization;
            _languageSubscription = localization.Changed.Subscribe(this, static (_, self) =>
            {
                if (self._key != null) self._current.Value = self._localization.Get(self._key);
            });
        }

        public void Show(string key, float duration = 0f)
        {
            _hideTimer?.Dispose();
            _hideTimer = null;
            if (string.IsNullOrEmpty(key) || !_localization.TryGet(key, out _))
            {
                // 字幕の無いボイスでは、前の字幕を消すだけにする
                Hide();
                return;
            }

            _key = key;
            _current.Value = _localization.Get(key);
            if (duration > 0f)
            {
                // ポーズ中も字幕だけ残らないよう、実時間で消す
                _hideTimer = Observable.Timer(TimeSpan.FromSeconds(duration), UnityTimeProvider.UpdateIgnoreTimeScale)
                    .Subscribe(this, static (_, self) => self.Hide());
            }
        }

        public void Hide()
        {
            _hideTimer?.Dispose();
            _hideTimer = null;
            _key = null;
            _current.Value = null;
        }

        public void Dispose()
        {
            _hideTimer?.Dispose();
            _languageSubscription.Dispose();
            _current.Dispose();
        }
    }
}
