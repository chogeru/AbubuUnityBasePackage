using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Abubu.UI
{
    /// <summary>
    /// ScrollRect に付けると、ゲームパッド・キーボードで選択した項目が見えるように自動でスクロールする。
    /// 長いリスト (設定画面・アイテム一覧) をスティックで操作するときに使う。
    /// </summary>
    [AddComponentMenu("Abubu/UI Scroll To Selection")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScrollRect))]
    public sealed class UiScrollToSelection : MonoBehaviour
    {
        [Tooltip("選択した項目の周りに空ける余白 (px)")]
        [SerializeField] private float padding = 16f;

        [Tooltip("スクロールにかける時間 (秒)。0 なら瞬時に移動する。ポーズ中も動く")]
        [Min(0f)] [SerializeField] private float smoothTime = 0.1f;

        private ScrollRect _scroll;
        private GameObject _lastSelected;
        private Vector2 _target;
        private Vector2 _velocity;
        private bool _moving;

        private void Awake() => _scroll = GetComponent<ScrollRect>();

        private void LateUpdate()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != _lastSelected)
            {
                _lastSelected = selected;
                if (selected != null && _scroll.content != null && selected.transform.IsChildOf(_scroll.content)) BeginScroll((RectTransform)selected.transform);
            }

            if (!_moving || _scroll.content == null) return;
            var content = _scroll.content;
            content.anchoredPosition = smoothTime <= 0f
                ? _target
                : Vector2.SmoothDamp(content.anchoredPosition, _target, ref _velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            if ((content.anchoredPosition - _target).sqrMagnitude < 0.25f)
            {
                content.anchoredPosition = _target;
                _moving = false;
            }
        }

        private void BeginScroll(RectTransform item)
        {
            var viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
            var view = viewport.rect;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);

            var offset = Vector2.zero;
            if (_scroll.vertical)
            {
                if (bounds.max.y + padding > view.yMax) offset.y = bounds.max.y + padding - view.yMax;
                else if (bounds.min.y - padding < view.yMin) offset.y = bounds.min.y - padding - view.yMin;
            }
            if (_scroll.horizontal)
            {
                if (bounds.max.x + padding > view.xMax) offset.x = bounds.max.x + padding - view.xMax;
                else if (bounds.min.x - padding < view.xMin) offset.x = bounds.min.x - padding - view.xMin;
            }
            if (offset == Vector2.zero) return;

            // 項目を見える位置へ動かすには、中身を逆方向へ動かす
            _scroll.StopMovement();
            _target = _scroll.content.anchoredPosition - offset;
            _velocity = Vector2.zero;
            _moving = true;
        }
    }
}
