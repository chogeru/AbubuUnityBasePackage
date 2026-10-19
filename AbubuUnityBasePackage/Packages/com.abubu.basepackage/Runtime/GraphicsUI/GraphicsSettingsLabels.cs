using System;
using UnityEngine;

namespace Abubu.Presentation
{
    /// <summary>
    /// 画面設定の選択肢を表示用の文字列にする。ローカライズしたい場合はこれを実装してコンテナにバインドするか、
    /// View の Labels で返す。
    /// </summary>
    public interface IGraphicsSettingsLabels
    {
        /// <summary>解像度の表示名 (例: "1920 x 1080")</summary>
        string Resolution(Vector2Int resolution);

        /// <summary>表示モードの表示名 (例: "Fullscreen")</summary>
        string WindowMode(FullScreenMode mode);

        /// <summary>画質の表示名。<paramref name="qualityName"/> は QualitySettings に登録された名前</summary>
        string Quality(int level, string qualityName);

        /// <summary>目標フレームレートの表示名。-1 はプラットフォーム既定 (無制限)</summary>
        string FrameRate(int frameRate);
    }

    /// <summary>
    /// <see cref="IGraphicsSettingsLabels"/> の既定実装。英語の既定値を持ち、Inspector で書き換えられる。
    /// </summary>
    /// <remarks>
    /// *Format は string.Format の書式。Resolution は {0} = 幅、{1} = 高さ。FrameRate は {0} = fps。
    /// </remarks>
    [Serializable]
    public sealed class GraphicsSettingsLabels : IGraphicsSettingsLabels
    {
        public string ResolutionFormat = "{0} x {1}";
        public string ExclusiveFullScreen = "Exclusive Fullscreen";
        public string FullScreenWindow = "Fullscreen";
        public string MaximizedWindow = "Borderless";
        public string Windowed = "Windowed";
        public string FrameRateFormat = "{0} FPS";
        [Tooltip("目標フレームレート -1 (プラットフォーム既定) の表示名")]
        public string UnlimitedFrameRate = "Unlimited";

        /// <inheritdoc />
        public string Resolution(Vector2Int resolution) => SafeFormat(ResolutionFormat, resolution.x, resolution.y);

        /// <inheritdoc />
        public string WindowMode(FullScreenMode mode) => mode switch
        {
            FullScreenMode.ExclusiveFullScreen => ExclusiveFullScreen,
            FullScreenMode.FullScreenWindow => FullScreenWindow,
            FullScreenMode.MaximizedWindow => MaximizedWindow,
            FullScreenMode.Windowed => Windowed,
            _ => mode.ToString(),
        };

        /// <inheritdoc />
        public string Quality(int level, string qualityName) => qualityName;

        /// <inheritdoc />
        public string FrameRate(int frameRate) => frameRate < 0 ? UnlimitedFrameRate : SafeFormat(FrameRateFormat, frameRate);

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
