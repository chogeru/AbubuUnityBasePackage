using System;
using Abubu.App;
using Abubu.Audio;
using Abubu.Effects;
using Abubu.Graphics;
using Abubu.Pause;
using Abubu.Pool;
using Abubu.Save;
using Abubu.Scene;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace Abubu
{
    /// <summary>
    /// モジュール間の連携をまとめる場所 (Composition Root)。
    /// 各モジュールは互いを直接参照せず、ここで結線する。
    /// </summary>
    internal sealed class PoolSceneBridge : IInitializable, IDisposable
    {
        private readonly ISceneService _scene;
        private readonly IPoolService _pool;
        private IDisposable _subscription;

        public PoolSceneBridge(ISceneService scene, IPoolService pool)
        {
            _scene = scene;
            _pool = pool;
        }

        public void Initialize() =>
            _subscription = _scene.OnBeforeSceneUnload.Subscribe(this, static (_, self) =>
            {
                // 旧シーンの親と一緒に破棄される前に、シーン側で貸し出し中のオブジェクトをプールへ回収する (DontDestroyOnLoad 側のものは残す)
                for (var i = 0; i < SceneManager.sceneCount; i++) self._pool.ReturnAll(SceneManager.GetSceneAt(i));
            });

        public void Dispose() => _subscription?.Dispose();
    }

    /// <summary>シーン遷移時にすべてのポーズを解除する (AbubuIntegrationSettings.ResumePauseOnSceneChange)</summary>
    internal sealed class PauseSceneBridge : IInitializable, IDisposable
    {
        private readonly ISceneService _scene;
        private readonly IPauseService _pause;
        private IDisposable _subscription;

        public PauseSceneBridge(ISceneService scene, IPauseService pause)
        {
            _scene = scene;
            _pause = pause;
        }

        // ポーズメニューごとシーンが破棄されると Resume されずに残るため、遷移時に解除する
        public void Initialize() =>
            _subscription = _scene.OnBeforeSceneUnload.Subscribe(this, static (_, self) => self._pause.ResumeAll());

        public void Dispose() => _subscription?.Dispose();
    }

    /// <summary>ポーズ状態を BGM / SE / 環境音 / ボイスに反映する (SoundSettings の Pause* に従う)</summary>
    internal sealed class AudioPauseBridge : IInitializable, IDisposable
    {
        private readonly IPauseService _pause;
        private readonly ISoundService _sound;
        private readonly SoundSettings _settings;
        private IDisposable _subscription;
        private IDisposable _pauseSnapshot;

        public AudioPauseBridge(IPauseService pause, ISoundService sound, SoundSettings settings)
        {
            _pause = pause;
            _sound = sound;
            _settings = settings;
        }

        public void Initialize() =>
            _subscription = _pause.IsPaused.Skip(1).Subscribe(this, static (paused, self) => self.Apply(paused));

        private void Apply(bool paused)
        {
            if (_settings.PauseBgm)
            {
                if (paused) _sound.Bgm.Pause();
                else _sound.Bgm.Resume();
            }
            if (_settings.PauseSe) _sound.Se.SetPaused(paused);
            if (_settings.PauseAmbient) _sound.Ambient.SetPaused(paused);
            if (_settings.PauseVoice) _sound.Voice.SetPaused(paused);

            // ポーズ中だけミキサーのスナップショットを切り替える (SoundSettings.PauseSnapshotName)
            if (!string.IsNullOrEmpty(_settings.PauseSnapshotName))
            {
                if (paused)
                {
                    _pauseSnapshot ??= _sound.Snapshots.Push(_settings.PauseSnapshotName, _settings.PauseSnapshotDuration);
                }
                else
                {
                    _pauseSnapshot?.Dispose();
                    _pauseSnapshot = null;
                }
            }
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _pauseSnapshot?.Dispose();
        }
    }

    /// <summary>EffectLibrary の SeKey をエフェクトの位置で鳴らす</summary>
    internal sealed class EffectSeHook : IEffectPlayHook
    {
        private readonly ISoundService _sound;

        // ISoundService を差し替えても動くよう、ISePlayer ではなく ISoundService.Se を使う
        public EffectSeHook(ISoundService sound) => _sound = sound;

        public void OnPlay(EffectEntry entry, GameObject instance, Vector3 position)
        {
            if (!string.IsNullOrEmpty(entry.SeKey)) _sound.Se?.Play(entry.SeKey, position);
        }
    }

    /// <summary>中断時に、パッケージが持つ設定 (音量・画面設定) を保存する (AppSettings.SaveSettingsOnSuspend)</summary>
    /// <remarks>どちらも変更を 0.5 秒待ってから保存するため、その間にアプリが落とされると変更が失われる</remarks>
    internal sealed class SettingsSaveOnSuspend : IAppSuspendHandler
    {
        private readonly AudioVolumeStore _volume;
        private readonly GraphicsSettingsStore _graphics;

        /// <remarks>サウンド・画面設定の機能が無効なら、その分は保存しない</remarks>
        public SettingsSaveOnSuspend([InjectOptional] AudioVolumeStore volume = null, [InjectOptional] GraphicsSettingsStore graphics = null)
        {
            _volume = volume;
            _graphics = graphics;
        }

        public void OnSuspend()
        {
            _volume?.Save();
            _graphics?.Save();
        }
    }

    /// <summary>
    /// 中断時・終了時に、バックグラウンドで書き込み中のセーブを終わらせる (書き込みの途中でアプリが落とされないように)。
    /// 中断のきっかけ (SuspendTriggers) に Quit が含まれていなくても、終了時は必ず待つ
    /// </summary>
    /// <remarks>IAppLifecycle を受け取らない (AppLifecycleService がこの一覧を受け取るので、受け取ると循環参照になる)</remarks>
    internal sealed class SaveFlushOnSuspend : IAppSuspendFinalizer
    {
        private readonly ISaveService _save;

        public SaveFlushOnSuspend(ISaveService save) => _save = save;

        // 中断時の保存 (IAppSuspendHandler・OnSuspend の購読) がすべて終わってから呼ばれるので、そこで始まった非同期の書き込みも待てる
        public void OnFinalizeSuspend(bool quitting) => _save.Flush();
    }

    /// <summary>フォーカスが外れている間のポーズ・消音 (AppSettings.PauseOnFocusLost / MuteOnFocusLost)</summary>
    internal sealed class AppFocusBridge : IInitializable, IDisposable
    {
        private readonly IAppLifecycle _app;
        private readonly IPauseService _pause;
        private readonly AppSettings _settings;
        private IDisposable _subscription;
        private float _volumeBeforeMute = 1f;
        private bool _muted;

        public AppFocusBridge(IAppLifecycle app, IPauseService pause, AppSettings settings)
        {
            _app = app;
            _pause = pause;
            _settings = settings;
        }

        public void Initialize()
        {
            if (!_settings.PauseOnFocusLost && !_settings.MuteOnFocusLost) return;
            _subscription = _app.IsFocused.Skip(1).Subscribe(this, static (focused, self) => self.Apply(focused));
        }

        private void Apply(bool focused)
        {
            if (_settings.PauseOnFocusLost)
            {
                if (focused) _pause.Resume(this);
                else _pause.Pause(this);
            }

            if (_settings.MuteOnFocusLost)
            {
                if (!focused && !_muted)
                {
                    _volumeBeforeMute = AudioListener.volume;
                    AudioListener.volume = 0f;
                    _muted = true;
                }
                else if (focused && _muted)
                {
                    AudioListener.volume = _volumeBeforeMute;
                    _muted = false;
                }
            }
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            if (_muted) AudioListener.volume = _volumeBeforeMute;
            _muted = false;
        }
    }
}
