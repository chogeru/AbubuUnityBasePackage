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
        [Tooltip("プールに残しておく数の上限。超えた分は返却時に破棄する。0 なら上限なし")]
        [Min(0)] public int MaxPooled;
    }

    /// <summary>
    /// キー → エフェクト定義の表。includes に他のライブラリを並べると、その中身も同じキー空間で検索できる
    /// (サンプルや機能ごとにライブラリを分けたいとき用。自分のエントリが優先され、同じキーは先に見つかったものが使われる)。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/Effect Library", fileName = "EffectLibrary")]
    public sealed class EffectLibrary : ScriptableObject
    {
        [SerializeField] private List<EffectEntry> effects = new();

        [Tooltip("一緒に検索する他のライブラリ (自分のエントリが優先)")]
        [SerializeField] private List<EffectLibrary> includes = new();

        /// <summary>
        /// どれかのライブラリが変更されるたびに進む番号。
        /// includes 先の編集も検知できるよう、全ライブラリ共通の番号とキャッシュ作成時の番号を比べる
        /// </summary>
        private static int s_version;

        private Dictionary<string, EffectEntry> _cache;
        private int _cacheVersion = -1;

        /// <summary>このライブラリ自身のエントリ (includes 先は含まない)</summary>
        public IReadOnlyList<EffectEntry> Effects => effects;

        /// <summary>一緒に検索する他のライブラリ</summary>
        public IReadOnlyList<EffectLibrary> Includes => includes;

        /// <summary>登録されているキー (includes 先を含む)。タイポ時の候補表示やキー定数の生成に使う</summary>
        public IEnumerable<string> Keys => GetCache().Keys;

        /// <summary>includes 先を含むすべてのエントリ (Prewarm などの一括処理用)</summary>
        public IEnumerable<EffectEntry> AllEffects => GetCache().Values;

        public bool TryGet(string key, out EffectEntry entry)
        {
            if (key == null)
            {
                entry = null;
                return false;
            }
            return GetCache().TryGetValue(key, out entry);
        }

        /// <summary>検索用キャッシュを捨てる。実行時にスクリプトからエントリや includes を書き換えた場合に呼ぶ</summary>
        public void ClearCache() => s_version++;

        private Dictionary<string, EffectEntry> GetCache()
        {
            if (_cache == null || _cacheVersion != s_version)
            {
                _cache = new Dictionary<string, EffectEntry>();
                Collect(this, _cache, new HashSet<EffectLibrary>());
                _cacheVersion = s_version;
            }
            return _cache;
        }

        /// <summary>自分 → includes の順に集める。循環参照は visited で止める</summary>
        private static void Collect(EffectLibrary library, Dictionary<string, EffectEntry> cache, HashSet<EffectLibrary> visited)
        {
            if (library == null || !visited.Add(library)) return;
            foreach (var e in library.effects)
            {
                if (e != null && !string.IsNullOrEmpty(e.Key)) cache.TryAdd(e.Key, e);
            }
            foreach (var include in library.includes) Collect(include, cache, visited);
        }

        // Domain Reload 無効時や includes 先の編集に備えて、有効化時・編集時にキャッシュを捨てる
        private void OnEnable() => s_version++;

        private void OnValidate() => s_version++;
    }
}
