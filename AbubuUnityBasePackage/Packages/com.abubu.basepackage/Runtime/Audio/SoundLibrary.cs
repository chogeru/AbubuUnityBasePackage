using System;
using System.Collections.Generic;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// キー → サウンド定義 の辞書アセット。
    /// Create > Abubu > Sound Library で作成し、AbubuSettings に登録して使う。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/Sound Library", fileName = "SoundLibrary")]
    public sealed class SoundLibrary : ScriptableObject
    {
        [SerializeField] private List<SoundEntry> bgm = new();
        [SerializeField] private List<SoundEntry> se = new();
        [SerializeField] private List<SoundEntry> ambient = new();
        [SerializeField] private List<SoundEntry> voice = new();

        [Tooltip("他のライブラリを取り込む (共通SE集 + ステージ別BGM など分割管理用)")]
        [SerializeField] private List<SoundLibrary> includes = new();

        private Dictionary<string, SoundEntry>[] _cache;
#if UNITY_EDITOR
        /// <summary>
        /// いずれかの SoundLibrary が編集されるたびに増える番号。
        /// includes 先の編集も検知できるよう、全ライブラリ共通の番号とキャッシュ作成時の番号を比べる
        /// </summary>
        private static int s_editorVersion;
        private int _cacheVersion = -1;
#endif

        public bool TryGet(SoundCategory category, string key, out SoundEntry entry)
        {
            if (key == null)
            {
                entry = null;
                return false;
            }
            return GetCache()[(int)category].TryGetValue(key, out entry);
        }

        /// <summary>指定カテゴリに登録されているキー (includes 先を含む)。タイポ時の候補表示やキー定数の生成に使う</summary>
        public IEnumerable<string> GetKeys(SoundCategory category) => GetCache()[(int)category].Keys;

        /// <summary>
        /// 検索用キャッシュを捨てる。実行時にスクリプトからエントリや includes を書き換えた場合に呼ぶ
        /// (インスペクタでの編集は自動で反映される)
        /// </summary>
        public void ClearCache()
        {
            _cache = null;
#if UNITY_EDITOR
            s_editorVersion++;
#endif
        }

        private Dictionary<string, SoundEntry>[] GetCache()
        {
#if UNITY_EDITOR
            if (_cacheVersion != s_editorVersion)
            {
                _cache = null;
                _cacheVersion = s_editorVersion;
            }
#endif
            return _cache ??= BuildCache();
        }

        public IReadOnlyList<SoundEntry> GetEntries(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => bgm,
            SoundCategory.Se => se,
            SoundCategory.Ambient => ambient,
            SoundCategory.Voice => voice,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        private Dictionary<string, SoundEntry>[] BuildCache()
        {
            var cache = new[]
            {
                new Dictionary<string, SoundEntry>(),
                new Dictionary<string, SoundEntry>(),
                new Dictionary<string, SoundEntry>(),
                new Dictionary<string, SoundEntry>(),
            };
            Collect(this, cache, new HashSet<SoundLibrary>());
            return cache;
        }

        private static void Collect(SoundLibrary library, Dictionary<string, SoundEntry>[] cache, HashSet<SoundLibrary> visited)
        {
            if (library == null || !visited.Add(library)) return;

            for (var i = 0; i < cache.Length; i++)
            {
                foreach (var entry in library.GetEntries((SoundCategory)i))
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;
                    // 先に登録されたもの (= 自分自身) を優先する
                    cache[i].TryAdd(entry.Key, entry);
                }
            }

            foreach (var include in library.includes) Collect(include, cache, visited);
        }

        // Domain Reload 無効時や includes 先の編集に備えて、有効化時・編集時にキャッシュを捨てる
        // (エディタでは全ライブラリ共通の番号を進めるので、このライブラリを include している側も作り直される)
        private void OnEnable() => ClearCache();
        private void OnValidate() => ClearCache();
    }
}
