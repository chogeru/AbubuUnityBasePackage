using System.Collections.Generic;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// 同じキーの連続再生を一定時間ふさぐ (SoundEntry.Cooldown)。timeScale の影響は受けない。
    /// </summary>
    internal sealed class CooldownGate
    {
        private readonly Dictionary<string, float> _lastPlayedTime = new();

        /// <summary>再生してよければ true を返し、再生時刻を記録する。cooldown が 0 以下なら常に true</summary>
        public bool TryPass(string key, float cooldown)
        {
            if (cooldown <= 0f) return true;

            var now = Time.unscaledTime;
            if (_lastPlayedTime.TryGetValue(key, out var last) && now - last < cooldown) return false;
            _lastPlayedTime[key] = now;
            return true;
        }
    }
}
