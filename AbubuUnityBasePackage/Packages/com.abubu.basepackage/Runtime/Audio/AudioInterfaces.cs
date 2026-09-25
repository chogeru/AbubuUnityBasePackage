using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace Abubu.Audio
{
    public interface IBgmPlayer
    {
        /// <summary>再生中の BGM キー (未再生なら null)</summary>
        ReadOnlyReactiveProperty<string> CurrentKey { get; }

        /// <summary>BGM をクロスフェードで切り替える。同じキーが再生中なら何もしない。fadeDuration 省略時は設定値</summary>
        UniTask PlayAsync(string key, float? fadeDuration = null, CancellationToken ct = default);
        UniTask PlayAsync(AudioClip clip, float volume = 1f, float? fadeDuration = null, CancellationToken ct = default);
        UniTask StopAsync(float? fadeDuration = null, CancellationToken ct = default);
        void Pause();
        void Resume();
    }

    public interface ISePlayer
    {
        /// <summary>SE を鳴らす (2D)。存在しないキーは警告のみ。</summary>
        void Play(string key);
        /// <summary>SE をワールド座標で鳴らす (SpatialBlend &gt; 0 の定義で 3D 音響になる)</summary>
        void Play(string key, Vector3 position);
        void Play(AudioClip clip, float volume = 1f);
        /// <summary>SE を鳴らし、再生終了まで待つ</summary>
        UniTask PlayAsync(string key, CancellationToken ct = default);
        void StopAll();
        /// <summary>
        /// 再生中の SE を一時停止/再開する。一時停止中も新しい SE は鳴らせる (ポーズメニューのクリック音など)
        /// </summary>
        void SetPaused(bool paused);
    }

    public interface IAmbientPlayer
    {
        /// <summary>環境音レイヤーを追加 (フェードイン)。複数同時再生可。</summary>
        UniTask PlayAsync(string key, float? fadeDuration = null, CancellationToken ct = default);
        UniTask StopAsync(string key, float? fadeDuration = null, CancellationToken ct = default);
        UniTask StopAllAsync(float? fadeDuration = null, CancellationToken ct = default);
        bool IsPlaying(string key);
        /// <summary>全レイヤーを一時停止/再開する</summary>
        void SetPaused(bool paused);
    }

    /// <summary>サウンド機能の窓口。基本はこれ1つを Inject すれば良い。</summary>
    public interface ISoundService
    {
        IBgmPlayer Bgm { get; }
        ISePlayer Se { get; }
        IAmbientPlayer Ambient { get; }
        AudioVolumeModel Volume { get; }
    }
}
