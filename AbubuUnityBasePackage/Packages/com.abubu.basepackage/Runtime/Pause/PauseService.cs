using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace Abubu.Pause
{
    [Serializable]
    public sealed class PauseSettings
    {
        [Tooltip("ポーズ中に Time.timeScale を 0 にする")]
        public bool StopTimeScale = true;
    }

    /// <summary>
    /// ポーズ状態の管理。誰がポーズしているか (owner) を数え、全員が解除した時だけ再開する。
    /// ポーズメニューの上に確認ダイアログを重ねても、片方を閉じただけでは再開しない。
    /// <code>
    /// _pause.Pause(this);   // 同じ owner で何度呼んでも 1 回として数える
    /// _pause.Resume(this);
    /// _pause.IsPaused.Subscribe(p => ...);
    /// </code>
    /// Time.timeScale を書き換えるのはこのクラスだけにする (他と取り合いにならないように)。
    /// 音・入力への反映は、それぞれのモジュールが IsPaused を購読して行う。
    /// </summary>
    public interface IPauseService
    {
        ReadOnlyReactiveProperty<bool> IsPaused { get; }
        void Pause(object owner);
        void Resume(object owner);
        bool IsPausedBy(object owner);
        /// <summary>全 owner のポーズを解除する (シーン遷移時など)</summary>
        void ResumeAll();
    }

    public sealed class PauseService : IPauseService, IDisposable
    {
        private readonly PauseSettings _settings;
        private readonly HashSet<object> _owners = new();
        private readonly ReactiveProperty<bool> _isPaused = new(false);
        private float _timeScaleBeforePause = 1f;

        public ReadOnlyReactiveProperty<bool> IsPaused => _isPaused;

        public PauseService(PauseSettings settings = null)
        {
            _settings = settings ?? new PauseSettings();
        }

        public void Pause(object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (!_owners.Add(owner) || _owners.Count > 1) return;
            Apply(true);
        }

        public void Resume(object owner)
        {
            if (owner == null || !_owners.Remove(owner) || _owners.Count > 0) return;
            Apply(false);
        }

        public bool IsPausedBy(object owner) => owner != null && _owners.Contains(owner);

        public void ResumeAll()
        {
            if (_owners.Count == 0) return;
            _owners.Clear();
            Apply(false);
        }

        private void Apply(bool paused)
        {
            if (_settings.StopTimeScale)
            {
                if (paused)
                {
                    _timeScaleBeforePause = Time.timeScale;
                    Time.timeScale = 0f;
                }
                else
                {
                    Time.timeScale = _timeScaleBeforePause;
                }
            }
            _isPaused.Value = paused;
        }

        public void Dispose()
        {
            ResumeAll();
            _isPaused.Dispose();
        }
    }
}
