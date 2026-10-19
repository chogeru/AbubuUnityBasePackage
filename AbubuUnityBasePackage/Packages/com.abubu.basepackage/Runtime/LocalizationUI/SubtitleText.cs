using System;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Abubu.Localization
{
    /// <summary>
    /// Text / TextMeshPro に付けると、<see cref="ISubtitleService"/> の字幕を表示する。字幕が無いときは Hide Root を非表示にする。
    /// 言語ごとのフォントにも切り替わる。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Abubu/Subtitle Text")]
    public sealed class SubtitleText : MonoBehaviour
    {
        [Tooltip("字幕が無いときに非表示にするもの (背景のパネルなど)。未設定なら文字だけを消す")]
        [SerializeField] private GameObject hideRoot;

        private ISubtitleService _subtitles;
        private ILocalizedFontService _fonts;
        private IDisposable _subscription;

        [Inject]
        public void SetServices([InjectOptional] ISubtitleService subtitles, [InjectOptional] ILocalizedFontService fonts)
        {
            _subtitles = subtitles;
            _fonts = fonts;
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Subscribe()
        {
            _subscription?.Dispose();
            _subscription = null;
            var subtitles = _subtitles ?? AbubuServices.TryResolve<ISubtitleService>();
            if (subtitles == null) return;
            _subscription = subtitles.Current.Subscribe(this, static (text, self) => self.Apply(text));
        }

        private void Apply(string text)
        {
            _fonts ??= AbubuServices.TryResolve<ILocalizedFontService>();
            var font = _fonts?.GetFont();
            if (TryGetComponent<TMP_Text>(out var tmp))
            {
                tmp.text = text ?? string.Empty;
                if (font != null && font.TmpFont != null) tmp.font = font.TmpFont;
            }
            else if (TryGetComponent<Text>(out var legacy))
            {
                legacy.text = text ?? string.Empty;
                if (font != null && font.Font != null) legacy.font = font.Font;
            }

            if (hideRoot != null && hideRoot != gameObject) hideRoot.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}
