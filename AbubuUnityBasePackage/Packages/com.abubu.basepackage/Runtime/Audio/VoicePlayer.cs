using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.Audio
{
    /// <summary>
    /// キャラクターのセリフ等を鳴らすプレイヤー。AudioSource は 1 つで、新しいボイスは再生中のボイスを置き換える (重ならない)。
    /// </summary>
    public sealed class VoicePlayer : IVoicePlayer, ITickable, IDisposable
    {
        private readonly AudioRoot _root;
        private readonly AudioSource _source;
        private readonly ReactiveProperty<string> _currentKey = new();
        private readonly IDisposable _volumeSubscription;
        private readonly System.Collections.Generic.Dictionary<string, float> _lastPlayedTime = new();
        private float _categoryVolume = 1f;
        private float _entryVolume = 1f;
        private bool _paused;
        private int _serial;

        public VoicePlayer(AudioRoot root, SoundSettings settings, AudioVolumeModel volume)
        {
            _root = root;
            _source = root.CreateSource("Voice", SoundCategory.Voice);

            _volumeSubscription = volume.GetSourceVolume(settings, SoundCategory.Voice).Subscribe(this, static (v, self) =>
            {
                self._categoryVolume = v;
                self._source.volume = self._entryVolume * v;
            });
        }

        public ReadOnlyReactiveProperty<string> CurrentKey => _currentKey;

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
            // 置き換え / Stop (= serial が変わる) か、再生終了で戻る。一時停止中は終わったとみなさない
            await UniTask.WaitUntil(() => _serial != serial || !IsActive(), cancellationToken: ct)
                .SuppressCancellationThrow();
        }

        void ITickable.Tick()
        {
            // 再生が終わったらキーを空にする (ポーズ中は isPlaying が false になるが終わっていない)
            if (_currentKey.Value != null && !IsActive()) _currentKey.Value = null;
        }

        private bool IsActive() => _source.isPlaying || _paused || AudioListener.pause;

        public void Stop()
        {
            _serial++;
            _paused = false;
            _source.Stop();
            _source.clip = null;
            _currentKey.Value = null;
        }

        public void SetPaused(bool paused)
        {
            if (paused)
            {
                if (!_source.isPlaying) return;
                _paused = true;
                _source.Pause();
            }
            else if (_paused)
            {
                _paused = false;
                _source.UnPause();
            }
        }

        private bool TryResolve(string key, out SoundEntry entry)
        {
            if (!_root.TryResolve(SoundCategory.Voice, key, out entry)) return false;
            if (entry.Cooldown <= 0f) return true;

            var now = Time.unscaledTime;
            if (_lastPlayedTime.TryGetValue(key, out var last) && now - last < entry.Cooldown) return false;
            _lastPlayedTime[key] = now;
            return true;
        }

        private int PlayEntry(string key, SoundEntry entry, Vector3? position)
        {
            _source.Stop();
            _paused = false;
            _entryVolume = entry.Volume;

            _source.clip = entry.PickClip();
            _source.pitch = entry.PickPitch();
            _source.volume = entry.Volume * _categoryVolume;
            _source.spatialBlend = position.HasValue ? entry.SpatialBlend : 0f;
            _source.transform.localPosition = position ?? Vector3.zero;
            if (position.HasValue) _source.transform.position = position.Value;
            _source.Play();

            _currentKey.Value = key;
            return ++_serial;
        }

        public void Dispose()
        {
            _volumeSubscription.Dispose();
            _currentKey.Dispose();
            if (_source != null) Object.Destroy(_source.gameObject);
        }
    }
}
