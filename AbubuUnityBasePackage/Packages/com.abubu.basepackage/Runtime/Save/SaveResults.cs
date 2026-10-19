using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;

namespace Abubu.Save
{
    /// <summary>失敗した操作の種類</summary>
    public enum SaveOperation
    {
        Save,
        Load,
        Exists,
        Delete,
    }

    /// <summary>
    /// セーブ/ロードが失敗した理由。UI の出し分け (「空き容量が足りません」など) に使う。
    /// <code>
    /// if (failure.Reason == SaveFailureReason.DiskFull) ShowDialog("空き容量が足りません");
    /// </code>
    /// </summary>
    public enum SaveFailureReason
    {
        /// <summary>失敗していない</summary>
        None = 0,
        /// <summary>分類できない失敗 (詳細は <see cref="SaveFailure.Exception"/>)</summary>
        Unknown,
        /// <summary>キーが空など、呼び出し側の指定が不正</summary>
        InvalidKey,
        /// <summary>データを文字列に変換できなかった (<see cref="ISaveSerializer"/> が例外を投げた)</summary>
        SerializationFailed,
        /// <summary>圧縮・暗号化 (<see cref="ISaveTransform"/>) の適用・復元に失敗した。鍵の違い・改ざん・未登録の変換など</summary>
        TransformFailed,
        /// <summary>保存先の空き容量が足りない</summary>
        DiskFull,
        /// <summary>保存先への権限が無い (読み取り専用・他のプロセスが使用中のフォルダなど)</summary>
        AccessDenied,
        /// <summary>そのほかの入出力エラー (ファイルのロック・パスが長すぎるなど)</summary>
        IoError,
        /// <summary>保存されていたデータが壊れていて復元できなかった</summary>
        Corrupted,
        /// <summary>旧バージョンのデータを変換する <see cref="ISaveMigration"/> が登録されていない</summary>
        MigrationMissing,
        /// <summary>今のビルドより新しいバージョンのデータが保存されているため、読み込み・上書きを拒否した</summary>
        NewerVersion,
    }

    /// <summary>
    /// セーブ/ロードの失敗の詳細。<see cref="ISaveFailureNotifier.SaveFailed"/> や <see cref="SaveResult"/> で受け取る。
    /// <code>
    /// _notifier.SaveFailed += f => Debug.Log($"{f.Operation} {f.Key}: {f.Reason}");
    /// </code>
    /// </summary>
    public sealed class SaveFailure
    {
        /// <summary>対象のキー (保存先に渡したキー。スロット内のデータならスロットの接頭辞付き)</summary>
        public string Key { get; }
        /// <summary>失敗した操作</summary>
        public SaveOperation Operation { get; }
        /// <summary>失敗の理由</summary>
        public SaveFailureReason Reason { get; }
        /// <summary>エラーログと同じ内容の説明 (開発者向け)</summary>
        public string Message { get; }
        /// <summary>原因の例外 (無ければ null)</summary>
        public Exception Exception { get; }

        public SaveFailure(string key, SaveOperation operation, SaveFailureReason reason, string message, Exception exception = null)
        {
            Key = key;
            Operation = operation;
            Reason = reason;
            Message = message;
            Exception = exception;
        }

        public override string ToString() => $"{Operation} \"{Key}\" failed ({Reason}): {Message}";
    }

    /// <summary>
    /// 保存の結果。bool に暗黙変換できるので、成否だけ見る場合は if にそのまま書ける。
    /// <code>
    /// var result = await _save.SaveAsync("progress", progress);
    /// if (!result) ShowError(result.Reason);
    /// </code>
    /// </summary>
    public readonly struct SaveResult
    {
        /// <summary>失敗の詳細。成功時は null</summary>
        public SaveFailure Failure { get; }

        /// <summary>保存先への書き込みまで完了したか</summary>
        public bool Success => Failure == null;

        /// <summary>失敗の理由。成功時は <see cref="SaveFailureReason.None"/></summary>
        public SaveFailureReason Reason => Failure?.Reason ?? SaveFailureReason.None;

        public SaveResult(SaveFailure failure) => Failure = failure;

        /// <summary>成功</summary>
        public static SaveResult Ok => default;

        public static implicit operator bool(SaveResult result) => result.Success;

        public override string ToString() => Success ? "Ok" : Failure.ToString();
    }

    /// <summary>
    /// 読み込みの結果。「データが無い」と「読み込みに失敗した」を区別できる。
    /// <code>
    /// var result = await _save.LoadAsync&lt;Progress&gt;("progress");
    /// var progress = result.GetValueOrDefault(new Progress());
    /// if (result.Failure != null) ShowError(result.Failure.Reason);
    /// </code>
    /// </summary>
    public readonly struct SaveLoadResult<T>
    {
        /// <summary>読み込めたか</summary>
        public bool Success { get; }
        /// <summary>読み込んだデータ (失敗時は default)</summary>
        public T Value { get; }
        /// <summary>失敗の詳細。成功時とデータが無かった時は null</summary>
        public SaveFailure Failure { get; }

        /// <summary>データが保存されていなかった (失敗ではない)</summary>
        public bool NotFound => !Success && Failure == null;

        private SaveLoadResult(bool success, T value, SaveFailure failure)
        {
            Success = success;
            Value = value;
            Failure = failure;
        }

        public static SaveLoadResult<T> Loaded(T value) => new(true, value, null);
        public static SaveLoadResult<T> Missing() => new(false, default, null);
        public static SaveLoadResult<T> Failed(SaveFailure failure) => new(false, default, failure);

        /// <summary>読み込めた場合はその値、それ以外は <paramref name="defaultValue"/></summary>
        public T GetValueOrDefault(T defaultValue = default) => Success ? Value : defaultValue;

        public static implicit operator bool(SaveLoadResult<T> result) => result.Success;
    }

    /// <summary>
    /// セーブ/ロードの失敗を通知する。<see cref="SaveService"/> が実装している。
    /// 公開されている操作 (Save / Load / Exists / Delete とその Async 版) 1 回の失敗につき 1 回、メインスレッドで呼ばれる。
    /// データが無いだけの場合や、バックアップから復旧できた場合は呼ばれない。
    /// <code>
    /// [Inject] void Construct(ISaveFailureNotifier notifier) =&gt;
    ///     notifier.SaveFailed += f =&gt; { if (f.Reason == SaveFailureReason.DiskFull) ShowDiskFullDialog(); };
    /// </code>
    /// </summary>
    public interface ISaveFailureNotifier
    {
        /// <summary>失敗したときに呼ばれる。ハンドラの例外は捕まえてログに出す (保存処理には影響しない)</summary>
        event Action<SaveFailure> SaveFailed;
    }

    /// <summary>
    /// 例外から <see cref="SaveFailureReason"/> を判定する。自作の <see cref="ISaveStorage"/> の例外もここで分類される。
    /// <code>
    /// var reason = SaveErrors.Classify(exception); // IOException (ディスクフル) → DiskFull
    /// </code>
    /// </summary>
    public static class SaveErrors
    {
        // Windows のエラーコード (HRESULT の下位 16 ビット)
        private const int ErrorHandleDiskFull = 0x27;
        private const int ErrorDiskFull = 0x70;
        private const int ErrorAccessDenied = 0x05;

        /// <summary>例外の種類・エラーコード・メッセージから理由を判定する。判定できなければ <see cref="SaveFailureReason.Unknown"/></summary>
        public static SaveFailureReason Classify(Exception exception)
        {
            switch (exception)
            {
                case null:
                    return SaveFailureReason.Unknown;
                case UnauthorizedAccessException:
                case SecurityException:
                    return SaveFailureReason.AccessDenied;
                case CryptographicException:
                    return SaveFailureReason.TransformFailed;
                case IOException io:
                    if (IsDiskFull(io)) return SaveFailureReason.DiskFull;
                    if ((io.HResult & 0xFFFF) == ErrorAccessDenied) return SaveFailureReason.AccessDenied;
                    return SaveFailureReason.IoError;
                case FormatException:
                    return SaveFailureReason.Corrupted;
                default:
                    return exception.InnerException != null ? Classify(exception.InnerException) : SaveFailureReason.Unknown;
            }
        }

        /// <summary>
        /// 空き容量不足か。Windows はエラーコード、それ以外 (Mono / IL2CPP の各 OS) はメッセージで判定する
        /// (ENOSPC のメッセージは "No space left on device" / "Disk full" など)。
        /// </summary>
        private static bool IsDiskFull(IOException io)
        {
            var code = io.HResult & 0xFFFF;
            if (code == ErrorDiskFull || code == ErrorHandleDiskFull) return true;
            var message = io.Message ?? "";
            return message.IndexOf("disk full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("no space left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("not enough space", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
