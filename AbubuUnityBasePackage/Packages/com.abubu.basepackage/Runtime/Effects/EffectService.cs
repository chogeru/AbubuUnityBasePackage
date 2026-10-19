using System;
using System.Collections.Generic;
using Abubu.Pool;
using UnityEngine;
using Zenject;

namespace Abubu.Effects
{
    /// <summary><see cref="IEffectService"/> の標準実装。生成と返却は <see cref="IPoolService"/> に任せ、再生と寿命の判定だけを行う</summary>
    public sealed class EffectService : IEffectService, IInitializable
    {
        /// <summary>プレハブから計算した寿命。Looping は「自動返却しない」</summary>
        private readonly struct Duration
        {
            public readonly float Seconds;
            public readonly bool Looping;
            public readonly bool HasParticles;
            public readonly bool Unscaled;

            public Duration(float seconds, bool looping, bool hasParticles, bool unscaled)
            {
                Seconds = seconds;
                Looping = looping;
                HasParticles = hasParticles;
                Unscaled = unscaled;
            }
        }

        private readonly IPoolService _pool;
        private readonly List<IEffectPlayHook> _hooks;
        private readonly EffectSettings _settings;
        private readonly EffectLibrary _library;

        /// <summary>プレハブ → 寿命 (プレハブごとに 1 回だけ計算する)</summary>
        private readonly Dictionary<GameObject, Duration> _durations = new();

        /// <summary>
        /// インスタンス → 再生する最上位の ParticleSystem。再生のたびに GetComponentsInChildren しないためのキャッシュ。
        /// プールのインスタンスは使い回されるので、1 インスタンスにつき 1 回だけ集める
        /// </summary>
        private readonly Dictionary<GameObject, ParticleSystem[]> _particles = new();

        /// <summary>DefaultLifetime を使ったことを警告済みのプレハブ</summary>
        private readonly HashSet<GameObject> _warnedFallback = new();

        private readonly List<ParticleSystem> _psBuffer = new();
        private readonly List<GameObject> _keyBuffer = new();

        public EffectService(IPoolService pool, EffectSettings settings, List<IEffectPlayHook> hooks = null)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _settings = settings ?? new EffectSettings();
            _library = _settings.Library;
            _hooks = hooks ?? new List<IEffectPlayHook>();
        }

        /// <summary>EffectLibrary の Prewarm / MaxPooled をプールへ反映する</summary>
        public void Initialize()
        {
            if (_library == null) return;
            foreach (var entry in _library.AllEffects)
            {
                if (entry == null || entry.Prefab == null) continue;
                if (entry.MaxPooled > 0) _pool.Configure(entry.Prefab, new PoolOptions(entry.MaxPooled));
                if (entry.Prewarm > 0) _pool.Prewarm(entry.Prefab, entry.Prewarm);
            }
        }

        public GameObject Play(string key, Vector3 position, Quaternion? rotation = null, Transform parent = null)
        {
            if (_library == null || !_library.TryGet(key, out var entry) || entry.Prefab == null)
            {
                var hint = KeySuggestion.Hint(key, _library != null ? _library.Keys : null);
                Debug.LogWarning($"[Abubu.Effect] \"{key}\" が EffectLibrary に見つからないか、Prefab が未設定です。{hint}");
                return null;
            }

            var instance = Play(entry.Prefab, position, rotation, parent, entry.Lifetime);
            foreach (var hook in _hooks) hook.OnPlay(entry, instance, position);
            return instance;
        }

        public GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null, Transform parent = null, float lifetime = 0f)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            var instance = _pool.Rent(prefab, position, rotation ?? prefab.transform.rotation, parent);
            foreach (var ps in GetRootParticles(instance))
            {
                ps.Clear(true);
                ps.Play(true);
            }

            ScheduleReturn(prefab, instance, lifetime);
            return instance;
        }

        public void Stop(GameObject instance) => _pool.Return(instance);

        private void ScheduleReturn(GameObject prefab, GameObject instance, float lifetime)
        {
            var duration = GetDuration(prefab);
            if (lifetime > 0f)
            {
                _pool.ReturnAfter(instance, lifetime, duration.Unscaled);
                return;
            }

            if (duration.Looping) return;
            if (duration.HasParticles)
            {
                if (duration.Seconds > 0f) _pool.ReturnAfter(instance, duration.Seconds, duration.Unscaled);
                return;
            }

            // ParticleSystem が無いと長さが分からない。返却されずに残り続けるのを防ぐため既定の秒数で返す
            if (_settings.DefaultLifetime <= 0f) return;
            if (_warnedFallback.Add(prefab))
            {
                Debug.LogWarning($"[Abubu.Effect] \"{prefab.name}\" には ParticleSystem が無いため、{_settings.DefaultLifetime} 秒後に返却します。EffectEntry の Lifetime か Play の lifetime 引数で指定してください。");
            }
            _pool.ReturnAfter(instance, _settings.DefaultLifetime);
        }

        /// <summary>再生すべき ParticleSystem (他の ParticleSystem の子でないもの)。子は親の Play(true) で一緒に再生される</summary>
        private ParticleSystem[] GetRootParticles(GameObject instance)
        {
            if (_particles.TryGetValue(instance, out var cached)) return cached;

            // 破棄済みのインスタンスが溜まり続けないように、表が大きくなったら掃除する
            if (_particles.Count >= 256) PurgeDestroyed();

            instance.GetComponentsInChildren(true, _psBuffer);
            var roots = new List<ParticleSystem>();
            foreach (var ps in _psBuffer)
            {
                var parent = ps.transform.parent;
                if (parent == null || parent.GetComponentInParent<ParticleSystem>(true) == null) roots.Add(ps);
            }
            _psBuffer.Clear();

            var result = roots.ToArray();
            _particles[instance] = result;
            return result;
        }

        private void PurgeDestroyed()
        {
            _keyBuffer.Clear();
            foreach (var key in _particles.Keys)
            {
                if (key == null) _keyBuffer.Add(key);
            }
            foreach (var key in _keyBuffer) _particles.Remove(key);
            _keyBuffer.Clear();
        }

        /// <summary>パーティクルが消えきるまでの秒数 (simulationSpeed を考慮)。ループを含む場合は Looping</summary>
        private Duration GetDuration(GameObject prefab)
        {
            if (_durations.TryGetValue(prefab, out var cached)) return cached;

            var seconds = 0f;
            var looping = false;
            var unscaled = false;
            prefab.GetComponentsInChildren(true, _psBuffer);
            foreach (var ps in _psBuffer)
            {
                var main = ps.main;
                if (main.loop) looping = true;
                if (main.useUnscaledTime) unscaled = true;
                var speed = main.simulationSpeed > 0f ? main.simulationSpeed : 1f;
                seconds = Mathf.Max(seconds, (MaxOf(main.startDelay) + main.duration + MaxOf(main.startLifetime)) / speed);
            }
            var result = new Duration(seconds, looping, _psBuffer.Count > 0, unscaled);
            _psBuffer.Clear();

            _durations[prefab] = result;
            return result;
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
