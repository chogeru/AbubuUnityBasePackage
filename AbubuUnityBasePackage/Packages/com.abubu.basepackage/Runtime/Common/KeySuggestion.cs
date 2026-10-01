using System;
using System.Collections.Generic;
using System.Linq;

namespace Abubu
{
    /// <summary>
    /// 文字列キーのタイポに気づけるよう、近いキーを探して警告文に添える。
    /// SoundLibrary / EffectLibrary のキー検索失敗時に使う。
    /// </summary>
    public static class KeySuggestion
    {
        /// <summary>
        /// <paramref name="key"/> に近い候補を、近い順に最大 <paramref name="max"/> 件返す。
        /// 編集距離が小さい、または一方がもう一方を含むものを候補にする (大文字小文字は無視)。
        /// </summary>
        public static IReadOnlyList<string> Closest(string key, IEnumerable<string> candidates, int max = 3)
        {
            if (string.IsNullOrEmpty(key) || candidates == null) return Array.Empty<string>();

            var threshold = Math.Max(2, key.Length / 3);
            return candidates
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .Select(c => (name: c, score: Score(key, c, threshold)))
                .Where(x => x.score >= 0)
                .OrderBy(x => x.score)
                .ThenBy(x => x.name, StringComparer.Ordinal)
                .Take(max)
                .Select(x => x.name)
                .ToList();
        }

        /// <summary>警告文の末尾に付けるヒント。候補が無ければ空文字</summary>
        public static string Hint(string key, IEnumerable<string> candidates)
        {
            var closest = Closest(key, candidates);
            return closest.Count == 0 ? string.Empty : $" もしかして: {string.Join(", ", closest.Select(c => $"\"{c}\""))}";
        }

        /// <summary>候補にならなければ -1、なるなら小さいほど近い値</summary>
        private static int Score(string key, string candidate, int threshold)
        {
            if (candidate.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Math.Abs(candidate.Length - key.Length);
            }

            var distance = Levenshtein(key.ToLowerInvariant(), candidate.ToLowerInvariant());
            return distance <= threshold ? distance : -1;
        }

        private static int Levenshtein(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) prev[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }
}
