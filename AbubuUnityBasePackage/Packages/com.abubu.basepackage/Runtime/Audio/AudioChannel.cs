using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// 1つの AudioSource を「定義音量 × フェード係数 × カテゴリ音量」で制御するラッパー。
    /// BGM のクロスフェードや環境音レイヤーのフェードイン/アウトに使う。
    /// </summary>
    internal sealed class AudioChannel
    {
        public AudioSource Source { get; }
        public SoundEntry Entry { get; private set; }
        public bool IsPlaying => Source != null && Source.isPlaying;

        private float _entryVolume = 1f;
        private float _fade = 1f;
        private float _categoryVolume = 1f;
        private MotionHandle _fadeHandle;

        public AudioChannel(AudioSource source)
        {
            Source = source;
            Source.playOnAwake = false;
        }

        public void Setup(SoundEntry entry, bool loop)
        {
            Entry = entry;
            _entryVolume = entry.Volume;
            Source.clip = entry.PickClip();
            Source.pitch = entry.PickPitch();
            Source.loop = loop;
            Apply();
        }

        public void SetCategoryVolume(float volume)
        {
            _categoryVolume = volume;
            Apply();
        }

        public void SetFadeImmediate(float value)
        {
            _fadeHandle.TryCancel();
            _fade = value;
            Apply();
        }

        /// <summary>
        /// フェード係数を to まで変化させる。timeScale の影響は受けない (ポーズ中もフェード可能)。
        /// 別のフェードで上書きされた場合やキャンセル時は例外を投げずに終了する。
        /// </summary>
        public async UniTask FadeAsync(float to, float duration, CancellationToken ct)
        {
            _fadeHandle.TryCancel();
            if (duration <= 0f)
            {
                _fade = to;
                Apply();
                return;
            }

            _fadeHandle = LMotion.Create(_fade, to, duration)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (x, self) =>
                {
                    self._fade = x;
                    self.Apply();
                });

            await _fadeHandle.ToUniTask(CancelBehavior.Cancel, ct).SuppressCancellationThrow();
        }

        public void Stop()
        {
            _fadeHandle.TryCancel();
            if (Source == null) return;
            Source.Stop();
            Source.clip = null;
            Entry = null;
        }

        private void Apply()
        {
            if (Source != null) Source.volume = _entryVolume * _fade * _categoryVolume;
        }
    }
}
