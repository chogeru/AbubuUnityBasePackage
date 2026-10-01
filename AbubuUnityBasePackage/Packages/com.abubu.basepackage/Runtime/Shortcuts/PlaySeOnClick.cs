using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Components
{
    /// <summary>Button に付けると押下時に SE を鳴らす。</summary>
    [AddComponentMenu("Abubu/Play SE On Click")]
    [RequireComponent(typeof(Button))]
    public sealed class PlaySeOnClick : MonoBehaviour
    {
        [SerializeField] private string seKey = "click";

        private void Awake() => GetComponent<Button>().onClick.AddListener(() => Sound.PlaySe(seKey));
    }
}
