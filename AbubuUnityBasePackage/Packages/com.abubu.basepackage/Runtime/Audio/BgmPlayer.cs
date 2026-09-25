using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// 2 つの AudioSource を交互に使ってクロスフェードする BGM プレイヤー。
    /// </summary>
    public sealed class BgmPlayer : IBgmPlayer, IDisposable
    {
        private readonly AudioRoot _root;
        private readonly SoundSettings _settings;
        private readonly AudioChannel[] _channels;
        private readonly ReactiveProperty<string> _currentKey = new(null);
        private readonly IDisposable _volumeSubscription;

        private int _activeIndex;
        private int _version;

        public ReadOnlyReactiveProperty<string> CurrentKey => _currentKey;

        public BgmPlayer(AudioRoot root, SoundSettings settings, AudioVolumeModel volume)
        {
            _root = root;
            _settings = settings;
            _channels = new[]
            {
                new AudioChannel(root.CreateSource("BGM_A", SoundCategory.Bgm)),
                new AudioChannel(root.CreateSource("BGM_B", SoundCategory.Bgm)),
            };

            _volumeSubscription = volume.GetSourceVolume(settings, SoundCategory.Bgm).Subscribe(_channels, static (v, channels) =>
            {
                foreach (var channel in channels) channel.SetCategoryVolume(v);
            });
        }

        public UniTask PlayAsync(string key, float? fadeDuration = null, CancellationToken ct = default)
        {
            if (_currentKey.Value == key && _channels[_activeIndex].IsPlaying) return UniTask.CompletedTask;
            if (!_root.TryResolve(SoundCategory.Bgm, key, out var entry)) return UniTask.CompletedTask;
            return CrossFadeAsync(entry, key, fadeDuration ?? _settings.BgmFadeDuration, ct);
        }

        public UniTask PlayAsync(AudioClip clip, float volume = 1f, float? fadeDuration = null, CancellationToken ct = default)
        {
            if (clip == null) return UniTask.CompletedTask;
            var entry = SoundEntry.FromClip(clip, volume);
            if (_currentKey.Value == entry.Key && _channels[_activeIndex].IsPlaying) return UniTask.CompletedTask;
            return CrossFadeAsync(entry, entry.Key, fadeDuration ?? _settings.BgmFadeDuration, ct);
        }

        public async UniTask StopAsync(float? fadeDuration = null, CancellationToken ct = default)
        {
            var version = ++_version;
            var active = _channels[_activeIndex];
            _currentKey.Value = null;

            await UniTask.WhenAll(
                active.FadeAsync(0f, fadeDuration ?? _settings.BgmFadeDuration, ct),
                _channels[1 - _activeIndex].FadeAsync(0f, 0f, ct));

            if (version == _version)
            {
                foreach (var channel in _channels) channel.Stop();
            }
        }

        public void Pause()
        {
            foreach (var channel in _channels) channel.Source.Pause();
        }

        public void Resume()
        {
            foreach (var channel in _channels)
            {
                if (channel.Entry != null) channel.Source.UnPause();
            }
        }

        private async UniTask CrossFadeAsync(SoundEntry entry, string key, float duration, CancellationToken ct)
        {
            var version = ++_version;
            var previous = _channels[_activeIndex];
            _activeIndex = 1 - _activeIndex;
            var next = _channels[_activeIndex];

            next.Setup(entry, loop: true);
            next.SetFadeImmediate(0f);
            next.Source.Play();
            _currentKey.Value = key;

            await UniTask.WhenAll(next.FadeAsync(1f, duration, ct), previous.FadeAsync(0f, duration, ct));

            // フェード中に別の BGM が要求された場合は、後続の処理に任せる
            if (version == _version) previous.Stop();
        }

        public void Dispose()
        {
            _volumeSubscription.Dispose();
            foreach (var channel in _channels) channel.Stop();
            _currentKey.Dispose();
        }
    }
}
