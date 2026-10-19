using System.Collections.Generic;
using R3;

namespace Abubu.Presentation
{
    /// <summary>画面設定画面の「選択式」の項目</summary>
    public enum GraphicsSettingsField
    {
        /// <summary>解像度</summary>
        Resolution,
        /// <summary>表示モード</summary>
        WindowMode,
        /// <summary>画質</summary>
        Quality,
        /// <summary>目標フレームレート</summary>
        FrameRate,
    }

    /// <summary>
    /// 画面設定画面の View。<see cref="GraphicsSettingsPresenter"/> はこのインターフェースだけを扱うため、
    /// uGUI / TextMeshPro / UI Toolkit など任意の UI で実装できる。
    /// </summary>
    /// <remarks>
    /// Set* は表示の更新だけを行い、ユーザー入力の通知 (On*) を発行してはならない (双方向バインドがループするため)。
    /// </remarks>
    public interface IGraphicsSettingsView
    {
        /// <summary>指定項目の操作部品を持っているか。false の項目は結線されない</summary>
        bool HasSelector(GraphicsSettingsField field);

        /// <summary>指定項目の表示/非表示 (実行環境で変更できない項目を隠すのに使う)</summary>
        void SetSelectorVisible(GraphicsSettingsField field, bool visible);

        /// <summary>指定項目の選択肢 (表示用の文字列) を設定する</summary>
        void SetOptions(GraphicsSettingsField field, IReadOnlyList<string> options);

        /// <summary>指定項目の選択中インデックスを表示する</summary>
        void SetSelectedIndex(GraphicsSettingsField field, int index);

        /// <summary>ユーザーが選択を変えたときの通知 (選択肢のインデックス)</summary>
        Observable<int> OnSelected(GraphicsSettingsField field);

        /// <summary>VSync の操作部品を持っているか</summary>
        bool HasVSync { get; }

        /// <summary>VSync の状態を表示する</summary>
        void SetVSync(bool enabled);

        /// <summary>ユーザーが VSync を切り替えたときの通知</summary>
        Observable<bool> OnVSyncChanged { get; }

        /// <summary>選択肢の表示文字列。null なら Presenter が既定 (<see cref="GraphicsSettingsLabels"/>) を使う</summary>
        IGraphicsSettingsLabels Labels { get; }
    }
}
