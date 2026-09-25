using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using uPools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Abubu.Audio
{
    /// <summary>
    /// 環境音をレイヤーとして重ねて鳴らすプレイヤー (例: 風 + 鳥 + 川)。
    /// 各レイヤーは独立してフェードイン/アウトする。
    /// </summary>
    public sealed class AmbientPlayer : IAmbientPlayer, IDisposable
    {
        private readonly AudioRoot _root;
        private readonly SoundSettings _settings;
        private readonly ObjectPool<AudioChannel> _pool;
        private readonly Dictionary<string, AudioChannel> _layers = new();
        private readonly HashSet<AudioChannel> _all = new();
        private readonly IDisposable _volumeSubscription;
        private float _categoryVolume = 1f;
        private int _serial;

        public AmbientPlayer(AudioRoot root, SoundSettings settings, AudioVolumeModel volume)
        {
            _root = root;
            _settings = settings;

            _pool = new ObjectPool<AudioChannel>(
                createFunc: () =>
                {
                    var channel = new AudioChannel(root.CreateSource($"Ambient_{_serial++}", SoundCategory.Ambient));
                    _all.Add(channel);
                    return channel;
                },
                onRent: c => c.SetCategoryVolume(_categoryVolume),
                onReturn: c => c.Stop(),
                onDestroy: c =>
                {
                    _all.Remove(c);
                    if (c.Source != null) Object.Destroy(c.Source.gameObject);
                });

            _volumeSubscription = volume.GetSourceVolume(settings, SoundCategory.Ambient).Subscribe(this, static (v, self) =>
            {
                self._categoryVolume = v;
                foreach (var channel in self._all) channel.SetCategoryVolume(v);
            });
        }

        public bool IsPlaying(string key) => _layers.ContainsKey(key);

        public async UniTask PlayAsync(string key, float? fadeDuration = null, CancellationToken ct = default)
        {
            var duration = fadeDuration ?? _settings.AmbientFadeDuration;

            if (_layers.TryGetValue(key, out var existing))
            {
                await existing.FadeAsync(1f, duration, ct);
                return;
            }

            if (!_root.TryResolve(SoundCategory.Ambient, key, out var entry)) return;

            var channel = _pool.Rent();
            _layers.Add(key, channel);
            channel.Setup(entry, loop: true);
            channel.SetFadeImmediate(0f);
            channel.Source.Play();
            await channel.FadeAsync(1f, duration, ct);
        }

        public async UniTask StopAsync(string key, float? fadeDuration = null, CancellationToken ct = default)
        {
            if (!_layers.Remove(key, out var channel)) return;

            await channel.FadeAsync(0f, fadeDuration ?? _settings.AmbientFadeDuration, ct);
            _pool.Return(channel);
        }

        public UniTask StopAllAsync(float? fadeDuration = null, CancellationToken ct = default) =>
            UniTask.WhenAll(_layers.Keys.ToArray().Select(key => StopAsync(key, fadeDuration, ct)));

        public void Dispose()
        {
            _volumeSubscription.Dispose();
            foreach (var channel in _layers.Values) _pool.Return(channel);
            _layers.Clear();
            _pool.Dispose();
        }
    }
}
