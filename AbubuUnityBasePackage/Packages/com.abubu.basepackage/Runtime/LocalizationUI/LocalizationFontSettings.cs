using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Abubu.Localization
{
    /// <summary>言語ごとのフォント。日中韓の文字が豆腐 (□) にならないよう、言語に合わせて切り替える</summary>
    [Serializable]
    public sealed class LocalizationFont
    {
        [Tooltip("言語コード (ja, zh-Hans など)。* にすると、個別の指定が無いすべての言語に使う")]
        public string Language = "*";

        [Tooltip("TextMeshPro 用のフォント")]
        public TMP_FontAsset TmpFont;

        [Tooltip("UI.Text (旧 Text) 用のフォント")]
        public Font Font;
    }

    /// <summary>
    /// 言語ごとのフォントの設定。Create > Abubu > Localization Fonts で作り、AbubuSettings > Module Settings に入れる。
    /// LocalizedText・SubtitleText・組み込みのダイアログが言語の切り替えに合わせてフォントを差し替える。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/Localization Fonts", fileName = "LocalizationFonts")]
    public sealed class LocalizationFontSettings : ScriptableObject
    {
        public List<LocalizationFont> Fonts = new();
    }
}
