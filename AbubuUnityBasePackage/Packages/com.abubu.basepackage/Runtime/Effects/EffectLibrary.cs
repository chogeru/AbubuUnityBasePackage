using System;
using System.Collections.Generic;
using UnityEngine;

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

        /// <summary>登録されているキー。タイポ時の候補表示やキー定数の生成に使う</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                _cache ??= BuildCache();
                return _cache.Keys;
            }
        }

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
}
