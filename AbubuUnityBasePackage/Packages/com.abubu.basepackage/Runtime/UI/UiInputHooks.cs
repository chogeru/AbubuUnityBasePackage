using TMPro;
using UnityEngine;

namespace Abubu.UI
{
    /// <summary>
    /// 今の操作がゲームパッド (方向入力で UI を選ぶ操作) かどうか。UI モジュールは入力デバイスを知らないので、
    /// Input モジュールとの連携 (Abubu.UI.Input) が実装してバインドする。バインドされていなければマウス操作として扱う。
    /// </summary>
    public interface IUiInputModeSource
    {
        /// <summary>ゲームパッドなど、方向入力で UI を操作しているなら true</summary>
        bool IsNavigationMode { get; }
    }

    /// <summary>決定ボタンが押されたままか (<see cref="UiHoldButton"/> の長押し用)。Abubu.UI.Input がバインドする</summary>
    public interface IUiSubmitHoldSource
    {
        bool IsSubmitHeld { get; }
    }

    /// <summary>
    /// 「戻る」(Cancel 入力) を止めたいときにバインドする。いずれかが true を返す間、Cancel 入力でポップアップを閉じない
    /// (キー割り当ての入力待ち中・デバッグメニュー操作中など)
    /// </summary>
    public interface IUiBackBlocker
    {
        bool BlocksBack { get; }
    }

    /// <summary>
    /// 組み込みのダイアログ・トーストに使うフォント。既定は AbubuUiSettings.DefaultFont / DefaultTmpFont。
    /// ローカライズが有効なら、言語ごとのフォントを返す実装がバインドされる
    /// </summary>
    public interface IUiFontProvider
    {
        Font Font { get; }
        TMP_FontAsset TmpFont { get; }
    }

    /// <summary>AbubuUiSettings のフォントを使う既定の実装</summary>
    public sealed class SettingsUiFontProvider : IUiFontProvider
    {
        private readonly AbubuUiSettings _settings;

        public SettingsUiFontProvider(AbubuUiSettings settings) => _settings = settings != null ? settings : AbubuUiSettings.Default;

        public Font Font => _settings.DefaultFont;
        public TMP_FontAsset TmpFont => _settings.DefaultTmpFont;
    }
}
