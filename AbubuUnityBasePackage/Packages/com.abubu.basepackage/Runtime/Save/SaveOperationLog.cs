using System;
using System.Collections.Generic;
using UnityEngine;

namespace Abubu.Save
{
    /// <summary>
    /// 1 回の公開操作 (Save / Load / Exists / Delete) のログと失敗を集める。メインスレッドではその場でログを出し、
    /// 別スレッドではためておいてメインスレッドに戻ってから出す (<see cref="SaveFailureReporter.Complete"/>)
    /// </summary>
    internal sealed class SaveOperationLog
    {
        public readonly SaveOperation Operation;
        public readonly string Key;
        private readonly bool _buffered;
        private List<(LogType type, string message)> _entries;

        /// <summary>この操作の失敗 (成功なら null)。最初の失敗を残す</summary>
        public SaveFailure Failure;

        /// <param name="buffered">true ならログをためておく (別スレッドで使う場合)</param>
        public SaveOperationLog(SaveOperation operation, string key, bool buffered = false)
        {
            Operation = operation;
            Key = key;
            _buffered = buffered;
        }

        public void Error(string message) => Write(LogType.Error, message);
        public void Warning(string message) => Write(LogType.Warning, message);

        /// <summary>失敗を記録する。log が true ならエラーログも出す</summary>
        public SaveFailure Fail(SaveFailureReason reason, string message, Exception exception = null, bool log = true)
        {
            if (log) Error(message);
            return Failure ??= new SaveFailure(Key, Operation, reason, message, exception);
        }

        private void Write(LogType type, string message)
        {
            message = "[Abubu.Save] " + message;
            if (!_buffered)
            {
                Emit(type, message);
                return;
            }
            (_entries ??= new List<(LogType, string)>()).Add((type, message));
        }

        /// <summary>ためておいたログを出す (メインスレッドで呼ぶ)</summary>
        public void FlushLogs()
        {
            if (_entries == null) return;
            foreach (var (type, message) in _entries) Emit(type, message);
            _entries = null;
        }

        private static void Emit(LogType type, string message)
        {
            if (type == LogType.Warning) Debug.LogWarning(message);
            else Debug.LogError(message);
        }
    }
}
