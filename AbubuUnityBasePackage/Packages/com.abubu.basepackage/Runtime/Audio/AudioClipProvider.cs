using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// SoundEntry から再生する AudioClip を取り出す仕組み。各プレイヤー (BGM / SE / 環境音 / ボイス) はすべてこれを経由する。
    /// 既定は <see cref="DirectAudioClipProvider"/> (SoundEntry.Clips の直接参照。従来どおり同期・常駐)。
    /// Addressables 導入時に SoundSettings.UseAddressableClips をオンにすると、SoundEntry.Address から必要なときに読み込む実装に切り替わる。
    /// 独自の読み込み方 (Resources / AssetBundle / ストリーミングなど) を使う場合はこれを実装して DI に登録する。
    /// <code>
    /// // ステージ開始前に BGM と SE を先読み (ロード画面で)
    /// await sound.PreloadAsync(SoundCategory.Se);
    /// await sound.PreloadAsync(SoundCategory.Bgm, new[] { "stage1" });
    /// // ステージ終了時に解放 (再生中のものは再生が終わるまで保持される)
    /// sound.Unload(SoundCategory.Se);
    ///
    /// // 独自実装への差し替え (AbubuInstaller より前の Installer で)
    /// Container.Bind&lt;IAudioClipProvider&gt;().To&lt;MyClipProvider&gt;().AsSingle();
    /// </code>
    /// 参照カウントの約束:
    /// TryAcquire / AcquireAsync が成功するたびに参照が 1 増え、Release で 1 減る。0 になった Clip は実装が解放してよい。
    /// PreloadAsync はエントリごとに参照を 1 つだけ保持し (何度呼んでも 1 つ)、Unload でそれを返す。
    /// </summary>
    public interface IAudioClipProvider
    {
        /// <summary>
        /// すでに読み込み済みなら参照を 1 つ取得して Clip を返す (同期・アロケーションなし)。
        /// Clips が複数あればランダムに選ぶ。未ロードなら false を返し、読み込みは開始しない
        /// </summary>
        bool TryAcquire(SoundEntry entry, out AudioClip clip);

        /// <summary>
        /// 読み込んで参照を 1 つ取得する。読み込み済みなら即完了する。
        /// 失敗した場合は null (参照は増えない。ログは実装が出す)。キャンセル時は OperationCanceledException (参照は増えない)
        /// </summary>
        UniTask<AudioClip> AcquireAsync(SoundEntry entry, CancellationToken ct = default);

        /// <summary>TryAcquire / AcquireAsync で取得した参照を 1 つ返す</summary>
        void Release(SoundEntry entry);

        /// <summary>同期で再生できる状態 (読み込み済み) か</summary>
        bool IsLoaded(SoundEntry entry);

        /// <summary>まとめて先読みし、Unload まで保持する。失敗したエントリはログを出して飛ばす</summary>
        UniTask PreloadAsync(IReadOnlyList<SoundEntry> entries, CancellationToken ct = default);

        /// <summary>PreloadAsync で保持した参照を返す (再生中のものは再生が終わった時点で解放される)</summary>
        void Unload(IReadOnlyList<SoundEntry> entries);
    }

    /// <summary>
    /// SoundEntry.Clips の直接参照をそのまま返す既定の実装。常に読み込み済みで、参照カウントは持たない (Clip は SoundLibrary と一緒に常駐する)。
    /// Address だけが設定されたエントリは再生できない (TryAcquire が false、AcquireAsync が null)。
    /// <code>
    /// var clips = DirectAudioClipProvider.Instance;
    /// if (clips.TryAcquire(entry, out var clip)) source.PlayOneShot(clip);
    /// </code>
    /// </summary>
    public sealed class DirectAudioClipProvider : IAudioClipProvider
    {
        public static readonly DirectAudioClipProvider Instance = new();

        private readonly HashSet<string> _warned = new();

        public bool TryAcquire(SoundEntry entry, out AudioClip clip)
        {
            clip = entry?.PickClip();
            return clip != null;
        }

        public UniTask<AudioClip> AcquireAsync(SoundEntry entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var clip = entry?.PickClip();
            if (clip == null && entry != null && entry.HasAddress && _warned.Add(entry.Key ?? string.Empty))
            {
                Debug.LogWarning($"[Abubu.Audio] \"{entry.Key}\" は Address (\"{entry.Address}\") だけが設定されていますが、Address から読み込む IAudioClipProvider が有効ではありません。" +
                                 "Addressables を導入して SoundSettings.UseAddressableClips をオンにするか、Clips を設定してください。");
            }
            return UniTask.FromResult(clip);
        }

        public void Release(SoundEntry entry) { }

        public bool IsLoaded(SoundEntry entry) => entry != null && entry.HasClip;

        public UniTask PreloadAsync(IReadOnlyList<SoundEntry> entries, CancellationToken ct = default) => UniTask.CompletedTask;

        public void Unload(IReadOnlyList<SoundEntry> entries) { }
    }
}
