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

    /// <summary>Button に付けると押下時に SE を鳴らす。</summary>
    [AddComponentMenu("Abubu/Play SE On Click")]
    [RequireComponent(typeof(Button))]
    public sealed class PlaySeOnClick : MonoBehaviour
    {
        [SerializeField] private string seKey = "click";

        private void Awake() => GetComponent<Button>().onClick.AddListener(() => Sound.PlaySe(seKey));
    }

    /// <summary>Button に付けると押下時にシーン遷移する。</summary>
    [AddComponentMenu("Abubu/Load Scene On Click")]
    [RequireComponent(typeof(Button))]
    public sealed class LoadSceneOnClick : MonoBehaviour
    {
        [SerializeField] private string sceneName;
        [Tooltip("オンなら sceneName を無視して現在のシーンを再読み込み")]
        [SerializeField] private bool reload;

        private void Awake() => GetComponent<Button>().onClick.AddListener(() =>
        {
            if (reload) Scenes.Reload();
            else Scenes.Load(sceneName);
        });
    }
}
