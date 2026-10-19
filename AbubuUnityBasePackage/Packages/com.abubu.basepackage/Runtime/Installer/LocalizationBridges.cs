using System;
using System.Threading;
using Abubu.Audio;
using Abubu.Boot;
using R3;
using Abubu.Localization;
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
}
