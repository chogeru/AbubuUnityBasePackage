using System;
using UnityEngine;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// 割り当て変更画面の表示文字列。ローカライズしたい場合はこれを実装してコンテナにバインドするか、View の Labels で返す。
    /// <code>
    /// Container.Bind&lt;IRebindLabels&gt;().To&lt;MyLocalizedRebindLabels&gt;().AsSingle();
    /// </code>
    /// </summary>
    public interface IRebindLabels
    {
        /// <summary>アクションの表示名 (RebindEntry.DisplayName が空のとき)。<paramref name="actionName"/> は Input Actions 上の名前</summary>
        string ActionName(string actionPath, string actionName);

        /// <summary>割り当てが無いときの表示</summary>
        string Unbound { get; }

        /// <summary>入力待ち中の行に出す文字列</summary>
        string Listening { get; }

        /// <summary>入力待ち表示に出す文字列 (例: "Press a key for Jump")</summary>
        string ListeningPrompt(string actionName);

        /// <summary>操作がかぶっているときの説明 (例: "Also used by Attack")。<paramref name="otherActions"/> はかぶっているアクション名 (カンマ区切り)</summary>
        string Conflict(string otherActions);

        /// <summary>入れ替えたときの通知 (例: "Swapped with Attack")</summary>
        string Swapped(string otherActions);

        /// <summary>取り消したときの通知 (例: "Space is already used by Attack")</summary>
        string Rejected(string binding, string otherActions);
    }

    /// <summary>
    /// <see cref="IRebindLabels"/> の既定実装。英語の既定値を持ち、Inspector で書き換えられる。
    /// </summary>
    /// <remarks>*Format は string.Format の書式。</remarks>
    [Serializable]
    public sealed class RebindLabels : IRebindLabels
    {
        public string UnboundText = "-";
        public string ListeningText = "...";
        [Tooltip("{0} = アクション名")]
        public string ListeningPromptFormat = "Press a button for \"{0}\"";
        [Tooltip("{0} = かぶっているアクション名")]
        public string ConflictFormat = "Also used by {0}";
        [Tooltip("{0} = 入れ替えたアクション名")]
        public string SwappedFormat = "Swapped with {0}";
        [Tooltip("{0} = 割り当てようとした操作、{1} = かぶっているアクション名")]
        public string RejectedFormat = "{0} is already used by {1}";

        /// <inheritdoc />
        public string ActionName(string actionPath, string actionName) => actionName;

        /// <inheritdoc />
        public string Unbound => UnboundText;

        /// <inheritdoc />
        public string Listening => ListeningText;

        /// <inheritdoc />
        public string ListeningPrompt(string actionName) => SafeFormat(ListeningPromptFormat, actionName);

        /// <inheritdoc />
        public string Conflict(string otherActions) => SafeFormat(ConflictFormat, otherActions);

        /// <inheritdoc />
        public string Swapped(string otherActions) => SafeFormat(SwappedFormat, otherActions);

        /// <inheritdoc />
        public string Rejected(string binding, string otherActions) => SafeFormat(RejectedFormat, binding, otherActions);

        // Inspector で書式を壊しても例外で画面が止まらないようにする
        private static string SafeFormat(string format, params object[] args)
        {
            try
            {
                return string.Format(format ?? string.Empty, args);
            }
            catch (FormatException)
            {
                return string.Join(" ", args);
            }
        }
    }
}
