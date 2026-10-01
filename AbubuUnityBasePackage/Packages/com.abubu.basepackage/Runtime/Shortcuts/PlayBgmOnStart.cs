using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Components
{
    /// <summary>シーン開始時に BGM を再生する。シーンに置いてキーを入れるだけ。</summary>
    [AddComponentMenu("Abubu/Play BGM On Start")]
    public sealed class PlayBgmOnStart : MonoBehaviour
    {
        [SerializeField] private string bgmKey;
        [Tooltip("空のキーなら BGM を停止する")]
        [SerializeField] private bool stopIfEmpty = true;

        private void Start()
        {
            if (!string.IsNullOrEmpty(bgmKey)) Sound.PlayBgm(bgmKey);
            else if (stopIfEmpty) Sound.StopBgm();
        }
    }
}
