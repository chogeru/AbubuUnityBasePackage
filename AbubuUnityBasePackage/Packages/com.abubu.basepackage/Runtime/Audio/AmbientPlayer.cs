using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// 環境音をレイヤーとして重ねて鳴らすプレイヤー (例: 風 + 鳥 + 川)。
    /// 各レイヤーは独立してフェードイン/アウトする。
    /// Clip が未ロードなら読み込んでから鳴らす (読み込み中も IsPlaying は true。その間に StopAsync されたら鳴らさない)。
    /// <code>
    /// await ambient.PlayAsync("wind");
    /// await ambient.PlayAsync("river");
    /// await ambient.StopAsync("wind", fadeDuration: 3f);
    /// </code>
    /// </summary>
    public sealed class AmbientPlayer : IAmbientPlayer, IDisposable
    {
        private readonly AudioRoot _root;
        private readonly SoundSettings _settings;
        private readonly AudioObjectPool<AudioChannel> _pool;
        /// <summary>再生中 (フェードイン中を含む) のレイヤー</summary>
        private readonly Dictionary<string, AudioChannel> _layers = new();
        /// <summary>フェードアウト中で、まだプールに戻っていないチャンネル</summary>
        private readonly HashSet<AudioChannel> _stopping = new();
        /// <summary>生成済みの全チャンネル (カテゴリ音量の反映用)</summary>
        private readonly HashSet<AudioChannel> _all = new();
        /// <summary>SetPaused(true) の時点で Clip を読み込み中だったチャンネル (読み込み後に一時停止した状態で始める)</summary>
        private readonly HashSet<AudioChannel> _pauseWhenLoaded = new();
        private readonly IDisposable _volumeSubscription;
        private float _categoryVolume = 1f;
        private int _serial;
        private bool _disposed;

        public AmbientPlayer(AudioRoot root, SoundSettings settings, IAudioVolumeModel volume)
        {
            _root = root;
            _settings = settings;

            _pool = new AudioObjectPool<AudioChannel>(
                create: () =>
                {
                    var channel = new AudioChannel(root.CreateSource($"Ambient_{_serial++}", SoundCategory.Ambient));
                    _all.Add(channel);
                    return channel;
                },
                onGet: c => c.SetCategoryVolume(_categoryVolume),
                onRelease: c =>
                {
                    _pauseWhenLoaded.Remove(c);
                    c.Stop();
                },
                onDestroy: c =>
                {
                    _all.Remove(c);
                    if (c.Source != null) AudioRoot.DestroyObject(c.Source.gameObject);
                });

            _volumeSubscription = volume.GetSourceVolume(settings, SoundCategory.Ambient).Subscribe(this, static (v, self) =>
            {
                self._categoryVolume = v;
                foreach (var channel in self._all) channel.SetCategoryVolume(v);
            });
        }

        public bool IsPlaying(string key) => key != null && _layers.ContainsKey(key);

        public void SetPaused(bool paused)
        {
            if (!paused) _pauseWhenLoaded.Clear();
            else
            {
                // 読み込み中のレイヤーは、読み込み後に一時停止した状態で始める (PlayAsync)。
                // 一時停止中に新しく要求されたレイヤーは普通に鳴らす
                foreach (var channel in _layers.Values)
                {
                    if (channel.Entry == null) _pauseWhenLoaded.Add(channel);
                }
            }

            foreach (var channel in _all)
            {
                if (channel.Entry == null || channel.Source == null) continue;
                if (paused) channel.Source.Pause();
                else channel.Source.UnPause();
            }
        }

        public async UniTask PlayAsync(string key, float? fadeDuration = null, CancellationToken ct = default)
        {
            if (_disposed || string.IsNullOrEmpty(key)) return;
            var duration = fadeDuration ?? _settings.AmbientFadeDuration;

            if (_layers.TryGetValue(key, out var existing))
            {
                await existing.FadeAsync(1f, duration, ct);
                return;
            }

            if (!_root.TryResolve(SoundCategory.Ambient, key, out var entry)) return;

            var channel = _pool.Get();
            _layers.Add(key, channel);
            channel.SetFadeImmediate(0f);

            var clips = _root.Clips;
            if (!clips.TryAcquire(entry, out var clip))
            {
                var version = channel.Version;
                try
                {
                    clip = await clips.AcquireAsync(entry, ct);
                }
                catch (OperationCanceledException)
                {
                    clip = null;
                }

                // 読み込み中に StopAsync / Dispose された (チャンネルが返却・再利用された) 場合は鳴らさない
                if (_disposed || channel.Version != version || !_layers.TryGetValue(key, out var current) || current != channel)
                {
                    if (clip != null) clips.Release(entry);
                    return;
                }
                if (clip == null)
                {
                    _layers.Remove(key);
                    _pool.Release(channel);
                    return;
                }
            }

            channel.Setup(entry, clip, loop: true, clips);
            channel.Source.Play();
            // 読み込み中に SetPaused(true) された場合は、鳴らしてすぐ一時停止する (SetPaused(false) で続きから鳴る)
            if (_pauseWhenLoaded.Remove(channel)) channel.Source.Pause();
            await channel.FadeAsync(1f, duration, ct);
        }

        public async UniTask StopAsync(string key, float? fadeDuration = null, CancellationToken ct = default)
        {
            if (_disposed || key == null || !_layers.Remove(key, out var channel)) return;

            _stopping.Add(channel);
            await channel.FadeAsync(0f, fadeDuration ?? _settings.AmbientFadeDuration, ct);

            // フェード中に Dispose された場合は Dispose 側で片付け済み
            if (!_stopping.Remove(channel)) return;
            _pool.Release(channel);
        }

        public UniTask StopAllAsync(float? fadeDuration = null, CancellationToken ct = default)
        {
            if (_layers.Count == 0) return UniTask.CompletedTask;

            var keys = new string[_layers.Count];
            _layers.Keys.CopyTo(keys, 0);
            var tasks = new UniTask[keys.Length];
            for (var i = 0; i < keys.Length; i++) tasks[i] = StopAsync(keys[i], fadeDuration, ct);
            return UniTask.WhenAll(tasks);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _volumeSubscription.Dispose();
            _pool.Dispose();

            // 停止によるフェードのキャンセルで StopAsync の続きが同期的に走ることがあるため、先にコレクションを空にしてから解放する
            var channels = new List<AudioChannel>(_layers.Count + _stopping.Count);
            channels.AddRange(_layers.Values);
            channels.AddRange(_stopping);
            _layers.Clear();
            _stopping.Clear();
            _pauseWhenLoaded.Clear();
            // Dispose 済みのプールへの Release は停止 + 破棄になる (フェードアウト中のものも含む)
            foreach (var channel in channels) _pool.Release(channel);
        }
    }
}
