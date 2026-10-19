using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Abubu.DisplaySettings
{
    /// <summary>
    /// 実行環境の画面情報のスナップショット (選択可能な解像度・画質名・現在値など)。
    /// <see cref="GraphicsSettingsModel"/> は Unity の API を直接読まず、これを通して初期値と選択肢を得る。
    /// </summary>
    /// <remarks>
    /// 通常は <see cref="Capture"/> で現在の環境から作る。テストや特殊なプラットフォームでは値を直接埋めて渡せる。
    /// </remarks>
    public sealed class GraphicsPlatformInfo
    {
        /// <summary>解像度・表示モードを変更できるか (WebGL / モバイルは不可)</summary>
        public bool SupportsResolution { get; set; }

        /// <summary>選択可能な解像度 (面積の小さい順)</summary>
        public IReadOnlyList<Vector2Int> Resolutions { get; set; } = new Vector2Int[0];

        /// <summary>画質レベルの名前 (QualitySettings.names)</summary>
        public IReadOnlyList<string> QualityNames { get; set; } = new string[0];

        /// <summary>現在の解像度</summary>
        public Vector2Int CurrentResolution { get; set; }

        /// <summary>現在の表示モード</summary>
        public FullScreenMode CurrentFullScreenMode { get; set; } = FullScreenMode.Windowed;

        /// <summary>現在の画質レベル</summary>
        public int CurrentQualityLevel { get; set; }

        /// <summary>現在 VSync が有効か (vSyncCount &gt; 0)</summary>
        public bool CurrentVSync { get; set; }

        /// <summary>現在の Application.targetFrameRate</summary>
        public int CurrentTargetFrameRate { get; set; } = -1;

        /// <summary>現在の実行環境から情報を集める。メインスレッドから呼ぶこと。</summary>
        public static GraphicsPlatformInfo Capture()
        {
            var supportsResolution = !Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer;
            var current = new Vector2Int(Screen.width, Screen.height);

            return new GraphicsPlatformInfo
            {
                SupportsResolution = supportsResolution,
                // リフレッシュレート違いで同じ解像度が複数並ぶため Distinct する
                Resolutions = Screen.resolutions
                    .Select(r => new Vector2Int(r.width, r.height))
                    .Distinct()
                    .OrderBy(r => r.x * r.y)
                    .ThenBy(r => r.x)
                    .ToArray(),
                QualityNames = QualitySettings.names,
                CurrentResolution = current,
                CurrentFullScreenMode = Screen.fullScreenMode,
                CurrentQualityLevel = QualitySettings.GetQualityLevel(),
                CurrentVSync = QualitySettings.vSyncCount > 0,
                CurrentTargetFrameRate = Application.targetFrameRate,
            };
        }
    }
}
