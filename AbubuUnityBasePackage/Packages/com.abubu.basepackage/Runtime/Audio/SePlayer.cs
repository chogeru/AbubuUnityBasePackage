using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Audio
{
    /// <summary>
    /// AudioSource をプールして使い回す SE プレイヤー。
    /// 再生が終わった AudioSource は Tick で自動的にプールへ返却される。
    /// Clip は AudioRoot.Clips (IAudioClipProvider) から取り出す。読み込み済み (直接参照なら常に) なら同期・アロケーションなしで鳴る。
    /// 未ロードの場合は SoundSettings.SeNotLoaded に従う (既定: 読み込んで SeLoadTimeout 以内なら鳴らす)。
    /// PlayAttached は動く物体に追従する SE (エンジン音・飛翔体など)。追従先が破棄されたら止めてプールへ返す。
    /// <code>
    /// se.Play("jump");
    /// se.Play("explosion", transform.position);
    /// var handle = se.PlayAttached("engine", car.transform);
    /// se.Stop(handle);
    /// </code>
    /// </summary>
    public sealed class SePlayer : ISePlayer, ITickable, ILateTickable, IDisposable
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public float EntryVolume;
            public int Priority;
            /// <summary>プールへ返却されるたびに増える。PlayAsync / SeHandle が「自分の再生が終わったか」を判定するのに使う</summary>
            public int Id;
            /// <summary>Clip の参照を取得しているエントリ (返却時に Release する)。直接 Clip を渡した場合は null</summary>
            public SoundEntry Acquired;
            /// <summary>Clip を読み込み中 (まだ鳴っていないが返却しない)</summary>
            public bool Loading;
            /// <summary>追従先 (PlayAttached)。Following が true で Follow が null になったら破棄されたとみなす</summary>
            public Transform Follow;
            public bool Following;
        }

        private readonly AudioRoot _root;
        private readonly SoundSettings _settings;
        private readonly AudioObjectPool<Voice> _pool;
        private readonly List<Voice> _active = new();
        /// <summary>
        /// 一時停止中の SE。SetPaused(true) の時点で読み込み中だったものも含む
        /// (読み込みが終わった時点で鳴らしてすぐ一時停止し、SetPaused(false) で再開する)
        /// </summary>
        private readonly HashSet<Voice> _paused = new();
        private readonly CooldownGate _cooldown = new();
        private readonly HashSet<string> _warned = new();
        private readonly IDisposable _volumeSubscription;
        private float _categoryVolume = 1f;
        /// <summary>追従中の SE の数 (0 なら LateTick を素通りする)</summary>
        private int _followingCount;
        private int _serial;
        private bool _disposed;

        public SePlayer(AudioRoot root, SoundSettings settings, IAudioVolumeModel volume)
        {
            _root = root;
            _settings = settings;

            _pool = new AudioObjectPool<Voice>(
                create: () => new Voice { Source = root.CreateSource($"SE_{_serial++}", SoundCategory.Se) },
                onGet: static v => v.Source.gameObject.SetActive(true),
                onRelease: OnReleaseVoice,
                onDestroy: static v =>
                {
                    if (v.Source != null) AudioRoot.DestroyObject(v.Source.gameObject);
                });
            _pool.Prewarm(settings.SePoolPrewarm);

            _volumeSubscription = volume.GetSourceVolume(settings, SoundCategory.Se).Subscribe(this, static (v, self) =>
            {
                self._categoryVolume = v;
                foreach (var voice in self._active) voice.Source.volume = voice.EntryVolume * v;
            });
        }

        private void OnReleaseVoice(Voice v)
        {
            v.Id++;
            v.Loading = false;
            if (v.Following) _followingCount--;
            v.Following = false;
            v.Follow = null;
            var acquired = v.Acquired;
            v.Acquired = null;
            if (acquired != null) _root.Clips.Release(acquired);
            if (v.Source == null) return;
            v.Source.Stop();
            v.Source.clip = null;
            v.Source.transform.localPosition = Vector3.zero;
            v.Source.gameObject.SetActive(false);
        }

        public void Play(string key)
        {
            if (TryResolve(key, out var entry)) PlayEntry(key, entry, null, null);
        }

        public void Play(string key, Vector3 position)
        {
            if (TryResolve(key, out var entry)) PlayEntry(key, entry, position, null);
        }

        public void Play(AudioClip clip, float volume = 1f)
        {
            // SoundEntry を作らずに直接鳴らす (呼び出しごとの GC アロケーションを避ける)
            if (clip == null) return;
            var voice = Reserve(volume, 0);
            if (voice != null) StartVoice(voice, clip, null, 1f, 0f, null);
        }

        public SeHandle PlayAttached(string key, Transform target)
        {
            if (target == null)
            {
                Debug.LogWarning($"[Abubu.Audio] SE \"{key}\" の追従先 (Transform) が null のため鳴らしません。");
                return default;
            }
            if (!TryResolve(key, out var entry)) return default;
            var voice = PlayEntry(key, entry, target.position, target);
            return voice != null ? new SeHandle(voice, voice.Id) : default;
        }

        public bool IsPlaying(SeHandle handle) => TryGetVoice(handle, out _);

        public void Stop(SeHandle handle)
        {
            if (!TryGetVoice(handle, out var voice)) return;
            _active.Remove(voice);
            _paused.Remove(voice);
            _pool.Release(voice);
        }

        public async UniTask PlayAsync(string key, CancellationToken ct = default)
        {
            if (!TryResolve(key, out var entry)) return;
            // 待つ前提の呼び出しなので、未ロードでも SeNotLoaded / SeLoadTimeout に関係なく読み込んでから鳴らす
            var voice = PlayEntry(key, entry, null, null, waitLoad: true);
            if (voice == null) return;

            // Tick が再生終了 (ポーズ中・読み込み中は除く) を検知してプールへ返却すると Id が変わる
            var id = voice.Id;
            await UniTask.WaitUntil((voice, id), static s => s.voice.Id != s.id, cancellationToken: ct)
                .SuppressCancellationThrow();
        }

        public void StopAll()
        {
            foreach (var voice in _active) _pool.Release(voice);
            _active.Clear();
            _paused.Clear();
        }

        public void SetPaused(bool paused)
        {
            if (paused)
            {
                foreach (var voice in _active)
                {
                    // 読み込み中のものは、読み込み後に一時停止した状態で始める (LoadThenPlayAsync)
                    if (voice.Loading)
                    {
                        _paused.Add(voice);
                        continue;
                    }
                    if (!voice.Source.isPlaying || !_paused.Add(voice)) continue;
                    voice.Source.Pause();
                }
            }
            else
            {
                foreach (var voice in _paused)
                {
                    // 読み込み中のものはまだ鳴っていないので、外すだけ (読み込み後に普通に鳴る)
                    if (!voice.Loading) voice.Source.UnPause();
                }
                _paused.Clear();
            }
        }

        void ITickable.Tick()
        {
            if (_disposed || AudioListener.pause) return;

            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var voice = _active[i];
                // 追従先が破棄されたら (読み込み中・一時停止中でも) 止める
                var lostTarget = voice.Following && voice.Follow == null;
                // 一時停止中の SE は isPlaying が false になるが、終わったわけではないので返却しない
                if (!lostTarget && voice.Source != null && (voice.Loading || voice.Source.isPlaying || _paused.Contains(voice))) continue;
                _active.RemoveAt(i);
                _paused.Remove(voice);
                _pool.Release(voice);
            }
        }

        void ILateTickable.LateTick()
        {
            // 追従先の移動 (Update) を反映してから音を鳴らす位置を合わせる
            if (_disposed || _followingCount == 0) return;
            foreach (var voice in _active)
            {
                if (voice.Following && voice.Follow != null && voice.Source != null) voice.Source.transform.position = voice.Follow.position;
            }
        }

        private bool TryGetVoice(SeHandle handle, out Voice voice)
        {
            voice = handle.Voice as Voice;
            return voice != null && voice.Id == handle.Id && !_disposed;
        }

        private bool TryResolve(string key, out SoundEntry entry)
        {
            return _root.TryResolve(SoundCategory.Se, key, out entry) && _cooldown.TryPass(key, entry.Cooldown);
        }

        /// <summary>読み込み済みならすぐ鳴らす。未ロードなら SeNotLoaded に従う (waitLoad なら必ず読み込んでから鳴らす)</summary>
        private Voice PlayEntry(string key, SoundEntry entry, Vector3? position, Transform follow, bool waitLoad = false)
        {
            if (_disposed) return null;

            var clips = _root.Clips;
            if (clips.TryAcquire(entry, out var clip))
            {
                var voice = Reserve(entry.Volume, entry.Priority);
                if (voice == null)
                {
                    clips.Release(entry);
                    return null;
                }
                StartVoice(voice, clip, entry, entry.PickPitch(), entry.SpatialBlend, position);
                SetFollow(voice, follow);
                return voice;
            }

            if (!waitLoad && _settings.SeNotLoaded == SeNotLoadedPolicy.Skip)
            {
                if (_warned.Add(key)) Debug.LogWarning($"[Abubu.Audio] SE \"{key}\" の Clip が読み込まれていないため鳴らしませんでした (SeNotLoaded = Skip)。ISoundService.PreloadAsync で先読みしてください。");
                return null;
            }

            // 先に AudioSource を確保しておく (ハンドルを返す・StopAll / Stop(handle) で取り消せるように)
            var pending = Reserve(entry.Volume, entry.Priority);
            if (pending == null) return null;
            pending.Loading = true;
            SetFollow(pending, follow);
            LoadThenPlayAsync(key, entry, pending, pending.Id, position, waitLoad ? -1f : _settings.SeLoadTimeout).Forget();
            return pending;
        }

        private async UniTaskVoid LoadThenPlayAsync(string key, SoundEntry entry, Voice voice, int id, Vector3? position, float timeout)
        {
            var requestedAt = Time.unscaledTime;
            var clips = _root.Clips;
            AudioClip clip;
            try
            {
                clip = await clips.AcquireAsync(entry);
            }
            catch (OperationCanceledException)
            {
                clip = null;
            }

            var stillMine = !_disposed && voice.Id == id;
            var late = timeout > 0f && Time.unscaledTime - requestedAt > timeout;
            if (clip == null || !stillMine || late)
            {
                if (clip != null) clips.Release(entry);
                if (stillMine)
                {
                    _active.Remove(voice);
                    _paused.Remove(voice);
                    _pool.Release(voice);
                }
                if (clip != null && stillMine && late && _warned.Add(key))
                {
                    Debug.LogWarning($"[Abubu.Audio] SE \"{key}\" の読み込みが {timeout} 秒以内に終わらなかったため鳴らしませんでした。ISoundService.PreloadAsync で先読みしてください。");
                }
                return;
            }

            voice.Loading = false;
            // 追従先は読み込み中に動いているので現在位置から鳴らす
            if (voice.Following && voice.Follow != null) position = voice.Follow.position;
            StartVoice(voice, clip, entry, entry.PickPitch(), entry.SpatialBlend, position);
            // 読み込み中に SetPaused(true) された場合は、鳴らしてすぐ一時停止する (SetPaused(false) で続きから鳴る)
            if (_paused.Contains(voice)) voice.Source.Pause();
        }

        private void SetFollow(Voice voice, Transform follow)
        {
            if (follow == null) return;
            voice.Follow = follow;
            voice.Following = true;
            _followingCount++;
        }

        /// <summary>同時発音数を確認して AudioSource を 1 つ確保する。上限で、優先度が全ての再生中の SE より低ければ null</summary>
        private Voice Reserve(float volume, int priority)
        {
            if (_disposed) return null;

            // 同時発音数を超えたら、優先度が最も低い (同じなら最も古い) SE を止める (ボイススティール)
            if (_active.Count >= Mathf.Max(1, _settings.MaxSeVoices))
            {
                var steal = 0;
                for (var i = 1; i < _active.Count; i++)
                {
                    if (_active[i].Priority < _active[steal].Priority) steal = i;
                }
                if (_active[steal].Priority > priority) return null;

                var stolen = _active[steal];
                _active.RemoveAt(steal);
                _paused.Remove(stolen);
                _pool.Release(stolen);
            }

            var voice = _pool.Get();
            voice.EntryVolume = volume;
            voice.Priority = priority;
            voice.Source.volume = volume * _categoryVolume;
            _active.Add(voice);
            return voice;
        }

        /// <param name="acquired">clip の参照を取得したエントリ (返却時に Release する)。直接 Clip なら null</param>
        private void StartVoice(Voice voice, AudioClip clip, SoundEntry acquired, float pitch, float spatialBlend, Vector3? position)
        {
            var source = voice.Source;
            voice.Acquired = acquired;
            source.clip = clip;
            source.pitch = pitch;
            source.volume = voice.EntryVolume * _categoryVolume;
            source.spatialBlend = position.HasValue ? spatialBlend : 0f;
            if (position.HasValue) source.transform.position = position.Value;
            source.Play();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _volumeSubscription.Dispose();
            _pool.Dispose();
            // Dispose 済みのプールへの Release は破棄になる
            foreach (var voice in _active) _pool.Release(voice);
            _active.Clear();
            _paused.Clear();
        }
    }
}
