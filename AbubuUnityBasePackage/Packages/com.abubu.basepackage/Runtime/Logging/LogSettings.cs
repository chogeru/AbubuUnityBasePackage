using System;
using UnityEngine;

namespace Abubu.Logging
{
    /// <summary>ログの重要度。<see cref="LogSettings.MinLevel"/> より低いものは出力しない</summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        /// <summary>何も出さない (MinLevel 用)</summary>
        None = 4,
    }

    [Serializable]
    public sealed class LogSettings
    {
        [Tooltip("これより低いレベルのログは出さない。Debug / Info はリリースビルドではコンパイル時に呼び出しごと除去される")]
        public LogLevel MinLevel = LogLevel.Debug;

        [Tooltip("出力しないカテゴリ (AbubuLog.For(\"AI\") の \"AI\" など)")]
        public string[] DisabledCategories = Array.Empty<string>();

        [Tooltip("Console に出たログ (Unity や他ライブラリのものも含む) を persistentDataPath/Logs に書き出す。エディタと WebGL では書き出さない")]
        public bool WriteToFile;

        [Tooltip("残しておくログファイルの数 (起動 1 回につき 1 ファイル)")]
        [Min(1)] public int MaxLogFiles = 5;
    }
}
