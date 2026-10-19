using System;
using UnityEngine;

namespace Abubu.DisplaySettings
{
    /// <summary>
    /// 画面設定の保存形式。<see cref="ExplicitMask"/> に含まれる項目だけが「ユーザーが設定した値」として復元・適用される。
    /// </summary>
    [Serializable]
    public sealed class GraphicsSettingsData
    {
        public int Width;
        public int Height;
        public FullScreenMode FullScreenMode = FullScreenMode.FullScreenWindow;
        public int QualityLevel = -1;
        public bool VSync = true;
        /// <summary>-1 = プラットフォーム既定</summary>
        public int TargetFrameRate = -1;

        /// <summary>
        /// 明示的に設定された項目のビットマスク (bit = <see cref="GraphicsSettingKind"/> の値)。
        /// このフィールドが無い旧形式のデータは初期値 -1 (全項目) として読み込まれ、従来どおり全項目を復元する。
        /// </summary>
        public int ExplicitMask = -1;

        /// <summary>指定項目がマスクに含まれるか</summary>
        public bool Has(GraphicsSettingKind kind) => (ExplicitMask & (1 << (int)kind)) != 0;
    }
}
