using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// BGM プレイヤー。1 曲だけを鳴らし、切り替え時はクロスフェードする。
    /// <code>
    /// await sound.Bgm.PlayAsync("stage1");
    /// sound.Bgm.CurrentKey.Subscribe(key => Debug.Log($"BGM: {key}"));
    /// </code>
    /// </summary>
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

    /// <summary>
    /// 効果音プレイヤー。複数同時に鳴らせる (上限は SoundSettings.MaxSeVoices)。
    /// <code>
    /// sound.Se.Play("jump");
    /// var engine = sound.Se.PlayAttached("engine", car.transform); // 追従。car が破棄されたら自動で止まる
    /// sound.Se.Stop(engine);
    /// </code>
    /// </summary>
    public interface ISePlayer
    {
        /// <summary>SE を鳴らす (2D)。存在しないキーは警告のみ。</summary>
        void Play(string key);
        /// <summary>SE をワールド座標で鳴らす (SpatialBlend &gt; 0 の定義で 3D 音響になる)</summary>
        void Play(string key, Vector3 position);
        void Play(AudioClip clip, float volume = 1f);
        /// <summary>SE を鳴らし、再生終了まで待つ</summary>
        UniTask PlayAsync(string key, CancellationToken ct = default);
        /// <summary>
        /// SE を target に追従させて鳴らす (動く物体のエンジン音など。SpatialBlend &gt; 0 の定義で 3D 音響になる)。
        /// target が破棄されると止めて AudioSource をプールに返す。ループ定義の音を止めるには戻り値を Stop に渡す。
        /// 鳴らせなかった場合は無効なハンドル (IsValid が false) を返す
        /// </summary>
        SeHandle PlayAttached(string key, Transform target);
        /// <summary>ハンドルの SE がまだ鳴っている (読み込み中・一時停止中を含む) か</summary>
        bool IsPlaying(SeHandle handle);
        /// <summary>ハンドルの SE を止める。終わっている / 無効なハンドルなら何もしない</summary>
        void Stop(SeHandle handle);
        void StopAll();
        /// <summary>
        /// 再生中の SE を一時停止/再開する。一時停止中も新しい SE は鳴らせる (ポーズメニューのクリック音など)
        /// </summary>
        void SetPaused(bool paused);
    }

    /// <summary>
    /// 環境音プレイヤー。複数のレイヤーを重ねて鳴らせる。
    /// <code>
    /// await sound.Ambient.PlayAsync("wind");
    /// await sound.Ambient.StopAllAsync();
    /// </code>
    /// </summary>
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

    /// <summary>
    /// ボイスプレイヤー。1 つずつ鳴らし、新しいボイスは前のボイスを置き換える。
    /// <code>
    /// await sound.Voice.PlayAsync("hero_001");
    /// sound.Voice.OnVoiceStarted.Subscribe(e => subtitle.Show(localization.Get(e.SubtitleKey), e.Duration));
    /// sound.Voice.OnVoiceEnded.Subscribe(_ => subtitle.Hide());
    /// </code>
    /// </summary>
    public interface IVoicePlayer
    {
        /// <summary>再生中のボイスキー (未再生なら null)</summary>
        ReadOnlyReactiveProperty<string> CurrentKey { get; }
        bool IsPlaying { get; }
        /// <summary>ボイスが鳴り始めたとき (Clip の読み込み待ちがあれば読み込み後)。字幕の表示に使う</summary>
        Observable<VoiceEvent> OnVoiceStarted { get; }
        /// <summary>ボイスが終わったとき (再生終了 / Stop / 次のボイスでの置き換え / 破棄)。置き換え時は次の OnVoiceStarted より先に通知される</summary>
        Observable<VoiceEvent> OnVoiceEnded { get; }

        /// <summary>ボイスを鳴らす。再生中のボイスがあれば止めて置き換える (重ならない)。存在しないキーは警告のみ</summary>
        void Play(string key);
        /// <summary>ボイスをワールド座標で鳴らす (SpatialBlend &gt; 0 の定義で 3D 音響になる)</summary>
        void Play(string key, Vector3 position);
        /// <summary>ボイスを鳴らし、再生終了まで待つ。別のボイスに置き換えられた / Stop された場合もそこで戻る</summary>
        UniTask PlayAsync(string key, CancellationToken ct = default);
        void Stop();
        void SetPaused(bool paused);
    }

    /// <summary>
    /// サウンド機能の窓口。基本はこれ1つを Inject すれば良い。
    /// <code>
    /// [Inject] private ISoundService _sound;
    /// _sound.Se.Play("jump");
    /// await _sound.PreloadAsync(SoundCategory.Bgm, new[] { "boss" }); // Address 指定のエントリを先読み
    /// _sound.Snapshots.TransitionTo("Underwater", 0.5f);
    /// _sound.StopAll(SoundCategory.Ambient);
    /// </code>
    /// </summary>
    public interface ISoundService
    {
        IBgmPlayer Bgm { get; }
        ISePlayer Se { get; }
        IAmbientPlayer Ambient { get; }
        IVoicePlayer Voice { get; }
        /// <summary>
        /// 音量設定。保存は AudioVolumeStore が行う。
        /// 依存を減らしたい場合は IAudioVolumeModel を直接 Inject する (IAudioVolumeModel を独自実装に差し替えている場合、ここは null)
        /// </summary>
        AudioVolumeModel Volume { get; }
        /// <summary>Clip の取り出し方 (既定は直接参照。SoundSettings.UseAddressableClips で Addressables)</summary>
        IAudioClipProvider Clips { get; }
        /// <summary>AudioMixer のスナップショット切り替え (ミキサー未設定なら何もしない)</summary>
        IAudioMixerSnapshotService Snapshots { get; }

        /// <summary>
        /// カテゴリのサウンドを先読みし、Unload まで保持する。keys が null なら SoundLibrary のそのカテゴリ全部 (includes 先を含む)。
        /// 直接参照 (既定) では何もしない (すでに常駐している)
        /// </summary>
        UniTask PreloadAsync(SoundCategory category, IReadOnlyList<string> keys = null, CancellationToken ct = default);
        /// <summary>PreloadAsync で保持したものを解放する。再生中のものは再生が終わった時点で解放される</summary>
        void Unload(SoundCategory category, IReadOnlyList<string> keys = null);
        /// <summary>カテゴリの音をすべて止める (BGM / 環境音は fadeDuration 省略時は設定値でフェードアウト。SE / ボイスは即停止)</summary>
        void StopAll(SoundCategory category, float? fadeDuration = null);
        /// <summary>全カテゴリの音を止める</summary>
        void StopAll(float? fadeDuration = null);
    }

    /// <summary>
    /// ISePlayer.PlayAttached の戻り値。SE を個別に止めるのに使う。
    /// 再生が終わった後 (AudioSource がプールへ戻った後) に使っても安全 (何もしない)。
    /// <code>
    /// var handle = sound.Se.PlayAttached("engine", transform);
    /// if (sound.Se.IsPlaying(handle)) sound.Se.Stop(handle);
    /// </code>
    /// </summary>
    public readonly struct SeHandle : System.IEquatable<SeHandle>
    {
        internal readonly object Voice;
        internal readonly int Id;

        internal SeHandle(object voice, int id)
        {
            Voice = voice;
            Id = id;
        }

        /// <summary>再生が開始された (鳴らせた) ハンドルか。終わったかどうかは ISePlayer.IsPlaying で調べる</summary>
        public bool IsValid => Voice != null;

        public bool Equals(SeHandle other) => ReferenceEquals(Voice, other.Voice) && Id == other.Id;
        public override bool Equals(object obj) => obj is SeHandle other && Equals(other);
        public override int GetHashCode() => ((Voice?.GetHashCode() ?? 0) * 397) ^ Id;
    }

    /// <summary>
    /// ボイスの開始・終了の通知内容 (IVoicePlayer.OnVoiceStarted / OnVoiceEnded)。
    /// <code>
    /// voice.OnVoiceStarted.Subscribe(e => subtitle.Show(e.SubtitleKey, e.Duration));
    /// </code>
    /// </summary>
    public readonly struct VoiceEvent
    {
        /// <summary>再生時に指定したキー</summary>
        public readonly string Key;
        public readonly SoundEntry Entry;
        /// <summary>再生にかかる秒数 (Clip の長さ / ピッチ)</summary>
        public readonly float Duration;
        /// <summary>終了通知で、最後まで鳴らずに止められた (Stop / 置き換え / 破棄) なら true。開始通知では常に false</summary>
        public readonly bool Interrupted;

        public VoiceEvent(string key, SoundEntry entry, float duration, bool interrupted)
        {
            Key = key;
            Entry = entry;
            Duration = duration;
            Interrupted = interrupted;
        }

        /// <summary>字幕に使うキー (SoundEntry.SubtitleKey。空なら Key)</summary>
        public string SubtitleKey => Entry != null ? Entry.GetSubtitleKey() : Key;
    }
}
