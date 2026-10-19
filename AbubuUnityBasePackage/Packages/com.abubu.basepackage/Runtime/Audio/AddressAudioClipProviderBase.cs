using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// SoundEntry.Address から AudioClip を読み込む <see cref="IAudioClipProvider"/> の共通部分
    /// (アドレス単位の参照カウント・同じアドレスの読み込みの共有・先読み / 解放)。
    /// 読み込み方 (Addressables / Resources / AssetBundle など) だけを実装すればよい。
    /// <code>
    /// public sealed class ResourcesAudioClipProvider : AddressAudioClipProviderBase
    /// {
    ///     protected override async UniTask&lt;AudioClip&gt; LoadClipAsync(string address) =&gt;
    ///         (AudioClip)await Resources.LoadAsync&lt;AudioClip&gt;(address);
    ///     protected override void ReleaseClip(string address, AudioClip clip)
    ///     {
    ///         if (clip != null) Resources.UnloadAsset(clip);
    ///     }
    /// }
    /// // AbubuInstaller より前の Installer で
    /// Container.Bind&lt;IAudioClipProvider&gt;().To&lt;ResourcesAudioClipProvider&gt;().AsSingle();
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>Clips (直接参照) が設定されたエントリはそちらを優先し、参照カウントも取らない (Address は無視)。</para>
    /// <para>同じアドレスは 1 つにまとめて参照カウントし、0 になった時点で <see cref="ReleaseClip"/> を呼ぶ。</para>
    /// <para>
    /// 読み込みに失敗したアドレスはエラーを出して null を返す (次の要求で再試行する)。
    /// <see cref="LoadClipAsync"/> は同期的に完了してもよい (読み込み済みのアセットなど)。
    /// </para>
    /// </remarks>
    public abstract class AddressAudioClipProviderBase : IAudioClipProvider
    {
        private sealed class Slot
        {
            public string Address;
            public int RefCount;
            /// <summary>読み込み完了まで null</summary>
            public AudioClip Clip;
            /// <summary>
            /// 読み込みの完了通知。同じアドレスの要求者が読み込み中に複数で待つため、複数の await に対応した CompletionSource を使う
            /// (UniTask.Preserve は完了前に 2 か所から await すると例外になる)
            /// </summary>
            public UniTaskCompletionSource<AudioClip> Loading;
        }

        private readonly Dictionary<string, Slot> _slots = new();
        private readonly HashSet<SoundEntry> _preloaded = new();

        /// <summary>
        /// アドレスの AudioClip を読み込む。要求者のキャンセルでは止めない (同じアドレスを待っている他の要求者がいるため)。
        /// 失敗時は例外を投げるか null を返す (どちらもエラーログを出して null 扱い。null の場合も <see cref="ReleaseClip"/> を呼ぶ)
        /// </summary>
        protected abstract UniTask<AudioClip> LoadClipAsync(string address);

        /// <summary>読み込んだ AudioClip を解放する (参照が 0 になったとき、または読み込み中に不要になったとき)</summary>
        /// <param name="address">読み込んだアドレス</param>
        /// <param name="clip">読み込んだ Clip (読み込みが null を返した場合は null)</param>
        protected abstract void ReleaseClip(string address, AudioClip clip);

        /// <summary>保持している (読み込み中を含む) アドレスの参照数。デバッグ・テスト用</summary>
        public int GetRefCount(string address) => address != null && _slots.TryGetValue(address, out var slot) ? slot.RefCount : 0;

        public bool TryAcquire(SoundEntry entry, out AudioClip clip)
        {
            if (entry == null || entry.HasClip) return DirectAudioClipProvider.Instance.TryAcquire(entry, out clip);
            if (entry.HasAddress && _slots.TryGetValue(entry.Address, out var slot) && slot.Clip != null)
            {
                slot.RefCount++;
                clip = slot.Clip;
                return true;
            }
            clip = null;
            return false;
        }

        public async UniTask<AudioClip> AcquireAsync(SoundEntry entry, CancellationToken ct = default)
        {
            if (entry == null || entry.HasClip || !entry.HasAddress) return await DirectAudioClipProvider.Instance.AcquireAsync(entry, ct);
            ct.ThrowIfCancellationRequested();

            // 参照は読み込みを始める前に取る。LoadClipAsync が同期的に完了した場合 (読み込み済みのアセットなど) に、
            // 完了処理が「誰も要求していない」と判断して解放してしまわないように
            if (_slots.TryGetValue(entry.Address, out var slot))
            {
                slot.RefCount++;
            }
            else
            {
                slot = new Slot { Address = entry.Address, RefCount = 1 };
                _slots.Add(entry.Address, slot);
                slot.Loading = new UniTaskCompletionSource<AudioClip>();
                RunLoadAsync(slot).Forget();
            }
            if (slot.Clip != null) return slot.Clip;

            try
            {
                // 読み込み自体は (他の要求者のために) 続けるので、待つのだけをやめる
                return await slot.Loading.Task.AttachExternalCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                ReleaseSlot(slot);
                throw;
            }
        }

        public void Release(SoundEntry entry)
        {
            if (entry == null || entry.HasClip || !entry.HasAddress) return;
            if (_slots.TryGetValue(entry.Address, out var slot)) ReleaseSlot(slot);
        }

        public bool IsLoaded(SoundEntry entry)
        {
            if (entry == null) return false;
            if (entry.HasClip) return true;
            return entry.HasAddress && _slots.TryGetValue(entry.Address, out var slot) && slot.Clip != null;
        }

        public async UniTask PreloadAsync(IReadOnlyList<SoundEntry> entries, CancellationToken ct = default)
        {
            if (entries == null || entries.Count == 0) return;
            var tasks = new List<UniTask>(entries.Count);
            foreach (var entry in entries)
            {
                // 直接参照のものは常駐しているので何もしない。同じエントリの先読みは 1 回分だけ保持する
                if (entry == null || entry.HasClip || !entry.HasAddress || !_preloaded.Add(entry)) continue;
                tasks.Add(PreloadOneAsync(entry, ct));
            }
            await UniTask.WhenAll(tasks);
        }

        private async UniTask PreloadOneAsync(SoundEntry entry, CancellationToken ct)
        {
            try
            {
                if (await AcquireAsync(entry, ct) == null) _preloaded.Remove(entry);
            }
            catch (OperationCanceledException)
            {
                _preloaded.Remove(entry);
                throw;
            }
        }

        public void Unload(IReadOnlyList<SoundEntry> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries)
            {
                if (entry != null && _preloaded.Remove(entry)) Release(entry);
            }
        }

        /// <summary>読み込みを実行し、結果を待っている全員に知らせる (LoadAsync は例外を外に出さない)</summary>
        private async UniTaskVoid RunLoadAsync(Slot slot)
        {
            var clip = await LoadAsync(slot);
            slot.Loading.TrySetResult(clip);
        }

        private async UniTask<AudioClip> LoadAsync(Slot slot)
        {
            AudioClip clip;
            try
            {
                clip = await LoadClipAsync(slot.Address);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Abubu.Audio] AudioClip \"{slot.Address}\" の読み込みに失敗しました: {e.Message}");
                RemoveSlot(slot);
                return null;
            }

            if (clip == null)
            {
                Debug.LogError($"[Abubu.Audio] AudioClip \"{slot.Address}\" の読み込みに失敗しました (AudioClip ではないか、見つかりません)。");
                RemoveSlot(slot);
                ReleaseClip(slot.Address, null);
                return null;
            }

            if (slot.RefCount <= 0)
            {
                // 読み込み中に全員が Release / キャンセルした
                RemoveSlot(slot);
                ReleaseClip(slot.Address, clip);
                return null;
            }
            slot.Clip = clip;
            return clip;
        }

        private void ReleaseSlot(Slot slot)
        {
            if (slot.RefCount <= 0) return;
            if (--slot.RefCount > 0) return;
            // 読み込み中なら LoadAsync の完了時に解放する
            if (slot.Clip == null) return;
            RemoveSlot(slot);
            var clip = slot.Clip;
            slot.Clip = null;
            ReleaseClip(slot.Address, clip);
        }

        private void RemoveSlot(Slot slot)
        {
            if (_slots.TryGetValue(slot.Address, out var current) && current == slot) _slots.Remove(slot.Address);
        }
    }
}
