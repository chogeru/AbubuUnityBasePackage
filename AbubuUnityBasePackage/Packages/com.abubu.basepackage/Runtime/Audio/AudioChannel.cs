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
        /// <summary>Stop のたびに増える。非同期の読み込み中に別の用途へ使い回されていないかの判定に使う</summary>
        public int Version { get; private set; }

        /// <summary>Entry の Clip 参照を返す先 (参照を取得していなければ null)</summary>
        private IAudioClipProvider _owner;

        private float _entryVolume = 1f;
        private float _fade = 1f;
        private float _categoryVolume = 1f;
        private MotionHandle _fadeHandle;

        public AudioChannel(AudioSource source)
        {
            Source = source;
            Source.playOnAwake = false;
        }

        /// <param name="owner">clip の参照を取得した provider (Stop / 次の Setup で返す)。返す必要がなければ null</param>
        public void Setup(SoundEntry entry, AudioClip clip, bool loop, IAudioClipProvider owner)
        {
            ReleaseClip();
            Entry = entry;
            _owner = owner;
            _entryVolume = entry.Volume;
            Source.clip = clip;
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
            Version++;
            if (Source != null)
            {
                Source.Stop();
                Source.clip = null;
            }
            ReleaseClip();
            Entry = null;
        }

        private void ReleaseClip()
        {
            var owner = _owner;
            _owner = null;
            if (owner != null && Entry != null) owner.Release(Entry);
        }

        private void Apply()
        {
            if (Source != null) Source.volume = _entryVolume * _fade * _categoryVolume;
        }
    }
}
