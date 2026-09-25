using Abubu.Audio;
using Abubu.Boot;
using Abubu.Effects;
using Abubu.Save;
using Abubu.Scene;
using UnityEngine;

namespace Abubu
{
    /// <summary>
    /// パッケージ全体の設定。Tools > Abubu > Setup Project で自動生成される。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/Settings", fileName = "AbubuSettings")]
    public sealed class AbubuSettings : ScriptableObject
    {
        public BootSettings Boot = new();
        public SoundSettings Sound = new();
        public SceneSettings Scene = new();
        public EffectSettings Effect = new();
        public SaveSettings Save = new();
    }
}
