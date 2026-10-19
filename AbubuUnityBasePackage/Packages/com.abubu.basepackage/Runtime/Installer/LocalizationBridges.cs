using System;
using System.Threading;
using Abubu.Audio;
using Abubu.Boot;
using Abubu.UI;
using R3;
using Abubu.Localization;
using Abubu.Presentation;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abubu
{
    /// <summary>
    /// 起動処理で翻訳の読み込みを待つ (非同期の取得元で、最初の画面が翻訳前のキーで表示されないように)。
    /// 同期の取得元ではすぐ終わる
    /// </summary>
    internal sealed class LocalizationBootTask : IBootTask
    {
        private readonly ILocalizationService _localization;

        public LocalizationBootTask(ILocalizationService localization) => _localization = localization;

        /// <summary>他の起動処理 (翻訳済みの文言を使うかもしれない) より先に実行する</summary>
        public int Order => -1000;

        public UniTask RunAsync(CancellationToken ct) => _localization.WaitReadyAsync(ct);
    }

    /// <summary>ダイアログのボタン (OK / Cancel) を翻訳する。キーが無ければ AbubuUiSettings の文言を使う</summary>
    internal sealed class LocalizedDialogLabels : IDialogLabels
    {
        public const string OkKey = "abubu.common.ok";
        public const string CancelKey = "abubu.common.cancel";

        private readonly ILocalizationService _localization;
        private readonly IDialogLabels _fallback;

        public LocalizedDialogLabels(ILocalizationService localization, [Zenject.InjectOptional] AbubuUiSettings settings = null)
        {
            _localization = localization;
            _fallback = new DialogLabels(settings != null ? settings : UnityEngine.ScriptableObject.CreateInstance<AbubuUiSettings>());
        }

        public string Ok => _localization.TryGet(OkKey, out _) ? _localization.Get(OkKey) : _fallback.Ok;
        public string Cancel => _localization.TryGet(CancelKey, out _) ? _localization.Get(CancelKey) : _fallback.Cancel;
    }

    /// <summary>ボイスの再生に合わせて字幕を出す (SoundLibrary の Subtitle Key を翻訳して ISubtitleService に渡す)</summary>
    internal sealed class VoiceSubtitleBridge : Zenject.IInitializable, IDisposable
    {
        private readonly ISoundService _sound;
        private readonly ISubtitleService _subtitles;
        private readonly CompositeDisposable _subscriptions = new();

        public VoiceSubtitleBridge(ISoundService sound, ISubtitleService subtitles)
        {
            _sound = sound;
            _subtitles = subtitles;
        }

        public void Initialize()
        {
            if (_sound.Voice == null) return;
            // 置き換え時は「終了 → 開始」の順に来るので、終了で消してから次の字幕を出す
            _sound.Voice.OnVoiceEnded.Subscribe(this, static (_, self) => self._subtitles.Hide()).AddTo(_subscriptions);
            _sound.Voice.OnVoiceStarted.Subscribe(this, static (e, self) => self._subtitles.Show(e.SubtitleKey)).AddTo(_subscriptions);
        }

        public void Dispose() => _subscriptions.Dispose();
    }

    /// <summary>
    /// 画面設定 UI の選択肢 (表示モード・FPS など) を ILocalizationService で翻訳する。
    /// テーブルにキーが無い項目は英語の既定値 (<see cref="GraphicsSettingsLabels"/>) を使う。
    /// 使うキーはパッケージ同梱の AbubuBuiltInStrings.csv を参照 (Setup Project で翻訳テーブルに追加される)。
    /// </summary>
    internal sealed class LocalizedGraphicsSettingsLabels : IGraphicsSettingsLabels
    {
        public const string ResolutionKey = "abubu.graphics.resolution";
        public const string ExclusiveFullScreenKey = "abubu.graphics.window.exclusive";
        public const string FullScreenWindowKey = "abubu.graphics.window.fullscreen";
        public const string MaximizedWindowKey = "abubu.graphics.window.borderless";
        public const string WindowedKey = "abubu.graphics.window.windowed";
        public const string FrameRateKey = "abubu.graphics.fps";
        public const string UnlimitedFrameRateKey = "abubu.graphics.fps.unlimited";
        /// <summary>画質の名前 ("abubu.graphics.quality.high" など。名前は QualitySettings の名前を小文字にしたもの)</summary>
        public const string QualityKeyPrefix = "abubu.graphics.quality.";

        private readonly ILocalizationService _localization;
        private readonly GraphicsSettingsLabels _fallback = new();

        public LocalizedGraphicsSettingsLabels(ILocalizationService localization) => _localization = localization;

        public string Resolution(Vector2Int resolution) =>
            _localization.TryGet(ResolutionKey, out _) ? _localization.Get(ResolutionKey, resolution.x, resolution.y) : _fallback.Resolution(resolution);

        public string WindowMode(FullScreenMode mode)
        {
            var key = mode switch
            {
                FullScreenMode.ExclusiveFullScreen => ExclusiveFullScreenKey,
                FullScreenMode.FullScreenWindow => FullScreenWindowKey,
                FullScreenMode.MaximizedWindow => MaximizedWindowKey,
                FullScreenMode.Windowed => WindowedKey,
                _ => null,
            };
            return key != null && _localization.TryGet(key, out _) ? _localization.Get(key) : _fallback.WindowMode(mode);
        }

        public string Quality(int level, string qualityName)
        {
            var key = QualityKeyPrefix + (qualityName ?? string.Empty).ToLowerInvariant().Replace(' ', '_');
            return _localization.TryGet(key, out _) ? _localization.Get(key) : _fallback.Quality(level, qualityName);
        }

        public string FrameRate(int frameRate)
        {
            if (frameRate < 0) return _localization.TryGet(UnlimitedFrameRateKey, out _) ? _localization.Get(UnlimitedFrameRateKey) : _fallback.FrameRate(frameRate);
            return _localization.TryGet(FrameRateKey, out _) ? _localization.Get(FrameRateKey, frameRate) : _fallback.FrameRate(frameRate);
        }
    }
}
