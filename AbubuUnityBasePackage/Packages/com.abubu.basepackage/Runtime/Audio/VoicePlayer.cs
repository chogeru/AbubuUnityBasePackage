using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Audio
{
    /// <summary>
    /// キャラクターのセリフ等を鳴らすプレイヤー。AudioSource は 1 つで、新しいボイスは再生中のボイスを置き換える (重ならない)。
    /// AudioSource は最初の再生時に作られる。
    /// Clip が未ロードなら読み込んでから鳴らす (読み込み中に次のボイス / Stop が来たらそちらを優先し、読み込んだものは鳴らさない)。
    /// 再生開始・終了を OnVoiceStarted / OnVoiceEnded で通知する (字幕・アクセシビリティ表示用)。
    /// <code>
    /// voice.Play("hero_001");
    /// voice.OnVoiceStarted.Subscribe(e => subtitle.Show(e.SubtitleKey, e.Duration));
    /// voice.OnVoiceEnded.Subscribe(_ => subtitle.Hide());
    /// </code>
    /// </summary>
    public sealed class VoicePlayer : IVoicePlayer, ITickable, IDisposable
    {
        private readonly AudioRoot _root;
        /// <summary>最初の再生まで null</summary>
        private AudioSource _source;
        private readonly ReactiveProperty<string> _currentKey = new();
        private readonly Subject<VoiceEvent> _started = new();
        private readonly Subject<VoiceEvent> _ended = new();
        private readonly IDisposable _volumeSubscription;
        private readonly CooldownGate _cooldown = new();
        private float _categoryVolume = 1f;
        private float _entryVolume = 1f;
        /// <summary>再生中のエントリ (Clip の参照を保持している)。未再生なら null</summary>
        private SoundEntry _currentEntry;
        private float _currentDuration;
        private bool _paused;
        /// <summary>読み込み中に SetPaused(true) された (読み込み後に一時停止した状態で始める)</summary>
        private bool _pauseWhenLoaded;
        /// <summary>Play / Stop のたびに増える通し番号</summary>
        private int _serial;
        /// <summary>Clip を読み込み中の再生の通し番号 (読み込み中でなければ -1)</summary>
        private int _loadingSerial = -1;
        private bool _disposed;

        public VoicePlayer(AudioRoot root, SoundSettings settings, IAudioVolumeModel volume)
        {
            _root = root;

            _volumeSubscription = volume.GetSourceVolume(settings, SoundCategory.Voice).Subscribe(this, static (v, self) =>
            {
                self._categoryVolume = v;
                if (self._source != null) self._source.volume = self._entryVolume * v;
            });
        }

        public ReadOnlyReactiveProperty<string> CurrentKey => _currentKey;
        public Observable<VoiceEvent> OnVoiceStarted => _started;
        public Observable<VoiceEvent> OnVoiceEnded => _ended;

        public bool IsPlaying => _currentKey.Value != null;

        public void Play(string key)
        {
            if (TryResolve(key, out var entry)) PlayEntry(key, entry, null);
        }

        public void Play(string key, Vector3 position)
        {
            if (TryResolve(key, out var entry)) PlayEntry(key, entry, position);
        }

        public async UniTask PlayAsync(string key, CancellationToken ct = default)
        {
            if (!TryResolve(key, out var entry)) return;
            var serial = PlayEntry(key, entry, null);
            // 置き換え / Stop (= serial が変わる) か、再生終了で戻る。読み込み中・一時停止中は終わったとみなさない
            if (serial < 0) return;
            await UniTask.WaitUntil((self: this, serial),
                    static s => s.self._serial != s.serial || (s.self._loadingSerial != s.serial && !s.self.IsActive()), cancellationToken: ct)
                .SuppressCancellationThrow();
        }

        void ITickable.Tick()
        {
            // 再生が終わったらキーを空にする (ポーズ中は isPlaying が false になるが終わっていない)
            if (_disposed) return;
            if (_currentKey.Value != null && !IsActive()) EndCurrent(interrupted: false, clearKey: true);
        }

        private bool IsActive() => !_disposed && _source != null && (_source.isPlaying || _paused || AudioListener.pause);

        public void Stop()
        {
            if (_disposed) return;
            _serial++;
            _loadingSerial = -1;
            _pauseWhenLoaded = false;
            EndCurrent(interrupted: true, clearKey: true);
        }

        public void SetPaused(bool paused)
        {
            if (paused)
            {
                // 読み込み中のボイスは、読み込み後に一時停止した状態で始める (LoadThenPlayAsync)
                if (_loadingSerial >= 0) _pauseWhenLoaded = true;
                if (_source == null || !_source.isPlaying) return;
                _paused = true;
                _source.Pause();
            }
            else
            {
                _pauseWhenLoaded = false;
                if (!_paused) return;
                _paused = false;
                _source.UnPause();
            }
        }

        private bool TryResolve(string key, out SoundEntry entry)
        {
            return _root.TryResolve(SoundCategory.Voice, key, out entry) && _cooldown.TryPass(key, entry.Cooldown);
        }

        /// <returns>再生の通し番号。破棄済みなら -1</returns>
        private int PlayEntry(string key, SoundEntry entry, Vector3? position)
        {
            if (_disposed) return -1;
            var serial = ++_serial;
            _loadingSerial = -1;
            // 一時停止中に要求されたボイスは普通に鳴らす (ポーズメニューでの案内音声など)
            _pauseWhenLoaded = false;

            var clips = _root.Clips;
            if (clips.TryAcquire(entry, out var clip))
            {
                // すぐ鳴らせる場合はキーを空にせず置き換える (BGM のダッキングが一瞬戻らないように)
                EndCurrent(interrupted: true, clearKey: false);
                Start(key, entry, clip, position);
            }
            else
            {
                EndCurrent(interrupted: true, clearKey: true);
                _loadingSerial = serial;
                LoadThenPlayAsync(key, entry, position, serial).Forget();
            }
            return serial;
        }

        private async UniTaskVoid LoadThenPlayAsync(string key, SoundEntry entry, Vector3? position, int serial)
        {
            var clips = _root.Clips;
            AudioClip clip;
            try
            {
                clip = await clips.AcquireAsync(entry);
            }
            catch (OperationCanceledException)
            {
                clip = null;
            }

            if (_loadingSerial == serial) _loadingSerial = -1;
            if (clip == null) return;
            // 読み込み中に次のボイス / Stop / Dispose があった場合は鳴らさない
            if (_disposed || serial != _serial)
            {
                clips.Release(entry);
                return;
            }
            Start(key, entry, clip, position);

            // 読み込み中に SetPaused(true) された場合は、鳴らしてすぐ一時停止する (SetPaused(false) で続きから鳴る)
            if (_pauseWhenLoaded)
            {
                _pauseWhenLoaded = false;
                _paused = true;
                _source.Pause();
            }
        }

        /// <summary>clip は参照を取得済みであること (EndCurrent で返す)</summary>
        private void Start(string key, SoundEntry entry, AudioClip clip, Vector3? position)
        {
            if (_source == null) _source = _root.CreateSource("Voice", SoundCategory.Voice);
            _paused = false;
            _entryVolume = entry.Volume;

            var pitch = entry.PickPitch();
            _source.clip = clip;
            _source.pitch = pitch;
            _source.volume = entry.Volume * _categoryVolume;
            _source.spatialBlend = position.HasValue ? entry.SpatialBlend : 0f;
            _source.transform.localPosition = Vector3.zero;
            if (position.HasValue) _source.transform.position = position.Value;
            _source.Play();

            _currentEntry = entry;
            _currentDuration = clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch));
            _currentKey.Value = key;
            _started.OnNext(new VoiceEvent(key, entry, _currentDuration, interrupted: false));
        }

        /// <summary>再生中のボイスを止めて Clip の参照を返し、OnVoiceEnded を通知する</summary>
        private void EndCurrent(bool interrupted, bool clearKey)
        {
            _paused = false;
            if (_source != null)
            {
                _source.Stop();
                _source.clip = null;
            }

            var entry = _currentEntry;
            var key = _currentKey.Value;
            _currentEntry = null;
            if (entry != null) _root.Clips.Release(entry);
            if (clearKey) _currentKey.Value = null;
            if (entry != null) _ended.OnNext(new VoiceEvent(key, entry, _currentDuration, interrupted));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _volumeSubscription.Dispose();
            EndCurrent(interrupted: true, clearKey: true);
            _started.OnCompleted();
            _ended.OnCompleted();
            _started.Dispose();
            _ended.Dispose();
            _currentKey.Dispose();
            if (_source != null) AudioRoot.DestroyObject(_source.gameObject);
            _source = null;
        }
    }
}
