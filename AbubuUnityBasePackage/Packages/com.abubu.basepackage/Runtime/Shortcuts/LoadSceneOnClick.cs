using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Components
{
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
