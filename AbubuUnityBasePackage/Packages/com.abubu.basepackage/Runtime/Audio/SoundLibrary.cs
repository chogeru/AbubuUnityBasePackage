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

        [Tooltip("他のライブラリを取り込む (共通SE集 + ステージ別BGM など分割管理用)")]
        [SerializeField] private List<SoundLibrary> includes = new();

        private Dictionary<string, SoundEntry>[] _cache;

        public bool TryGet(SoundCategory category, string key, out SoundEntry entry)
        {
#if UNITY_EDITOR
            RebuildInEditor();
#endif
            _cache ??= BuildCache();
            return _cache[(int)category].TryGetValue(key, out entry);
        }

        public IReadOnlyList<SoundEntry> GetEntries(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => bgm,
            SoundCategory.Se => se,
            SoundCategory.Ambient => ambient,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        private Dictionary<string, SoundEntry>[] BuildCache()
        {
            var cache = new[]
            {
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

        // Domain Reload 無効時や includes 先の編集に備えて、有効化時にもキャッシュを捨てる
        private void OnEnable() => _cache = null;
        private void OnValidate() => _cache = null;

#if UNITY_EDITOR
        // エディタでは includes 先の変更を検知できないため毎回作り直す (件数が少ないので十分軽い)
        private void RebuildInEditor() => _cache = null;
#endif
    }
}
