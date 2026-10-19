namespace Abubu.DisplaySettings
{
    /// <summary>
    /// <see cref="GraphicsSettingsModel"/> が持つ設定項目の種類。
    /// 変更通知 (<see cref="GraphicsSettingsModel.Changed"/>) や「明示的に設定済みか」の判定に使う。
    /// </summary>
    public enum GraphicsSettingKind
    {
        /// <summary>解像度 (幅 x 高さ)</summary>
        Resolution,
        /// <summary>表示モード (フルスクリーン / ウィンドウなど)</summary>
        FullScreenMode,
        /// <summary>画質レベル (QualitySettings のインデックス)</summary>
        QualityLevel,
        /// <summary>垂直同期</summary>
        VSync,
        /// <summary>目標フレームレート (-1 = プラットフォーム既定)</summary>
        TargetFrameRate,
    }
}
