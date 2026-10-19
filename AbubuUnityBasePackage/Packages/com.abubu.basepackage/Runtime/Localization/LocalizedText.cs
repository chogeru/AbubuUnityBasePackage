using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Abubu.Localization
{
    /// <summary>
    /// Text / TextMeshPro に付けると、Key の翻訳を表示し、言語が変わったら自動で書き換える。
    /// 言語ごとのフォント (LocalizationSettings.Fonts) も切り替える (Keep Font で無効化)。
    /// それ以外の表示先 (UI Toolkit・独自コンポーネントなど) は On Changed に登録すれば翻訳済みの文字列が渡される。
    /// <code>
    /// GetComponent&lt;LocalizedText&gt;().SetArguments(score);   // "スコア: {0}" の {0} に入る
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Abubu/Localized Text")]
    public sealed class LocalizedText : LocalizedBehaviour
    {
        [SerializeField] private string key;

        [Tooltip("言語ごとのフォントに切り替えず、今のフォントを使い続ける")]
        [SerializeField] private bool keepFont;

        [Tooltip("翻訳した文字列を受け取る (Text / TextMeshPro 以外に表示したいとき)")]
        [SerializeField] private UnityEvent<string> onChanged = new();

        private object[] _args;
        private Text _text;
        private TMP_Text _tmp;
        private bool _initialized;
        private Font _originalFont;
        private TMP_FontAsset _originalTmpFont;

        public string Key
        {
            get => key;
            set
            {
                key = value;
                Refresh();
            }
        }

        public bool KeepFont
        {
            get => keepFont;
            set
            {
                keepFont = value;
                Refresh();
            }
        }

        /// <summary>書式の {0} {1} ... に入れる値を設定して表示を更新する</summary>
        public void SetArguments(params object[] args)
        {
            _args = args;
            Refresh();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _text = GetComponent<Text>();
            _tmp = GetComponent<TMP_Text>();
            _originalFont = _text != null ? _text.font : null;
            _originalTmpFont = _tmp != null ? _tmp.font : null;
            if (_text == null && _tmp == null && onChanged.GetPersistentEventCount() == 0)
            {
                Debug.LogWarning($"[Abubu.Localization] {name} に Text / TextMeshPro が無く、On Changed も未設定です。", this);
            }
        }

        public override void Refresh()
        {
            Initialize();
            var service = Service;
            if (service == null) return;

            if (!keepFont) ApplyFont(service.GetFont());
            if (string.IsNullOrEmpty(key)) return;

            var value = _args is { Length: > 0 } ? service.Get(key, _args) : service.Get(key);
            if (_tmp != null) _tmp.text = value;
            else if (_text != null) _text.text = value;
            onChanged.Invoke(value);
        }

        /// <summary>言語のフォントに切り替える。指定が無ければ元のフォントに戻す</summary>
        private void ApplyFont(LocalizationFont font)
        {
            if (_tmp != null)
            {
                var target = font != null && font.TmpFont != null ? font.TmpFont : _originalTmpFont;
                if (target != null && _tmp.font != target) _tmp.font = target;
            }

            if (_text != null)
            {
                var target = font != null && font.Font != null ? font.Font : _originalFont;
                if (target != null && _text.font != target) _text.font = target;
            }
        }
    }
}
