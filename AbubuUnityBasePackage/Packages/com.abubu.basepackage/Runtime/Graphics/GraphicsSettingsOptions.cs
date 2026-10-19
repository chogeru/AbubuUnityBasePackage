using System;
using System.Collections.Generic;
using UnityEngine;

namespace Abubu.DisplaySettings
{
    /// <summary>
    /// 画面設定機能の構成 (選択肢・保存キー・保存の待ち時間)。
    /// AbubuSettings などに持たせて Inspector から変更できる。
    /// </summary>
    /// <remarks>
    /// ここに無い値 (保存済みの値や、実行環境の現在値) は Model が自動で選択肢に追加する。
    /// </remarks>
    [Serializable]
    public sealed class GraphicsSettingsOptions
    {
        [Tooltip("目標フレームレートの選択肢。-1 はプラットフォーム既定 (無制限)。0 や -2 以下は無視される")]
        public List<int> FrameRateOptions = new() { 30, 60, 120, 144, -1 };

        [Tooltip("表示モードの選択肢。ExclusiveFullScreen は Windows のみ有効、MaximizedWindow は macOS 以外ではフルスクリーンウィンドウ扱い")]
        public List<FullScreenMode> WindowModes = new()
        {
            FullScreenMode.FullScreenWindow,
            FullScreenMode.MaximizedWindow,
            FullScreenMode.Windowed,
        };

        [Tooltip("ISaveService に保存するときのキー")]
        public string SaveKey = "abubu.graphics";

        [Tooltip("変更から保存までの待ち時間 (秒)。ドロップダウンを連続で操作しても書き込みは 1 回にまとめる。Time.timeScale の影響は受けない")]
        [Min(0f)] public float SaveDebounceSeconds = 0.5f;

        [Tooltip("オフにすると保存しない (毎回プロジェクトの既定値で起動する)")]
        public bool Persist = true;
    }
}
