using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Abubu.UI
{
    /// <summary>
    /// 長押しで確定するボタン (セーブデータの削除など、うっかり押されたくない操作用)。
    /// マウス・タッチは押している間、ゲームパッド・キーボードは選択中に決定ボタンを押している間 (Abubu.UI.Input が必要) に進む。
    /// ポーズ中も進む (unscaled time)。進み具合は Fill Image (Image Type = Filled) に表示される。
    /// </summary>
    [AddComponentMenu("Abubu/UI Hold Button")]
    [DisallowMultipleComponent]
    public sealed class UiHoldButton : Selectable, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("確定までに押し続ける秒数")]
        [Min(0.05f)] [SerializeField] private float holdSeconds = 1f;

        [Tooltip("進み具合を表示する Image (Image Type = Filled)。任意")]
        [SerializeField] private Image fill;

        [Tooltip("離したときに進み具合を戻す速さ (1 秒あたり)。0 なら即座に 0 に戻す")]
        [Min(0f)] [SerializeField] private float releaseSpeed = 3f;

        [SerializeField] private UnityEvent onCompleted = new();

        private IUiSubmitHoldSource _submitSource;
        private bool _pointerHeld;
        private bool _completed;
        private float _progress;

        /// <summary>長押しが確定したとき</summary>
        public UnityEvent OnCompleted => onCompleted;

        /// <summary>0〜1 の進み具合</summary>
        public float Progress => _progress;

        /// <summary>決定ボタンの状態を渡す (DI コンテナ経由で生成されれば自動。無ければ AbubuServices から探す)</summary>
        [Zenject.Inject]
        public void SetSubmitSource([Zenject.InjectOptional] IUiSubmitHoldSource source) => _submitSource = source;

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData.button == PointerEventData.InputButton.Left) _pointerHeld = true;
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            _pointerHeld = false;
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            base.OnPointerExit(eventData);
            _pointerHeld = false;
        }

        private void Update()
        {
            if (!IsInteractable()) return;

            var held = _pointerHeld || IsSubmitHeldWhileSelected();
            if (held && !_completed)
            {
                _progress = Mathf.Min(1f, _progress + Time.unscaledDeltaTime / holdSeconds);
                if (_progress >= 1f)
                {
                    _completed = true;
                    onCompleted.Invoke();
                }
            }
            else if (!held)
            {
                // 離したらやり直し (確定したあとも、離すまでは続けて確定しない)
                _completed = false;
                _progress = releaseSpeed <= 0f ? 0f : Mathf.Max(0f, _progress - Time.unscaledDeltaTime * releaseSpeed);
            }

            if (fill != null) fill.fillAmount = _progress;
        }

        private bool IsSubmitHeldWhileSelected()
        {
            if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != gameObject) return false;
            _submitSource ??= AbubuServices.TryResolve<IUiSubmitHoldSource>();
            return _submitSource != null && _submitSource.IsSubmitHeld;
        }
    }
}
