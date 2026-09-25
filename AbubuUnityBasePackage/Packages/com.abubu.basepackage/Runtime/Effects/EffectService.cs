using System;
using System.Collections.Generic;
using Abubu.Pool;
using UnityEngine;
using Zenject;

namespace Abubu.Effects
{
    [Serializable]
    public sealed class EffectEntry
    {
        public string Key;
        public GameObject Prefab;
        [Tooltip("同時に鳴らす SE のキー (任意)。エフェクトの位置で再生される")]
        public string SeKey;
        [Tooltip("自動返却までの秒数。0 ならパーティクルの長さから自動計算 (ループするパーティクルは自動返却しない)")]
        [Min(0f)] public float Lifetime;
        [Tooltip("起動時にあらかじめ生成しておく数")]
        [Min(0)] public int Prewarm;
    }

    [CreateAssetMenu(menuName = "Abubu/Effect Library", fileName = "EffectLibrary")]
    public sealed class EffectLibrary : ScriptableObject
    {
        [SerializeField] private List<EffectEntry> effects = new();

        private Dictionary<string, EffectEntry> _cache;

        public IReadOnlyList<EffectEntry> Effects => effects;

        public bool TryGet(string key, out EffectEntry entry)
        {
            _cache ??= BuildCache();
            return _cache.TryGetValue(key, out entry);
        }

        private Dictionary<string, EffectEntry> BuildCache()
        {
            var cache = new Dictionary<string, EffectEntry>();
            foreach (var e in effects)
            {
                if (e != null && !string.IsNullOrEmpty(e.Key)) cache.TryAdd(e.Key, e);
            }
            return cache;
        }

        private void OnValidate() => _cache = null;
    }

    [Serializable]
    public sealed class EffectSettings
    {
        public EffectLibrary Library;
    }

    /// <summary>
    /// エフェクト (パーティクル等) をプールから出し、終わったら自動で返却する。SE も同時に鳴らせる。
    /// <code>
    /// _effect.Play("explosion", enemy.position);
    /// _effect.Play(hitPrefab, hitPoint, Quaternion.LookRotation(normal));
    /// </code>
    /// </summary>
    /// <summary>
    /// エフェクト再生時に割り込む処理 (SE を鳴らす、カメラを揺らす、など)。
    /// Container.Bind&lt;IEffectPlayHook&gt;().To&lt;MyHook&gt;().AsSingle() で追加できる。
    /// </summary>
    public interface IEffectPlayHook
    {
        void OnPlay(EffectEntry entry, GameObject instance, Vector3 position);
    }

    public interface IEffectService
    {
        GameObject Play(string key, Vector3 position, Quaternion? rotation = null, Transform parent = null);
        GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null, Transform parent = null, float lifetime = 0f);
        void Stop(GameObject instance);
    }

    public sealed class EffectService : IEffectService, IInitializable
    {
        private readonly IPoolService _pool;
        private readonly List<IEffectPlayHook> _hooks;
        private readonly EffectLibrary _library;
        private readonly Dictionary<GameObject, float> _durationCache = new();

        public EffectService(IPoolService pool, EffectSettings settings, List<IEffectPlayHook> hooks = null)
        {
            _pool = pool;
            _library = settings.Library;
            _hooks = hooks ?? new List<IEffectPlayHook>();
        }

        public void Initialize()
        {
            if (_library == null) return;
            foreach (var entry in _library.Effects)
            {
                if (entry.Prefab != null && entry.Prewarm > 0) _pool.Prewarm(entry.Prefab, entry.Prewarm);
            }
        }

        public GameObject Play(string key, Vector3 position, Quaternion? rotation = null, Transform parent = null)
        {
            if (_library == null || !_library.TryGet(key, out var entry) || entry.Prefab == null)
            {
                Debug.LogWarning($"[Abubu.Effect] \"{key}\" が EffectLibrary に見つからないか、Prefab が未設定です。");
                return null;
            }

            var instance = Play(entry.Prefab, position, rotation, parent, entry.Lifetime);
            foreach (var hook in _hooks) hook.OnPlay(entry, instance, position);
            return instance;
        }

        public GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null, Transform parent = null, float lifetime = 0f)
        {
            var instance = _pool.Rent(prefab, position, rotation ?? prefab.transform.rotation, parent);

            // ルートに ParticleSystem があれば子ごと 1 回で再生。無ければ最上位の ParticleSystem だけ再生する
            if (instance.TryGetComponent<ParticleSystem>(out var root))
            {
                root.Clear(true);
                root.Play(true);
            }
            else
            {
                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
                {
                    var psParent = ps.transform.parent;
                    if (psParent != null && psParent.GetComponentInParent<ParticleSystem>() != null) continue;
                    ps.Clear(true);
                    ps.Play(true);
                }
            }

            var duration = lifetime > 0f ? lifetime : GetParticleDuration(prefab);
            if (duration > 0f) _pool.ReturnAfter(instance, duration);
            return instance;
        }

        public void Stop(GameObject instance) => _pool.Return(instance);

        /// <summary>パーティクルが消えきるまでの秒数。ループを含む場合は 0 (自動返却しない)</summary>
        private float GetParticleDuration(GameObject prefab)
        {
            if (_durationCache.TryGetValue(prefab, out var cached)) return cached;

            var duration = 0f;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (main.loop)
                {
                    duration = 0f;
                    break;
                }
                duration = Mathf.Max(duration, MaxOf(main.startDelay) + main.duration + MaxOf(main.startLifetime));
            }

            _durationCache[prefab] = duration;
            return duration;
        }

        // Curve モードでは constantMax が 0 になるため、curveMultiplier (カーブの最大倍率) を使う
        private static float MaxOf(ParticleSystem.MinMaxCurve curve) => curve.mode switch
        {
            ParticleSystemCurveMode.Constant => curve.constant,
            ParticleSystemCurveMode.TwoConstants => curve.constantMax,
            _ => curve.curveMultiplier,
        };
    }
}
