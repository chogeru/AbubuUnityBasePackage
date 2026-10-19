using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Abubu.Localization
{
    /// <summary>翻訳が見つからないキーを Get したときに返すもの</summary>
    public enum MissingKeyBehavior
    {
        /// <summary>キーをそのまま返す ("menu.start")</summary>
        ReturnKey,
        /// <summary>キーを角かっこで囲んで返す ("[menu.start]")。翻訳漏れが画面で目立つ</summary>
        Bracketed,
        /// <summary>空文字を返す (製品版で何も表示したくない場合)</summary>
        Empty,
    }

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

    [Serializable]
    public sealed class LocalizationSettings
    {
        [Tooltip("翻訳テーブル。ILocalizationSource を自前でバインドした場合は使われない")]
        public LocalizationTable Table;

        [Tooltip("初回起動時の言語。空なら端末の言語 (テーブルに無ければ Fallback Language)")]
        public string StartupLanguage = "";

        [Tooltip("選択中の言語に翻訳が無いキーで使う言語。空ならテーブルの先頭の言語")]
        public string FallbackLanguage = "";

        [Tooltip("選んだ言語を ISaveService に保存し、次回起動時に復元する")]
        public bool RememberLanguage = true;

        [Tooltip("翻訳が見つからないキーを Get したときに返すもの")]
        public MissingKeyBehavior MissingKey = MissingKeyBehavior.ReturnKey;

        [Tooltip("翻訳が見つからないキーを警告する (キーごとに 1 回)")]
        public bool WarnMissingKeys = true;

        [Tooltip("疑似ローカライズ (\"Start\" → \"[!!Ŝţåŕţ~~!!]\")。翻訳漏れや、長い文言でのレイアウト崩れを確認するときに使う")]
        public bool PseudoLocalization;

        [Tooltip("言語ごとのフォント。LocalizedText が言語の切り替えに合わせて差し替える")]
        public List<LocalizationFont> Fonts = new();
    }
}
