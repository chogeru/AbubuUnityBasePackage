using System;
using System.Collections.Generic;
using Abubu.Inputs;
using R3;
using UnityEngine;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// 割り当て変更画面の 1 行の設定 (どのアクションのどの割り当てを変えるか)。
    /// <code>
    /// new RebindEntry { ActionPath = "Player/Jump", DisplayName = "Jump" }                 // 使用中のデバイス向けの割り当て
    /// new RebindEntry { ActionPath = "Player/Move", BindingIndex = 1, DisplayName = "Move" } // 割り当てを固定 (WASD の複合など)
    /// </code>
    /// </summary>
    [Serializable]
    public sealed class RebindEntry
    {
        [Tooltip("\"Map/Action\" 形式のアクションのパス")]
        public string ActionPath;

        [Tooltip("表示名。空ならアクション名 (IRebindLabels.ActionName で変換される)")]
        public string DisplayName;

        [Tooltip("変更する割り当てのインデックス。-1 なら使用中のデバイス (キーボード / ゲームパッド) 向けの割り当てを自動で選ぶ。" +
                 "複合バインディング (WASD など) の先頭を指定すると、各パートを順に割り当てる")]
        public int BindingIndex = -1;

        public RebindEntry() { }

        public RebindEntry(string actionPath, string displayName = null, int bindingIndex = -1)
        {
            ActionPath = actionPath;
            DisplayName = displayName;
            BindingIndex = bindingIndex;
        }
    }

    /// <summary>割り当て変更画面の 1 行に表示する内容</summary>
    public readonly struct RebindRowState
    {
        /// <summary>アクションの表示名</summary>
        public readonly string ActionName;
        /// <summary>現在の割り当ての表示文字列 (入力待ち中は「入力待ち」の文字列)</summary>
        public readonly string BindingText;
        /// <summary>現在の割り当てのボタンアイコン (無ければ null)</summary>
        public readonly Sprite Glyph;
        /// <summary>この行が入力待ち中か</summary>
        public readonly bool IsListening;
        /// <summary>同じ ActionMap の別の割り当てと操作がかぶっているか</summary>
        public readonly bool HasConflict;
        /// <summary>かぶっている場合の説明 (かぶっていなければ空)</summary>
        public readonly string ConflictText;
        /// <summary>初期状態から変更されているか (リセットボタンの有効/無効に使う)</summary>
        public readonly bool IsOverridden;
        /// <summary>割り当て変更を始められるか (アクションが無い・他の行が入力待ち中なら false)</summary>
        public readonly bool CanRebind;

        public RebindRowState(string actionName, string bindingText, Sprite glyph, bool isListening, bool hasConflict,
            string conflictText, bool isOverridden, bool canRebind)
        {
            ActionName = actionName ?? string.Empty;
            BindingText = bindingText ?? string.Empty;
            Glyph = glyph;
            IsListening = isListening;
            HasConflict = hasConflict;
            ConflictText = conflictText ?? string.Empty;
            IsOverridden = isOverridden;
            CanRebind = canRebind;
        }
    }

    /// <summary>
    /// 割り当て変更 (キーコンフィグ) 画面の View。<see cref="RebindPresenter"/> はこのインターフェースだけを扱うため、
    /// uGUI / TextMeshPro / UI Toolkit など任意の UI で実装できる。
    /// <code>
    /// public sealed class MyRebindView : ViewBase&lt;RebindPresenter&gt;, IRebindView { ... }   // 独自 UI で実装する場合
    /// </code>
    /// </summary>
    /// <remarks>
    /// 行の数・順番は <see cref="Entries"/> で決まる。行番号 (row) は Entries のインデックス。
    /// Set* は表示の更新だけを行い、ユーザー操作の通知 (On*) を発行してはならない。
    /// </remarks>
    public interface IRebindView
    {
        /// <summary>表示する行の設定</summary>
        IReadOnlyList<RebindEntry> Entries { get; }

        /// <summary>同じ ActionMap の別の割り当てと操作がかぶったときの扱い</summary>
        RebindConflictPolicy ConflictPolicy { get; }

        /// <summary>表示文字列。null なら Presenter が既定 (<see cref="RebindLabels"/>) を使う</summary>
        IRebindLabels Labels { get; }

        /// <summary>行の内容を表示する</summary>
        void SetRow(int row, in RebindRowState state);

        /// <summary>入力待ち表示 (「キーを押してください」など) の表示/非表示</summary>
        void SetListening(bool listening, string prompt);

        /// <summary>結果の通知 (入れ替えた・取り消した など) を表示する。空文字なら消す</summary>
        void SetMessage(string message);

        /// <summary>ユーザーが行の割り当て変更を求めた (行番号)</summary>
        Observable<int> OnRebindRequested { get; }

        /// <summary>ユーザーが行の割り当てを初期状態に戻すよう求めた (行番号)</summary>
        Observable<int> OnResetRequested { get; }

        /// <summary>ユーザーがすべての割り当てを初期状態に戻すよう求めた</summary>
        Observable<Unit> OnResetAllRequested { get; }

        /// <summary>ユーザーが入力待ちのキャンセルを求めた</summary>
        Observable<Unit> OnCancelRequested { get; }
    }
}
