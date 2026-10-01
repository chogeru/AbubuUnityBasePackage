using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Components
{
    /// <summary>シーン開始時に環境音レイヤーを再生し、それ以外の環境音はフェードアウトする。</summary>
    [AddComponentMenu("Abubu/Play Ambient On Start")]
    public sealed class PlayAmbientOnStart : MonoBehaviour
    {
        [SerializeField] private string[] ambientKeys = System.Array.Empty<string>();
        [SerializeField] private bool stopOthers = true;

        private void Start()
        {
            if (stopOthers) Sound.StopAllAmbient();
            foreach (var key in ambientKeys) Sound.PlayAmbient(key);
        }
    }
}
