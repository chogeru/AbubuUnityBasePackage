using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using R3;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// 2 つの AudioSource を交互に使ってクロスフェードする BGM プレイヤー。
    /// AudioSource は最初の再生時に作られる。
    /// IVoicePlayer が渡された場合、ボイス再生中は BGM を下げる (SoundSettings.DuckBgmOnVoice)。
    /// Clip は AudioRoot.Clips (IAudioClipProvider) から取り出し、未ロードなら読み込んでから切り替える (読み込み中に別の BGM / Stop が要求されたら破棄)。
    /// <code>
    /// await bgm.PlayAsync("stage1");          // クロスフェードで切り替え
    /// await bgm.StopAsync(fadeDuration: 2f);
    /// </code>
    /// </summary>
    public sealed class BgmPlayer : IBgmPlayer, IDisposable
    {
        private readonly AudioRoot _root;
        private readonly SoundSettings _settings;
        /// <summary>最初の再生まで null</summary>
        private AudioChannel[] _channels;
        private readonly ReactiveProperty<string> _currentKey = new(null);
        private readonly IDisposable _volumeSubscription;
        private readonly IDisposable _duckSubscription;

        private float _categoryVolume = 1f;
        private float _duck = 1f;
        private MotionHandle _duckHandle;
        private int _activeIndex;
        private int _version;
        /// <summary>Play / Stop が要求されるたびに増える。Clip の読み込み中に次の要求が来たかの判定に使う</summary>
        private int _request;
        /// <summary>Clip を読み込み中の BGM キー (同じキーの重複要求を無視する)</summary>
        private string _loadingKey;
        private bool _paused;
        /// <summary>読み込み中に Pause された (読み込み後に一時停止した状態で始める)</summary>
        private bool _pauseWhenLoaded;
        private bool _disposed;

        public ReadOnlyReactiveProperty<string> CurrentKey => _currentKey;

        /// <param name="voice">ダッキング用。null ならダッキングしない</param>
        public BgmPlayer(AudioRoot root, SoundSettings settings, IAudioVolumeModel volume, IVoicePlayer voice = null)
        {
            _root = root;
            _settings = settings;

            _volumeSubscription = volume.GetSourceVolume(settings, SoundCategory.Bgm).Subscribe(this, static (v, self) =>
            {
                self._categoryVolume = v;
                self.ApplyVolume();
            });

            // ボイス再生中は BGM を下げる (終わったら戻す)
            _duckSubscription = voice?.CurrentKey.Subscribe(this, static (key, self) =>
            {
                var target = self._settings.DuckBgmOnVoice && key != null ? self._settings.VoiceDuckLevel : 1f;
                self.DuckTo(target);
            });
        }

        private AudioChannel[] EnsureChannels()
        {
            if (_channels != null) return _channels;
            _channels = new[]
            {
                new AudioChannel(_root.CreateSource("BGM_A", SoundCategory.Bgm)),
                new AudioChannel(_root.CreateSource("BGM_B", SoundCategory.Bgm)),
            };
            ApplyVolume();
            return _channels;
        }

        /// <summary>同じ BGM を鳴らしている (一時停止中を含む) か</summary>
        private bool IsCurrent(string key) =>
            key != null && _currentKey.Value == key && _channels != null && (_paused || _channels[_activeIndex].IsPlaying);

        private void ApplyVolume()
        {
            if (_channels == null) return;
            foreach (var channel in _channels) channel.SetCategoryVolume(_categoryVolume * _duck);
        }

        private void DuckTo(float target)
        {
            _duckHandle.TryCancel();
            var duration = _settings.DuckFadeDuration;
            if (duration <= 0f || Mathf.Approximately(_duck, target))
            {
                _duck = target;
                ApplyVolume();
                return;
            }

            _duckHandle = LMotion.Create(_duck, target, duration)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (x, self) =>
                {
                    self._duck = x;
                    self.ApplyVolume();
                });
        }

        public UniTask PlayAsync(string key, float? fadeDuration = null, CancellationToken ct = default)
        {
            if (_disposed || IsCurrent(key) || (key != null && key == _loadingKey)) return UniTask.CompletedTask;
            if (!_root.TryResolve(SoundCategory.Bgm, key, out var entry)) return UniTask.CompletedTask;
            var duration = fadeDuration ?? _settings.BgmFadeDuration;
            var request = ++_request;
            _loadingKey = null;
            // 一時停止中に要求された BGM は普通に鳴らす (ポーズメニューの BGM など)
            _pauseWhenLoaded = false;

            var clips = _root.Clips;
            return clips.TryAcquire(entry, out var clip)
                ? CrossFadeAsync(entry, clip, clips, key, duration, ct)
                : LoadThenCrossFadeAsync(entry, key, duration, request, ct);
        }

        private async UniTask LoadThenCrossFadeAsync(SoundEntry entry, string key, float duration, int request, CancellationToken ct)
        {
            var clips = _root.Clips;
            _loadingKey = key;
            AudioClip clip;
            try
            {
                clip = await clips.AcquireAsync(entry, ct);
            }
            catch (OperationCanceledException)
            {
                if (request == _request) _loadingKey = null;
                return;
            }

            if (request == _request) _loadingKey = null;
            if (clip == null) return;
            // 読み込み中に別の BGM / Stop が要求された場合は鳴らさない
            if (request != _request || _disposed)
            {
                clips.Release(entry);
                return;
            }
            // 読み込み中に Pause された場合は、切り替えてすぐ一時停止する (Resume で続きから鳴る)
            var startPaused = _pauseWhenLoaded;
            _pauseWhenLoaded = false;
            await CrossFadeAsync(entry, clip, clips, key, duration, ct, startPaused);
        }

        public UniTask PlayAsync(AudioClip clip, float volume = 1f, float? fadeDuration = null, CancellationToken ct = default)
        {
            if (_disposed || clip == null) return UniTask.CompletedTask;
            var entry = SoundEntry.FromClip(clip, volume);
            if (IsCurrent(entry.Key)) return UniTask.CompletedTask;
            _request++;
            _loadingKey = null;
            _pauseWhenLoaded = false;
            return CrossFadeAsync(entry, clip, null, entry.Key, fadeDuration ?? _settings.BgmFadeDuration, ct);
        }

        public async UniTask StopAsync(float? fadeDuration = null, CancellationToken ct = default)
        {
            var version = ++_version;
            _request++;
            _loadingKey = null;
            _paused = false;
            _pauseWhenLoaded = false;
            if (_disposed || _channels == null)
            {
                if (!_disposed) _currentKey.Value = null;
                return;
            }
            var active = _channels[_activeIndex];
            _currentKey.Value = null;

            await UniTask.WhenAll(
                active.FadeAsync(0f, fadeDuration ?? _settings.BgmFadeDuration, ct),
                _channels[1 - _activeIndex].FadeAsync(0f, 0f, ct));

            if (version == _version && !_disposed)
            {
                foreach (var channel in _channels) channel.Stop();
            }
        }

        public void Pause()
        {
            // 読み込み中の BGM は、読み込み後に一時停止した状態で始める (LoadThenCrossFadeAsync)
            if (_loadingKey != null) _pauseWhenLoaded = true;
            if (_channels == null) return;
            _paused = _currentKey.Value != null;
            foreach (var channel in _channels) channel.Source.Pause();
        }

        public void Resume()
        {
            _paused = false;
            _pauseWhenLoaded = false;
            if (_channels == null) return;
            foreach (var channel in _channels)
            {
                if (channel.Entry != null) channel.Source.UnPause();
            }
        }

        /// <param name="owner">clip の参照を返す先 (直接渡された Clip なら null)</param>
        /// <param name="startPaused">true なら鳴らしてすぐ一時停止する (フェードは進むので、Resume 時には切り替えが終わっている)</param>
        private async UniTask CrossFadeAsync(SoundEntry entry, AudioClip clip, IAudioClipProvider owner, string key, float duration, CancellationToken ct,
            bool startPaused = false)
        {
            var version = ++_version;
            _paused = false;
            var channels = EnsureChannels();
            var previous = channels[_activeIndex];
            _activeIndex = 1 - _activeIndex;
            var next = channels[_activeIndex];

            next.Setup(entry, clip, loop: true, owner);
            next.SetFadeImmediate(0f);
            next.Source.Play();
            if (startPaused)
            {
                _paused = true;
                foreach (var channel in channels) channel.Source.Pause();
            }
            _currentKey.Value = key;

            await UniTask.WhenAll(next.FadeAsync(1f, duration, ct), previous.FadeAsync(0f, duration, ct));

            // フェード中に別の BGM が要求された場合は、後続の処理に任せる
            if (version != _version || _disposed) return;

            // キャンセルされた場合も中途半端な音量で止めず、切り替えを完了させる
            if (ct.IsCancellationRequested) next.SetFadeImmediate(1f);
            previous.Stop();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _volumeSubscription.Dispose();
            _duckSubscription?.Dispose();
            _duckHandle.TryCancel();
            if (_channels != null)
            {
                foreach (var channel in _channels)
                {
                    channel.Stop();
                    if (channel.Source != null) AudioRoot.DestroyObject(channel.Source.gameObject);
                }
            }
            _currentKey.Dispose();
        }
    }
}
