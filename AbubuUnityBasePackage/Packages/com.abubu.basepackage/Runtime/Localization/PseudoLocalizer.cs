using System;
using System.Text;

namespace Abubu.Localization
{
    /// <summary>
    /// 疑似ローカライズ。文字を飾り付きの文字に置き換え、長さを伸ばし、前後を [!! !!] で囲む ("Start" → "[!!Ŝţåŕţ~~!!]")。
    /// 翻訳前に「ハードコードされた文言」「長い言語ではみ出すレイアウト」「対応していないフォント」を見つけるために使う。
    /// </summary>
    /// <remarks>リッチテキストのタグ (&lt;b&gt; など) と、書式化されずに残ったプレースホルダー ({name}) は変えない。</remarks>
    public static class PseudoLocalizer
    {
        private const string Plain = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Accented = "åƀçðéƒĝĥîĵķļɱñöþǫŕŝţûṽŵẋýžÅƁÇÐÉƑĜĤÎĴĶĻṀÑÖÞǪŔŜŢÛṼŴẊÝŽ";

        /// <param name="expansion">伸ばす割合 (0.3 なら 30% 長くする。ドイツ語などは英語より 3 割ほど長くなる)</param>
        public static string Apply(string text, float expansion = 0.3f)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new StringBuilder(text.Length * 2 + 8);
            sb.Append("[!!");
            var visible = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var close = c == '<' ? '>' : c == '{' ? '}' : '\0';
                if (close != '\0')
                {
                    var end = text.IndexOf(close, i + 1);
                    if (end > i)
                    {
                        sb.Append(text, i, end - i + 1);
                        i = end;
                        continue;
                    }
                }

                var index = Plain.IndexOf(c);
                sb.Append(index >= 0 ? Accented[index] : c);
                if (!char.IsWhiteSpace(c)) visible++;
            }

            var padding = (int)Math.Ceiling(visible * Math.Max(0f, expansion));
            sb.Append('~', padding);
            sb.Append("!!]");
            return sb.ToString();
        }
    }
}
