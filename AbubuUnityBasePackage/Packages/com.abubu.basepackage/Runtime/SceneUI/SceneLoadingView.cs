using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Presentation
{
    /// <summary>
    /// ロード画面の uGUI 実装。プレハブ化して AbubuSettings の Loading View Prefab に設定すると、
    /// シーン遷移中に自動で表示される。生成と Presenter の結線は <see cref="SceneLoadingScreen"/> が行う。
    /// </summary>
    /// <remarks>
    /// プレハブのルートに Canvas があれば、フェードより手前に出るよう sortingOrder は SceneLoadingScreen が設定する。
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class SceneLoadingView : MonoBehaviour, ISceneLoadingView
    {
        [SerializeField] private Slider progressBar;
        [Tooltip("Filled タイプの Image でも可")]
        [SerializeField] private Image progressFill;

        private CanvasGroup _group;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            SetVisible(false);
        }

        /// <inheritdoc />
        public void SetVisible(bool visible)
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
        }

        /// <inheritdoc />
        public void SetProgress(float value)
        {
            if (progressBar != null) progressBar.SetValueWithoutNotify(value);
            if (progressFill != null) progressFill.fillAmount = value;
        }
    }
}
