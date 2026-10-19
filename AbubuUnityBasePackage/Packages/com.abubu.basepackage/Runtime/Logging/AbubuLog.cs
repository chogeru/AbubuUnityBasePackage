using System;
using System.Collections.Generic;
using System.Diagnostics;
using Abubu.Logging;
using Object = UnityEngine.Object;

namespace Abubu
{
    /// <summary>
    /// カテゴリ付きのログ。カテゴリ単位で止められ、Debug / Info はリリースビルドで呼び出しごと消える。
    /// <code>
    /// AbubuLog.Info("Save", "保存しました");
    /// AbubuLog.Debug("AI", $"target = {target}");   // リリースビルドでは文字列の組み立ても行われない
    ///
    /// private static readonly LogChannel Logger = AbubuLog.For("Player");
    /// Logger.Warning("HP がマイナスです");
    /// </code>
    /// </summary>
    /// <remarks>
    /// Debug / Info をリリースビルドでも残したいときは、Player Settings の Scripting Define Symbols に ABUBU_LOG_VERBOSE を追加する。
    /// Warning / Error は常に残る (MinLevel とカテゴリ設定では止められる)。
    /// </remarks>
    public static class AbubuLog
    {
        /// <summary>リリースビルドでも Debug / Info を残すためのシンボル</summary>
        public const string VerboseSymbol = "ABUBU_LOG_VERBOSE";

        /// <summary>これより低いレベルのログは出さない</summary>
        public static LogLevel MinLevel { get; set; } = LogLevel.Debug;

        /// <summary>出力しないカテゴリ。ログはどのスレッドからも呼ばれるため、変更時は作り直して差し替える</summary>
        private static volatile HashSet<string> _disabled = new();

        /// <summary>ファイル出力中ならそのパス。出力していなければ null</summary>
        public static string FilePath => LogFileWriter.CurrentPath;

        /// <summary>設定を反映する (AbubuInstaller から呼ばれる)</summary>
        public static void Configure(LogSettings settings)
        {
            if (settings == null) return;

            MinLevel = settings.MinLevel;
            _disabled = new HashSet<string>(settings.DisabledCategories ?? Array.Empty<string>());
            if (settings.WriteToFile) LogFileWriter.Start(settings.MaxLogFiles);
        }

        /// <summary>カテゴリの出力を切り替える</summary>
        public static void SetCategoryEnabled(string category, bool enabled)
        {
            var next = new HashSet<string>(_disabled);
            if (enabled) next.Remove(category);
            else next.Add(category);
            _disabled = next;
        }

        /// <summary>このレベル・カテゴリのログが出力されるか (重い文字列を組み立てる前の判定に使う)</summary>
        public static bool IsEnabled(LogLevel level, string category) =>
            level >= MinLevel && level != LogLevel.None && (category == null || !_disabled.Contains(category));

        /// <summary>カテゴリを固定したロガー</summary>
        public static LogChannel For(string category) => new(category);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional(VerboseSymbol)]
        public static void Debug(string category, object message, Object context = null)
        {
            if (IsEnabled(LogLevel.Debug, category)) UnityEngine.Debug.Log(Format(category, message), context);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional(VerboseSymbol)]
        public static void Info(string category, object message, Object context = null)
        {
            if (IsEnabled(LogLevel.Info, category)) UnityEngine.Debug.Log(Format(category, message), context);
        }

        public static void Warning(string category, object message, Object context = null)
        {
            if (IsEnabled(LogLevel.Warning, category)) UnityEngine.Debug.LogWarning(Format(category, message), context);
        }

        public static void Error(string category, object message, Object context = null)
        {
            if (IsEnabled(LogLevel.Error, category)) UnityEngine.Debug.LogError(Format(category, message), context);
        }

        /// <summary>例外はカテゴリ・レベルの設定に関係なく常に出力する</summary>
        public static void Exception(Exception exception, Object context = null) => UnityEngine.Debug.LogException(exception, context);

        private static string Format(string category, object message) =>
            string.IsNullOrEmpty(category) ? message?.ToString() : $"[{category}] {message}";

        // Domain Reload 無効時のために再生ごとに初期状態へ戻す
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            MinLevel = LogLevel.Debug;
            _disabled = new HashSet<string>();
            LogFileWriter.Stop();
        }
    }

    /// <summary>
    /// カテゴリを固定したロガー。<see cref="AbubuLog.For"/> で作り、static readonly フィールドに持っておく。
    /// </summary>
    public readonly struct LogChannel
    {
        public string Category { get; }

        public LogChannel(string category) => Category = category;

        public bool IsEnabled(LogLevel level) => AbubuLog.IsEnabled(level, Category);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional(AbubuLog.VerboseSymbol)]
        public void Debug(object message, Object context = null) => AbubuLog.Debug(Category, message, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional(AbubuLog.VerboseSymbol)]
        public void Info(object message, Object context = null) => AbubuLog.Info(Category, message, context);

        public void Warning(object message, Object context = null) => AbubuLog.Warning(Category, message, context);
        public void Error(object message, Object context = null) => AbubuLog.Error(Category, message, context);
        public void Exception(Exception exception, Object context = null) => AbubuLog.Exception(exception, context);
    }
}
