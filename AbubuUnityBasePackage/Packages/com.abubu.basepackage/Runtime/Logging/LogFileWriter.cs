using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Abubu.Logging
{
    /// <summary>
    /// Console に出たログをすべて persistentDataPath/Logs/log_日時.txt に書き出す。
    /// 実機 (特にモバイル) で不具合が出たとき、プレイヤーからファイルを送ってもらうために使う。
    /// </summary>
    /// <remarks>
    /// ログはどのスレッドからも届くので、書き込みは lock で直列化する。
    /// 落ちる直前のログを残せるよう、1 行ごとにフラッシュする。
    /// </remarks>
    internal static class LogFileWriter
    {
        private const string FolderName = "Logs";
        private const string FilePrefix = "log_";

        private static readonly object Gate = new();
        private static StreamWriter _writer;

        public static string CurrentPath { get; private set; }

        public static void Start(int maxFiles)
        {
            // エディタは Console と Editor.log があり、WebGL はファイルに書けない
            if (Application.isEditor || Application.platform == RuntimePlatform.WebGLPlayer) return;

            lock (Gate)
            {
                if (_writer != null) return;
                try
                {
                    var folder = Path.Combine(Application.persistentDataPath, FolderName);
                    Directory.CreateDirectory(folder);
                    // これから作る 1 ファイルを含めて maxFiles 個になるよう、古いものから消す
                    DeleteOldFiles(folder, Math.Max(1, maxFiles) - 1);

                    var path = Path.Combine(folder, $"{FilePrefix}{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{FilePrefix}{DateTime.Now:yyyyMMdd_HHmmss}_{i}.txt");

                    _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
                    _writer.WriteLine($"# {Application.productName} {Application.version} / Unity {Application.unityVersion}");
                    _writer.WriteLine($"# {Application.platform} / {SystemInfo.operatingSystem} / {SystemInfo.deviceModel}");
                    CurrentPath = path;
                }
                catch (Exception e)
                {
                    // ログの書き出しに失敗してもゲームは止めない (OnLog はまだ登録していないので、ここで警告を出しても戻ってこない)
                    _writer?.Dispose();
                    _writer = null;
                    CurrentPath = null;
                    UnityEngine.Debug.LogWarning($"[Abubu.Log] ログファイルを作成できませんでした: {e.Message}");
                    return;
                }
            }

            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting -= Stop;
            Application.quitting += Stop;
        }

        public static void Stop()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.quitting -= Stop;
            lock (Gate)
            {
                _writer?.Dispose();
                _writer = null;
                CurrentPath = null;
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (Gate)
            {
                if (_writer == null) return;
                try
                {
                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{type}] {condition}");
                    // スタックトレースは量が多いので、エラー系だけ残す
                    if (type != LogType.Log && type != LogType.Warning && !string.IsNullOrEmpty(stackTrace)) _writer.WriteLine(stackTrace);
                }
                catch (IOException)
                {
                    // ディスクフルなど。ここでログを出すと再帰するので黙って捨てる
                }
            }
        }

        private static void DeleteOldFiles(string folder, int keep)
        {
            var old = new DirectoryInfo(folder).GetFiles(FilePrefix + "*.txt")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(keep);
            foreach (var file in old)
            {
                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                    // 別プロセスが開いているなど。次回の起動で消える
                }
            }
        }
    }
}
