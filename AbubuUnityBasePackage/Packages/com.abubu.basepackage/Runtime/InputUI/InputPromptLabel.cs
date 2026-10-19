using System;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Zenject;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// "Press {Player/Jump} to jump" の {Map/Action} を、使用中のデバイス・割り当てに合わせた表示に置き換えて出す
    /// (<see cref="InputGlyphText"/>)。デバイス・割り当てが変わると自動で作り直す。
    /// <code>
    /// // Text を持つオブジェクトに付けて Template を書くだけ。TextMeshPro なら On Text Changed に TMP_Text.text を登録し、
    /// // Options の Use Sprite Tags をオンにすると &lt;sprite name=...&gt; でアイコンを出せる
    /// promptLabel.Template = "{UI/Submit} Start";   // コードから変えてもよい
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputPromptLabel : MonoBehaviour
    {
        [Tooltip("{Map/Action} を含む文字列 ({Map/Action:1} で割り当てのインデックスを指定。{{ / }} で { / })")]
        [TextArea]
        [SerializeField] private string template = "Press {UI/Submit}";
        [Tooltip("結果を出す Text (未設定なら同じオブジェクトの Text)")]
        [SerializeField] private Text text;
        [SerializeField] private InputGlyphTextOptions options = new();

        [Header("TextMeshPro などを使う場合")]
        [SerializeField] private UnityEvent<string> onTextChanged = new();

        private IInputGlyphService _glyphs;
        private IDisposable _subscription;

        /// <summary>{Map/Action} を含む文字列。変えるとすぐ作り直す</summary>
        public string Template
        {
            get => template;
            set
            {
                template = value;
                Refresh();
            }
        }

        /// <summary>置き換えの設定 (変えたら <see cref="Refresh"/> を呼ぶ)</summary>
        public InputGlyphTextOptions Options => options;

        [Inject]
        private void InjectGlyphService([InjectOptional] IInputGlyphService glyphs)
        {
            if (glyphs == null || ReferenceEquals(glyphs, _glyphs)) return;
            _glyphs = glyphs;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake()
        {
            if (text == null) text = GetComponent<Text>();
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
            var gamepad = GameInput.Devices?.CurrentGamepadKind.CurrentValue ?? GamepadKind.Generic;
            var result = InputGlyphText.Format(template, _glyphs, gamepad, options);
            if (text != null) text.text = result;
            onTextChanged.Invoke(result);
        }
    }
}
