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
    /// 時間の停止・再開は <see cref="ITimeScaleController"/> 経由で行う (既定は Time.timeScale を直接書き換える)。
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

    /// <summary>
    /// ポーズ時に時間を止める / 戻す方法。独自の時間管理 (ヒットストップ・スロー演出を一元管理するクラスなど) が
    /// ある場合は、これを実装して Installer で先にバインドすると PauseService がそれを使う。
    /// <code>
    /// Container.Bind&lt;ITimeScaleController&gt;().To&lt;MyTimeScaleController&gt;().AsSingle();
    /// </code>
    /// </summary>
    public interface ITimeScaleController
    {
        /// <summary>現在の時間の倍率。PauseService はポーズ時に 0 を設定し、再開時に元の値を設定する</summary>
        float TimeScale { get; set; }
    }

    /// <summary>Time.timeScale をそのまま読み書きする既定の <see cref="ITimeScaleController"/></summary>
    public sealed class UnityTimeScaleController : ITimeScaleController
    {
        /// <inheritdoc />
        public float TimeScale
        {
            get => Time.timeScale;
            set => Time.timeScale = value;
        }
    }

    /// <summary><see cref="IPauseService"/> の標準実装</summary>
    /// <remarks>
    /// timeScale の扱い (<see cref="PauseSettings.StopTimeScale"/> が true の場合):
    /// ・ポーズ開始時に現在の値を覚えて 0 にする。
    /// ・再開時、値がまだ 0 (= ポーズ中に誰も変えていない) なら覚えておいた値に戻す。
    /// ・ポーズ中に他のコード (ヒットストップやスロー演出など) が値を変えていた場合は、その新しい値を尊重してそのままにする
    ///   (古い値で上書きしてしまわないため)。
    /// ポーズ中に timeScale を変えるコードは、変更が即座にゲームを動かしてしまう点に注意 (IsPaused を見て控えるのが安全)。
    /// </remarks>
    public sealed class PauseService : IPauseService, IDisposable
    {
        private readonly PauseSettings _settings;
        private readonly ITimeScaleController _timeScale;
        private readonly HashSet<object> _owners = new();
        private readonly ReactiveProperty<bool> _isPaused = new(false);
        private float _timeScaleBeforePause = 1f;

        public ReadOnlyReactiveProperty<bool> IsPaused => _isPaused;

        /// <param name="settings">設定 (null なら既定値)</param>
        /// <param name="timeScale">時間の止め方 (null なら Time.timeScale を直接書き換える)</param>
        public PauseService(PauseSettings settings = null, ITimeScaleController timeScale = null)
        {
            _settings = settings ?? new PauseSettings();
            _timeScale = timeScale ?? new UnityTimeScaleController();
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
                    _timeScaleBeforePause = _timeScale.TimeScale;
                    _timeScale.TimeScale = 0f;
                }
                else if (_timeScale.TimeScale == 0f)
                {
                    // ポーズ中に誰も変えていない時だけ戻す (変えられていたらその値を尊重する)
                    _timeScale.TimeScale = _timeScaleBeforePause;
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
