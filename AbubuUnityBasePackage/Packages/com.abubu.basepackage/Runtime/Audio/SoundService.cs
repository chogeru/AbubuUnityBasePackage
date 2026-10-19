using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Abubu.Audio
{
    /// <summary>
    /// ISoundService の既定の実装。各プレイヤーをまとめ、先読み / 解放 / カテゴリ単位の停止を提供する。
    /// <code>
    /// await sound.PreloadAsync(SoundCategory.Se);   // ロード画面で SE を先読み
    /// sound.StopAll();                               // 全部止める
    /// </code>
    /// </summary>
    public sealed class SoundService : ISoundService
    {
        private readonly AudioRoot _root;

        public IBgmPlayer Bgm { get; }
        public ISePlayer Se { get; }
        public IAmbientPlayer Ambient { get; }
        public IVoicePlayer Voice { get; }
        /// <summary>音量設定 (既定の実装)。IAudioVolumeModel を独自実装に差し替えている場合は null (VolumeModel を使う)</summary>
        public AudioVolumeModel Volume { get; }
        /// <summary>音量設定 (差し替え後の実装を含む)</summary>
        public IAudioVolumeModel VolumeModel { get; }
        public IAudioClipProvider Clips => _root != null ? _root.Clips : DirectAudioClipProvider.Instance;
        public IAudioMixerSnapshotService Snapshots { get; }

        /// <param name="root">先読み対象の SoundLibrary と Clip の取り出し方。null なら先読みは何もしない</param>
        /// <param name="snapshots">null なら root の SoundSettings から AudioMixerSnapshotService を作る</param>
        public SoundService(IBgmPlayer bgm, ISePlayer se, IAmbientPlayer ambient, IVoicePlayer voice, IAudioVolumeModel volume,
            AudioRoot root = null, IAudioMixerSnapshotService snapshots = null)
        {
            Bgm = bgm;
            Se = se;
            Ambient = ambient;
            Voice = voice;
            VolumeModel = volume;
            Volume = volume as AudioVolumeModel;
            _root = root;
            Snapshots = snapshots ?? new AudioMixerSnapshotService(root != null ? root.Settings : new SoundSettings());
        }

        public UniTask PreloadAsync(SoundCategory category, IReadOnlyList<string> keys = null, CancellationToken ct = default)
        {
            if (_root == null) return UniTask.CompletedTask;
            var entries = CollectEntries(category, keys);
            return entries.Count > 0 ? _root.Clips.PreloadAsync(entries, ct) : UniTask.CompletedTask;
        }

        public void Unload(SoundCategory category, IReadOnlyList<string> keys = null)
        {
            if (_root == null) return;
            var entries = CollectEntries(category, keys);
            if (entries.Count > 0) _root.Clips.Unload(entries);
        }

        public void StopAll(SoundCategory category, float? fadeDuration = null)
        {
            switch (category)
            {
                case SoundCategory.Bgm:
                    Bgm.StopAsync(fadeDuration).Forget();
                    break;
                case SoundCategory.Se:
                    Se.StopAll();
                    break;
                case SoundCategory.Ambient:
                    Ambient.StopAllAsync(fadeDuration).Forget();
                    break;
                case SoundCategory.Voice:
                    Voice.Stop();
                    break;
            }
        }

        public void StopAll(float? fadeDuration = null)
        {
            StopAll(SoundCategory.Bgm, fadeDuration);
            StopAll(SoundCategory.Se, fadeDuration);
            StopAll(SoundCategory.Ambient, fadeDuration);
            StopAll(SoundCategory.Voice, fadeDuration);
        }

        private List<SoundEntry> CollectEntries(SoundCategory category, IReadOnlyList<string> keys)
        {
            var library = _root.Library;
            var entries = new List<SoundEntry>();
            if (keys == null)
            {
                foreach (var key in library.GetKeys(category))
                {
                    if (library.TryGet(category, key, out var entry)) entries.Add(entry);
                }
                return entries;
            }

            foreach (var key in keys)
            {
                // 見つからないキーは TryResolve が候補付きで警告する
                if (_root.TryResolve(category, key, out var entry)) entries.Add(entry);
            }
            return entries;
        }
    }
}
