using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace Abubu.Inputs.UI
{
    /// <summary>ボタン操作の案内 1 つ分 ("Ⓐ 決定" "Ⓑ 戻る" など)。アイコンは ActionPath の割り当てから自動で決まる</summary>
    [Serializable]
    public struct ButtonPrompt
    {
        [Tooltip("\"Map/Action\" 形式のアクションのパス (UI/Submit など)")]
        public string ActionPath;

        [Tooltip("アイコンの横に出す文言")]
        public string Label;

        public ButtonPrompt(string actionPath, string label)
        {
            ActionPath = actionPath;
            Label = label;
        }
    }

    /// <summary>
    /// <see cref="UiButtonPromptBar"/> に表示する案内の供給元。画面・ポップアップごとに案内を切り替える場合に実装してバインドする。
    /// <code>
    /// var source = new ButtonPromptSource();
    /// bar.SetSource(source);
    /// source.Set(new ButtonPrompt("UI/Submit", "OK"), new ButtonPrompt("UI/Cancel", "Back"));
    /// source.Clear();   // バーの既定 (Inspector の Default Prompts) に戻す
    /// </code>
    /// </summary>
    /// <remarks>
    /// Abubu.Inputs.UI は UI モジュール (Abubu.UI) に依存しないよう、このインターフェースだけを見る。
    /// UI モジュールの「一番手前の画面・ポップアップの案内」(IUiPromptService など) を出したい場合は、
    /// 連携側 (Abubu.UI.Input) がそれをこのインターフェースに変換してバインドする。
    /// </remarks>
    public interface IButtonPromptSource
    {
        /// <summary>表示する案内。null ならバーの既定の案内を出す</summary>
        ReadOnlyReactiveProperty<IReadOnlyList<ButtonPrompt>> Prompts { get; }
    }

    /// <summary>コードから案内を差し替えられる、<see cref="IButtonPromptSource"/> の簡単な実装</summary>
    public sealed class ButtonPromptSource : IButtonPromptSource, IDisposable
    {
        private readonly ReactiveProperty<IReadOnlyList<ButtonPrompt>> _prompts = new(null);

        public ReadOnlyReactiveProperty<IReadOnlyList<ButtonPrompt>> Prompts => _prompts;

        /// <summary>表示する案内を設定する</summary>
        public void Set(params ButtonPrompt[] prompts) => _prompts.Value = prompts;

        /// <summary>表示する案内を設定する (null でバーの既定に戻す)</summary>
        public void Set(IReadOnlyList<ButtonPrompt> prompts) => _prompts.Value = prompts;

        /// <summary>バーの既定の案内に戻す</summary>
        public void Clear() => _prompts.Value = null;

        public void Dispose() => _prompts.Dispose();
    }
}
