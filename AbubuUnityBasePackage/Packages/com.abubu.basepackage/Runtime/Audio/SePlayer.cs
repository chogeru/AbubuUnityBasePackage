using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using uPools;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.Audio
{
    /// <summary>
    /// uPools で AudioSource をプールして使い回す SE プレイヤー。
    /// 再生が終わった AudioSource は Tick で自動的にプールへ返却される。
    /// </summary>
    public sealed class SePlayer : ISePlayer, ITickable, IDisposable
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public float EntryVolume;
        }

        private readonly AudioRoot _root;
        private readonly SoundSettings _settings;
        private readonly ObjectPool<Voice> _pool;
        private readonly List<Voice> _active = new();
        private readonly HashSet<Voice> _paused = new();
        private readonly Dictionary<string, float> _lastPlayedTime = new();
        private readonly IDisposable _volumeSubscription;
        private float _categoryVolume = 1f;
        private int _serial;

        public SePlayer(AudioRoot root, SoundSettings settings, AudioVolumeModel volume)
        {
            _root = root;
            _settings = settings;

            _pool = new ObjectPool<Voice>(
                createFunc: () =>
                {
                    var source = root.CreateSource($"SE_{_serial++}", SoundCategory.Se);
                    source.gameObject.SetActive(false);
                    return new Voice { Source = source };
                },
                onRent: v => v.Source.gameObject.SetActive(true),
                onReturn: v =>
                {
                    v.Source.Stop();
                    v.Source.clip = null;
                    v.Source.transform.localPosition = Vector3.zero;
                    v.Source.gameObject.SetActive(false);
                },
                onDestroy: v =>
                {
                    if (v.Source != null) Object.Destroy(v.Source.gameObject);
                });
            _pool.Prewarm(settings.SePoolPrewarm);

            _volumeSubscription = volume.GetSourceVolume(settings, SoundCategory.Se).Subscribe(this, static (v, self) =>
            {
                self._categoryVolume = v;
                foreach (var voice in self._active) voice.Source.volume = voice.EntryVolume * v;
            });
        }

        public void Play(string key)
        {
            if (TryResolve(key, out var entry)) PlayEntry(entry, null);
        }

        public void Play(string key, Vector3 position)
        {
            if (TryResolve(key, out var entry)) PlayEntry(entry, position);
        }

        public void Play(AudioClip clip, float volume = 1f)
        {
            if (clip != null) PlayEntry(SoundEntry.FromClip(clip, volume), null);
        }

        public async UniTask PlayAsync(string key, CancellationToken ct = default)
        {
            if (!TryResolve(key, out var entry)) return;
            var voice = PlayEntry(entry, null);
            var length = voice.Source.clip.length / Mathf.Max(0.01f, Mathf.Abs(voice.Source.pitch));
            await UniTask.Delay(TimeSpan.FromSeconds(length), ignoreTimeScale: true, cancellationToken: ct)
                .SuppressCancellationThrow();
        }

        public void StopAll()
        {
            foreach (var voice in _active) _pool.Return(voice);
            _active.Clear();
            _paused.Clear();
        }

        public void SetPaused(bool paused)
        {
            if (paused)
            {
                foreach (var voice in _active)
                {
                    if (!voice.Source.isPlaying || !_paused.Add(voice)) continue;
                    voice.Source.Pause();
                }
            }
            else
            {
                foreach (var voice in _paused) voice.Source.UnPause();
                _paused.Clear();
            }
        }

        void ITickable.Tick()
        {
            if (AudioListener.pause) return;

            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var voice = _active[i];
                // 一時停止中の SE は isPlaying が false になるが、終わったわけではないので返却しない
                if (voice.Source.isPlaying || _paused.Contains(voice)) continue;
                _active.RemoveAt(i);
                _pool.Return(voice);
            }
        }

        private bool TryResolve(string key, out SoundEntry entry)
        {
            if (!_root.TryResolve(SoundCategory.Se, key, out entry)) return false;
            if (entry.Cooldown <= 0f) return true;

            var now = Time.unscaledTime;
            if (_lastPlayedTime.TryGetValue(key, out var last) && now - last < entry.Cooldown) return false;
            _lastPlayedTime[key] = now;
            return true;
        }

        private Voice PlayEntry(SoundEntry entry, Vector3? position)
        {
            // 同時発音数を超えたら最も古い SE を止める (ボイススティール)
            if (_active.Count >= _settings.MaxSeVoices)
            {
                _paused.Remove(_active[0]);
                _pool.Return(_active[0]);
                _active.RemoveAt(0);
            }

            var voice = _pool.Rent();
            var source = voice.Source;
            voice.EntryVolume = entry.Volume;

            source.clip = entry.PickClip();
            source.pitch = entry.PickPitch();
            source.volume = entry.Volume * _categoryVolume;
            source.spatialBlend = position.HasValue ? entry.SpatialBlend : 0f;
            if (position.HasValue) source.transform.position = position.Value;
            source.Play();

            _active.Add(voice);
            return voice;
        }

        public void Dispose()
        {
            _volumeSubscription.Dispose();
            StopAll();
            _pool.Dispose();
        }
    }
}
