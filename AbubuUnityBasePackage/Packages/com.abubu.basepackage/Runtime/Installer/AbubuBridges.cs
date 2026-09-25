using System;
using Abubu.Audio;
using Abubu.Effects;
using Abubu.Pause;
using Abubu.Pool;
using Abubu.Scene;
using R3;
using UnityEngine;
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
        private readonly IPauseService _pause;
        private IDisposable _subscription;

        public PoolSceneBridge(ISceneService scene, IPoolService pool, IPauseService pause)
        {
            _scene = scene;
            _pool = pool;
            _pause = pause;
        }

        public void Initialize() =>
            _subscription = _scene.OnBeforeSceneUnload.Subscribe(this, static (_, self) =>
            {
                // 旧シーンの親と一緒に破棄される前に、貸し出し中のオブジェクトをプールへ回収する
                self._pool.ReturnAll();
                // ポーズメニューごとシーンが破棄されると Resume されずに残るため、遷移時に解除する
                self._pause.ResumeAll();
            });

        public void Dispose() => _subscription?.Dispose();
    }

    /// <summary>ポーズ状態を BGM / SE / 環境音に反映する (SoundSettings の Pause* に従う)</summary>
    internal sealed class AudioPauseBridge : IInitializable, IDisposable
    {
        private readonly IPauseService _pause;
        private readonly ISoundService _sound;
        private readonly SoundSettings _settings;
        private IDisposable _subscription;

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
        }

        public void Dispose() => _subscription?.Dispose();
    }

    /// <summary>EffectLibrary の SeKey をエフェクトの位置で鳴らす</summary>
    internal sealed class EffectSeHook : IEffectPlayHook
    {
        private readonly ISePlayer _se;

        public EffectSeHook(ISePlayer se) => _se = se;

        public void OnPlay(EffectEntry entry, GameObject instance, Vector3 position)
        {
            if (!string.IsNullOrEmpty(entry.SeKey)) _se.Play(entry.SeKey, position);
        }
    }
}
