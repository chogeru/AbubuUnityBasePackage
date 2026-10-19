using System;
using UnityEngine;

namespace Abubu.Effects
{
    [Serializable]
    public sealed class EffectSettings
    {
        public EffectLibrary Library;

        [Tooltip("ParticleSystem を含まないエフェクト (VFX Graph / Animator など) で Lifetime が 0 のときに使う自動返却の秒数。0 なら自動返却しない")]
        [Min(0f)] public float DefaultLifetime = 5f;
    }
}
