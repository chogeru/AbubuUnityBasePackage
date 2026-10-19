using System;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Zenject;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// アクションのボタンアイコンを、使用中のデバイス (キーボード / Xbox / PlayStation / Switch ...) と割り当てに合わせて自動で切り替える。
    /// アイコンが無ければ表示文字列 ("Space" / "A" など) を出す。
    /// <code>
    /// // Image を持つオブジェクトに付けて Action Path に "Player/Jump" を設定するだけ
    /// glyphImage.ActionPath = "UI/Submit";   // コードから変えてもよい
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>アイコン表は AbubuInputSettings.Glyphs (<see cref="InputGlyphs"/>)。未設定なら文字列だけになる。</para>
    /// <para>TextMeshPro を使う場合は On Text Changed に TMP_Text.text を、独自の表示には On Sprite Changed を登録する。</para>
    /// <para>IInputGlyphService は Zenject の Inject、無ければ ProjectContext から取得する。どちらも無ければ何もしない。</para>
    /// <para>割り当てが無いときは Unbound Text / Unbound Sprite を出す (<see cref="InputGlyph.IsUnbound"/>)。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class InputGlyphImage : MonoBehaviour
    {
        [Tooltip("\"Map/Action\" 形式のアクションのパス")]
        [SerializeField] private string actionPath;
        [Tooltip("割り当てのインデックス。-1 なら使用中のデバイス向けの割り当てを自動で選ぶ")]
        [SerializeField] private int bindingIndex = -1;

        [Tooltip("アイコンを表示する Image (未設定なら同じオブジェクトの Image)")]
        [SerializeField] private Image image;
        [Tooltip("アイコンが無いときに表示文字列を出す Text (任意)")]
        [SerializeField] private Text fallbackText;
        [Tooltip("アイコンがあるときも Fallback Text に文字列を出す")]
        [SerializeField] private bool alwaysShowText;
        [Tooltip("アイコンを設定するたびに SetNativeSize する")]
        [SerializeField] private bool setNativeSize;

        [Header("未割り当てのとき")]
        [Tooltip("アクションが無い・割り当てが空のときに出す文字列 (空なら何も出さない)")]
        [SerializeField] private string unboundText = "-";
        [Tooltip("アクションが無い・割り当てが空のときに出すアイコン (任意)")]
        [SerializeField] private Sprite unboundSprite;

        [Header("TextMeshPro などを使う場合")]
        [SerializeField] private UnityEvent<Sprite> onSpriteChanged = new();
        [SerializeField] private UnityEvent<string> onTextChanged = new();

        private IInputGlyphService _glyphs;
        private IDisposable _subscription;

        /// <summary>表示するアクションのパス。変えるとすぐ表示を作り直す</summary>
        public string ActionPath
        {
            get => actionPath;
            set
            {
                actionPath = value;
                Refresh();
            }
        }

        /// <summary>割り当てのインデックス (-1 で自動)。変えるとすぐ表示を作り直す</summary>
        public int BindingIndex
        {
            get => bindingIndex;
            set
            {
                bindingIndex = value;
                Refresh();
            }
        }

        /// <summary>現在の表示内容</summary>
        public InputGlyph Current { get; private set; }

        /// <summary>アイコンを表示する Image (コードで組み立てる場合に設定する)</summary>
        public Image Image
        {
            get => image;
            set
            {
                image = value;
                Refresh();
            }
        }

        /// <summary>アイコンが無いときに表示文字列を出す Text (コードで組み立てる場合に設定する)</summary>
        public Text FallbackText
        {
            get => fallbackText;
            set
            {
                fallbackText = value;
                Refresh();
            }
        }

        /// <summary>表示が作り直されたとき (アイコン・文字列が変わりうるとき) に呼ばれる</summary>
        public event Action<InputGlyphImage> Refreshed;

        /// <summary>未割り当てのときに出す文字列。変えるとすぐ表示を作り直す</summary>
        public string UnboundText
        {
            get => unboundText;
            set
            {
                unboundText = value;
                Refresh();
            }
        }

        /// <summary>未割り当てのときに出すアイコン。変えるとすぐ表示を作り直す</summary>
        public Sprite UnboundSprite
        {
            get => unboundSprite;
            set
            {
                unboundSprite = value;
                Refresh();
            }
        }

        [Inject]
        private void InjectGlyphService([InjectOptional] IInputGlyphService glyphs)
        {
            if (glyphs == null || ReferenceEquals(glyphs, _glyphs)) return;
            _glyphs = glyphs;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake()
        {
            if (image == null) image = GetComponent<Image>();
        }

        private void OnEnable()
        {
            if (_glyphs == null && AbubuServices.IsAvailable) _glyphs = AbubuServices.TryResolve<IInputGlyphService>();
            Subscribe();
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Subscribe()
        {
            _subscription?.Dispose();
            _subscription = _glyphs?.Changed.Subscribe(this, static (_, self) => self.Refresh());
            Refresh();
        }

        /// <summary>表示を作り直す</summary>
        public void Refresh()
        {
            if (_glyphs == null) return;
            var glyph = _glyphs.GetGlyph(actionPath, bindingIndex);
            Current = glyph;

            var unbound = glyph.IsUnbound;
            var sprite = unbound ? unboundSprite : glyph.Sprite;
            if (image != null)
            {
                image.sprite = sprite;
                image.enabled = sprite != null;
                if (sprite != null && setNativeSize) image.SetNativeSize();
            }

            var display = unbound ? unboundText ?? string.Empty : glyph.DisplayString;
            var text = sprite != null && !alwaysShowText ? string.Empty : display;
            if (fallbackText != null)
            {
                fallbackText.text = text;
                fallbackText.enabled = text.Length > 0;
            }

            onSpriteChanged.Invoke(sprite);
            onTextChanged.Invoke(text);
            Refreshed?.Invoke(this);
        }
    }
}
