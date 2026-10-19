using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Abubu.Audio
{
    /// <summary>
    /// AudioMixer のスナップショットを名前で切り替える (ポーズ中のこもった音、水中、洞窟の残響など)。
    /// SoundSettings.Mixer が未設定なら何もしない (IsAvailable が false)。
    /// TransitionTo は「基本の状態」を変え、Push は一時的な重ね掛け (Dispose で元に戻る。ポーズの上に水中、のような入れ子にも対応)。
    /// <code>
    /// sound.Snapshots.TransitionTo("Underwater", 0.5f);              // 基本の状態を切り替える
    /// var paused = sound.Snapshots.Push("Paused", 0.2f);             // 一時的に切り替える
    /// paused.Dispose();                                              // 元 (Underwater) に戻る
    /// sound.Snapshots.ResetSnapshot(0.5f);                           // SoundSettings.DefaultSnapshotName に戻す
    /// </code>
    /// ポーズ中 (Time.timeScale = 0) にも遷移させるには、AudioMixer の Update Mode を Unscaled Time にしておくこと。
    /// </summary>
    public interface IAudioMixerSnapshotService
    {
        /// <summary>ミキサーが設定されていて、切り替えができるか</summary>
        bool IsAvailable { get; }
        /// <summary>今適用しているスナップショット名 (切り替えたことがなければ null)</summary>
        string Current { get; }

        /// <summary>基本のスナップショットを切り替える。Push 中なら、全て Dispose された時点で反映される。見つからなければ警告して false</summary>
        bool TransitionTo(string snapshotName, float duration = 0f);
        /// <summary>基本のスナップショットを SoundSettings.DefaultSnapshotName に戻す</summary>
        void ResetSnapshot(float duration = 0f);
        /// <summary>一時的にスナップショットを切り替える。戻り値を Dispose すると直前の状態に (duration 秒で) 戻る</summary>
        IDisposable Push(string snapshotName, float duration = 0f);
    }

    /// <summary>
    /// SoundSettings.Mixer のスナップショットを切り替える既定の実装。
    /// <code>
    /// var snapshots = new AudioMixerSnapshotService(settings);
    /// snapshots.TransitionTo("Cave", 1f);
    /// </code>
    /// </summary>
    public sealed class AudioMixerSnapshotService : IAudioMixerSnapshotService
    {
        private sealed class Layer : IDisposable
        {
            public AudioMixerSnapshotService Owner;
            public string Name;
            public float Duration;

            public void Dispose()
            {
                var owner = Owner;
                Owner = null;
                owner?.Pop(this);
            }
        }

        private readonly SoundSettings _settings;
        private readonly List<Layer> _layers = new();
        private readonly HashSet<string> _warned = new();
        private string _base;

        public AudioMixerSnapshotService(SoundSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool IsAvailable => _settings.Mixer != null;
        public string Current { get; private set; }

        public bool TransitionTo(string snapshotName, float duration = 0f)
        {
            if (!IsAvailable || !Exists(snapshotName)) return false;
            _base = snapshotName;
            if (_layers.Count == 0) Apply(snapshotName, duration);
            return true;
        }

        public void ResetSnapshot(float duration = 0f)
        {
            if (!IsAvailable) return;
            var name = _settings.DefaultSnapshotName;
            if (string.IsNullOrEmpty(name)) return;
            TransitionTo(name, duration);
        }

        public IDisposable Push(string snapshotName, float duration = 0f)
        {
            if (!IsAvailable || !Exists(snapshotName)) return Disposable.Empty;
            var layer = new Layer { Owner = this, Name = snapshotName, Duration = duration };
            _layers.Add(layer);
            Apply(snapshotName, duration);
            return layer;
        }

        private void Pop(Layer layer)
        {
            var index = _layers.IndexOf(layer);
            if (index < 0) return;
            var wasTop = index == _layers.Count - 1;
            _layers.RemoveAt(index);
            // 一番上でなければ見た目は変わらない (後から Push されたものが効いている)
            if (!wasTop || !IsAvailable) return;

            var next = _layers.Count > 0 ? _layers[_layers.Count - 1].Name : _base ?? _settings.DefaultSnapshotName;
            if (!string.IsNullOrEmpty(next) && Exists(next)) Apply(next, layer.Duration);
        }

        private bool Exists(string snapshotName)
        {
            if (!string.IsNullOrEmpty(snapshotName) && _settings.Mixer.FindSnapshot(snapshotName) != null) return true;
            if (_warned.Add(snapshotName ?? string.Empty))
            {
                Debug.LogWarning($"[Abubu.Audio] AudioMixer \"{_settings.Mixer.name}\" にスナップショット \"{snapshotName}\" がありません。");
            }
            return false;
        }

        private void Apply(string snapshotName, float duration)
        {
            _settings.Mixer.FindSnapshot(snapshotName).TransitionTo(Mathf.Max(0f, duration));
            Current = snapshotName;
        }

        /// <summary>何もしない IDisposable (ミキサーが無いときの Push の戻り値)</summary>
        private static class Disposable
        {
            public static readonly IDisposable Empty = new EmptyDisposable();

            private sealed class EmptyDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}
