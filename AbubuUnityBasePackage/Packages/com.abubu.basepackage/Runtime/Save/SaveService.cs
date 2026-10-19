using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Abubu.Save
{
    /// <summary>
    /// <see cref="ISaveService"/> の標準実装。非同期版 (<see cref="IAsyncSaveService"/>) と失敗の通知 (<see cref="ISaveFailureNotifier"/>) も持つ。
    /// <code>
    /// var save = new SaveService(new FileSaveStorage(), new JsonUtilitySaveSerializer());
    /// save.SaveFailed += f =&gt; Debug.Log(f.Reason);
    /// save.Save("progress", progress);
    /// </code>
    /// </summary>
    /// <remarks>
    /// 保存形式: ユーザーデータを <see cref="ISaveSerializer"/> で文字列化し、(登録されていれば <see cref="ISaveTransform"/> で圧縮・暗号化して Base64 にし)
    /// それをバージョンや型名と一緒に Envelope (JSON) に包んで <see cref="ISaveStorage"/> に書き込む。
    /// 読み込み時に保存時のバージョンが古ければ、マイグレーションを順に適用してから復元する。
    /// <para>
    /// 新しいバージョンのデータの保護: 読み込み時、または (この実行中にまだ読んでいないキーへの) 最初の保存時に
    /// 保存済みデータのバージョンを確認し、今のビルドより新しければそのキーを保護対象にする (<see cref="ISaveService"/> 参照)。
    /// そのため各キーの最初の Save では 1 回だけ保存先の読み込みが発生する (非同期版では別スレッドで行う)。
    /// </para>
    /// <para>
    /// 圧縮 (<see cref="GZipSaveTransform"/>) は登録していなくても読み込みには常に使うため、圧縮の有効/無効を途中で切り替えても以前のデータは読める。
    /// 暗号化など、読み込みに必要な変換が登録されていない場合は <see cref="SaveFailureReason.TransformFailed"/> で失敗し、
    /// データは壊れていないのでバックアップからの復旧や .broken への退避はせず、そのまま残す。
    /// さらに新しいバージョンのデータと同じく、そのキーへの Save / SaveAsync は <see cref="SaveFailureReason.TransformFailed"/> で拒否する
    /// (読み込み時、またはまだ読んでいないキーへの最初の保存時に見出しだけを確認して判定する。ForceSave / Delete で解除)。
    /// </para>
    /// <para>
    /// 内部の分担: 保存形式と変換は SaveEnvelopeCodec、復元は SaveDecoder、マイグレーションは SaveMigrator、
    /// 上書きの保護 (新しいバージョン・変換の未登録) は SaveOverwriteGuard、バックアップからの復旧と .broken への退避は SaveRecovery、
    /// 非同期処理の順序は SaveWriteQueue、失敗の通知は SaveFailureReporter。このクラスはそれらを公開 API の手順どおりに呼ぶだけ。
    /// </para>
    /// メインスレッドから使う前提。非同期版の別スレッドで行う処理は内部で排他している。
    /// </remarks>
    public sealed class SaveService : ISaveService, IAsyncSaveService, ISaveFailureNotifier
    {
        /// <summary>Envelope に書き込む、Abubu のセーブデータであることを示す目印</summary>
        public const string FormatMarker = "abubu.save";

        /// <summary>読み込めなかったデータを退避するキーの接尾辞 ("progress" → "progress.broken")</summary>
        public const string BrokenSuffix = ".broken";

        /// <summary>今のビルドより新しいバージョンのデータを退避するキーの接尾辞 ("progress" → "progress.newer")</summary>
        public const string NewerSuffix = ".newer";

        private readonly ISaveStorage _storage;
        private readonly ISaveSerializer _serializer;

        /// <summary>保存先が別スレッドから使えるか (非同期版で別スレッドを使うか)</summary>
        private readonly bool _threadSafeStorage;

        private readonly SaveEnvelopeCodec _codec;
        private readonly SaveOverwriteGuard _guard;
        private readonly SaveDecoder _decoder;
        private readonly SaveRecovery _recovery;
        private readonly SaveWriteQueue _queue = new();
        private readonly SaveFailureReporter _reporter = new();

        /// <inheritdoc />
        public event Action<SaveFailure> SaveFailed
        {
            add => _reporter.Failed += value;
            remove => _reporter.Failed -= value;
        }

        /// <param name="storage">保存先</param>
        /// <param name="serializer">データ ⇔ 文字列の変換方法</param>
        /// <param name="migrations">
        /// DI コンテナに登録されたマイグレーション一覧 (無ければ null)。
        /// 同じ (型, バージョン) が重複して登録されていると例外 (ArgumentException) を投げる。
        /// </param>
        /// <param name="transforms">
        /// DI コンテナに登録された圧縮・暗号化 (無ければ null = 変換なし)。この順に適用する。Id が重複していると例外を投げる。
        /// 読み込みには、ここに無くても組み込みの GZip を使う。
        /// </param>
        public SaveService(ISaveStorage storage, ISaveSerializer serializer, List<ISaveMigration> migrations = null,
            List<ISaveTransform> transforms = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _threadSafeStorage = storage is IThreadSafeSaveStorage;
            var migrator = new SaveMigrator(migrations);
            _codec = new SaveEnvelopeCodec(transforms);
            _guard = new SaveOverwriteGuard(storage, _codec);
            _decoder = new SaveDecoder(_codec, migrator, serializer, _guard);
            _recovery = new SaveRecovery(storage, _decoder);
        }

        // ───────────────────────── 保存 ─────────────────────────

        /// <inheritdoc />
        public bool Save<T>(string key, T data) => SaveSync(key, data, false) == null;

        /// <inheritdoc />
        public bool ForceSave<T>(string key, T data) => SaveSync(key, data, true) == null;

        /// <inheritdoc />
        public Task<SaveResult> SaveAsync<T>(string key, T data) => SaveAsyncCore(key, data, false);

        /// <inheritdoc />
        public Task<SaveResult> ForceSaveAsync<T>(string key, T data) => SaveAsyncCore(key, data, true);

        private SaveFailure SaveSync<T>(string key, T data, bool force)
        {
            var log = new SaveOperationLog(SaveOperation.Save, key);
            if (ValidateKey(key, log))
            {
                _queue.WaitPending(key);
                if (TrySerialize(key, data, log, out var payload)) WriteCore(key, payload, force, log);
            }
            return _reporter.Complete(log);
        }

        private Task<SaveResult> SaveAsyncCore<T>(string key, T data, bool force)
        {
            if (!_threadSafeStorage) return Task.FromResult(new SaveResult(SaveSync(key, data, force)));

            // キーの検証とシリアライズはメインスレッド (シリアライザが Unity の API を使ってもよいように)
            var log = new SaveOperationLog(SaveOperation.Save, key);
            if (!ValidateKey(key, log) || !TrySerialize(key, data, log, out var payload))
            {
                return Task.FromResult(new SaveResult(_reporter.Complete(log)));
            }

            // 変換・書き込みは別スレッド。ログはためておき、メインスレッドに戻ってから出す
            var background = new SaveOperationLog(SaveOperation.Save, key, buffered: true);
            var task = _queue.Enqueue(key, () => WriteCore(key, payload, force, background));
            return CompleteAfter(task, background);
        }

        private async Task<SaveResult> CompleteAfter(Task task, SaveOperationLog log)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                // WriteCore は例外を投げないが、念のため
                log.Fail(SaveErrors.Classify(e), $"\"{log.Key}\" の保存に失敗しました: {e}", e);
            }
            return new SaveResult(_reporter.Complete(log));
        }

        /// <summary>データをシリアライズする (メインスレッド)</summary>
        private bool TrySerialize<T>(string key, T data, SaveOperationLog log, out SavePayload payload)
        {
            try
            {
                payload = new SavePayload(_serializer.Serialize(data), SaveDataVersion<T>.Value, typeof(T).FullName);
                return true;
            }
            catch (Exception e)
            {
                payload = default;
                log.Fail(SaveFailureReason.SerializationFailed, $"\"{key}\" の保存に失敗しました: {e}", e);
                return false;
            }
        }

        /// <summary>
        /// 新しいバージョンのデータの保護を確認し、Envelope に包んで書き込む。失敗はすべて log に記録する。
        /// 保存先がスレッドセーフなら別スレッドから呼んでよい
        /// </summary>
        private void WriteCore(string key, SavePayload payload, bool force, SaveOperationLog log)
        {
            if (!force)
            {
                _guard.EnsureChecked(key, payload.Version, log);
                if (_guard.IsProtected(key, out var reason))
                {
                    log.Fail(reason, reason == SaveFailureReason.NewerVersion
                        ? $"\"{key}\" には今のビルドより新しいバージョンのデータが保存されているため、上書きしませんでした。" +
                          $"上書きしてよい場合は ForceSave を使うか Delete してから保存してください (元データは \"{key}{NewerSuffix}\" にも退避済み)。"
                        : $"\"{key}\" には読み込みに必要な変換 (暗号化の鍵など) が登録されていないデータが保存されているため、上書きしませんでした。" +
                          "ISaveTransform を登録するか、上書きしてよい場合は ForceSave を使うか Delete してから保存してください。");
                    return;
                }
            }

            string text;
            try
            {
                text = _codec.Encode(payload);
            }
            catch (Exception e)
            {
                log.Fail(SaveFailureReason.TransformFailed, $"\"{key}\" の保存に失敗しました: {e}", e);
                return;
            }

            try
            {
                _storage.Write(key, text);
            }
            catch (Exception e)
            {
                // ディスクフル・権限エラーなどでゲームを止めない (自動保存の購読内から呼ばれることもある)
                log.Fail(SaveErrors.Classify(e), $"\"{key}\" の保存に失敗しました: {e}", e);
                return;
            }

            if (force) _guard.OnForceWritten(key);
        }

        // ───────────────────────── 読み込み ─────────────────────────

        /// <inheritdoc />
        /// <remarks>
        /// 本体が壊れていた場合、保存先が <see cref="ISaveBackupStorage"/> ならバックアップからの復旧を試みる
        /// (壊れた本体は "キー + .broken" に退避し、バックアップは残したまま本体だけを戻す)。
        /// 復旧もできなかったデータは、次の Save で上書きされて失われないよう "キー + .broken" に退避する。
        /// 読み込みに必要な変換 (暗号化など) が登録されていない場合は、データを壊れたものとして扱わず、復旧・退避もしない。
        /// </remarks>
        public bool TryLoad<T>(string key, out T data)
        {
            var log = new SaveOperationLog(SaveOperation.Load, key);
            var loaded = TryLoadCore(key, out data, log);
            _reporter.Complete(log);
            return loaded;
        }

        private bool TryLoadCore<T>(string key, out T data, SaveOperationLog log)
        {
            data = default;
            if (!ValidateKey(key, log)) return false;
            _queue.WaitPending(key);
            _guard.MarkChecked(key);

            string text;
            try
            {
                if (!_storage.TryRead(key, out text)) return false;
            }
            catch (Exception e)
            {
                log.Fail(SaveErrors.Classify(e), $"\"{key}\" の読み込みに失敗しました: {e}", e);
                return false;
            }

            return HandleLoaded(key, text, null, out data, log);
        }

        /// <inheritdoc />
        public async Task<SaveLoadResult<T>> LoadAsync<T>(string key)
        {
            if (!_threadSafeStorage)
            {
                var syncLog = new SaveOperationLog(SaveOperation.Load, key);
                var loaded = TryLoadCore<T>(key, out var syncData, syncLog);
                var failure = _reporter.Complete(syncLog);
                return loaded ? SaveLoadResult<T>.Loaded(syncData) : failure != null ? SaveLoadResult<T>.Failed(failure) : SaveLoadResult<T>.Missing();
            }

            var log = new SaveOperationLog(SaveOperation.Load, key);
            if (!ValidateKey(key, log)) return SaveLoadResult<T>.Failed(_reporter.Complete(log));

            // 読み込み・Envelope の解析・変換の復元は別スレッド。
            // マイグレーション (利用者のコード) とデシリアライズ、壊れていた場合の復旧はメインスレッドで行う
            var currentVersion = SaveDataVersion<T>.Value;
            string text = null;
            var found = false;
            Exception readError = null;
            SaveUnwrapped unwrapped = null;
            await _queue.Enqueue(key, () =>
            {
                try
                {
                    found = _storage.TryRead(key, out text);
                }
                catch (Exception e)
                {
                    readError = e;
                    return;
                }
                // 済ませられなかった場合 (壊れている・新しいバージョンなど) はメインスレッドで同期版と同じ流れで扱う
                if (found) unwrapped = _decoder.TryUnwrap(text, currentVersion);
            });

            // ここからメインスレッド。後から予約された書き込みと重ならないよう待つ
            _queue.WaitPending(key);
            _guard.MarkChecked(key);
            if (readError != null)
            {
                log.Fail(SaveErrors.Classify(readError), $"\"{key}\" の読み込みに失敗しました: {readError}", readError);
                return SaveLoadResult<T>.Failed(_reporter.Complete(log));
            }
            if (!found) return SaveLoadResult<T>.Missing();

            var ok = HandleLoaded<T>(key, text, unwrapped, out var data, log);
            var loadFailure = _reporter.Complete(log);
            return ok ? SaveLoadResult<T>.Loaded(data) : SaveLoadResult<T>.Failed(loadFailure);
        }

        /// <summary>読み込んだ文字列を復元する。壊れていればバックアップからの復旧・.broken への退避まで行う (メインスレッド)</summary>
        private bool HandleLoaded<T>(string key, string text, SaveUnwrapped unwrapped, out T data, SaveOperationLog log)
        {
            switch (_decoder.Decode(key, text, unwrapped, out data, out var needsResave, log))
            {
                case SaveDecodeResult.Ok:
                    // 変換したら新しい形式で保存し直す
                    if (needsResave) Resave(key, data);
                    return true;
                case SaveDecodeResult.Newer:
                case SaveDecodeResult.TransformMissing:
                    // データ自体は壊れていない。古いバックアップで戻したり .broken に退避したりしない
                    return false;
            }

            if (_recovery.TryRecoverFromBackup(key, text, out data, out var backupNeedsResave, log))
            {
                log.Failure = null;
                if (backupNeedsResave) Resave(key, data);
                return true;
            }

            _recovery.KeepBroken(key, text, log);
            return false;
        }

        /// <summary>マイグレーションしたデータを新しい形式で保存し直す。失敗は保存の失敗として通知する (読み込み自体は成功)</summary>
        private void Resave<T>(string key, T data)
        {
            var log = new SaveOperationLog(SaveOperation.Save, key);
            if (TrySerialize(key, data, log, out var payload)) WriteCore(key, payload, true, log);
            _reporter.Complete(log);
        }

        /// <inheritdoc />
        public T Load<T>(string key, T defaultValue = default) => TryLoad<T>(key, out var data) ? data : defaultValue;

        // ───────────────────────── 存在確認・削除 ─────────────────────────

        /// <inheritdoc />
        public bool Exists(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            _queue.WaitPending(key);
            var log = new SaveOperationLog(SaveOperation.Exists, key);
            var exists = false;
            try
            {
                exists = _storage.Exists(key);
            }
            catch (Exception e)
            {
                log.Fail(SaveErrors.Classify(e), $"\"{key}\" の存在確認に失敗しました: {e}", e);
            }
            _reporter.Complete(log);
            return exists;
        }

        /// <inheritdoc />
        public void Delete(string key)
        {
            var log = new SaveOperationLog(SaveOperation.Delete, key);
            if (ValidateKey(key, log))
            {
                _queue.WaitPending(key);
                _guard.Reset(key);
                TryDelete(key, log);
                if (!SaveRecovery.IsSideKey(key))
                {
                    TryDelete(key + BrokenSuffix, log);
                    TryDelete(key + NewerSuffix, log);
                }
            }
            _reporter.Complete(log);
        }

        private void TryDelete(string key, SaveOperationLog log)
        {
            try
            {
                _storage.Delete(key);
            }
            catch (Exception e)
            {
                log.Fail(SaveErrors.Classify(e), $"\"{key}\" の削除に失敗しました: {e}", e);
            }
        }

        // ───────────────────────── 非同期処理の順序 ─────────────────────────

        /// <inheritdoc />
        public bool HasPendingWrites => _queue.HasPending;

        /// <inheritdoc />
        public void Flush() => _queue.Flush();

        /// <inheritdoc />
        public Task FlushAsync() => _queue.FlushAsync();

        // ───────────────────────── 共通 ─────────────────────────

        private static bool ValidateKey(string key, SaveOperationLog log)
        {
            if (!string.IsNullOrEmpty(key)) return true;
            log.Fail(SaveFailureReason.InvalidKey, "キーが空です。");
            return false;
        }

        /// <summary>
        /// 設定に応じた保存先を作る。
        /// Auto の場合、WebGL ではファイルに書き込めないため PlayerPrefs を、それ以外ではファイルを使う。
        /// </summary>
        public static ISaveStorage CreateStorage(SaveSettings settings)
        {
            var kind = settings.Storage;
            if (kind == SaveStorageKind.Auto)
            {
                kind = Application.platform == RuntimePlatform.WebGLPlayer ? SaveStorageKind.PlayerPrefs : SaveStorageKind.File;
            }

            return kind == SaveStorageKind.PlayerPrefs
                ? new PlayerPrefsSaveStorage(settings.PlayerPrefsPrefix)
                : new FileSaveStorage(string.IsNullOrEmpty(settings.FolderName) ? SaveSettings.DefaultFolderName : settings.FolderName);
        }

        /// <summary>
        /// 設定に応じた圧縮・暗号化の一覧を作る (<see cref="SaveSettings.Compress"/> なら GZip)。
        /// 暗号化は鍵が必要なため設定では有効にできない。コードで <see cref="AesSaveTransform"/> を登録する。
        /// GZip は無効にしても読み込みには使われるため、以前に圧縮して保存したデータも読める。
        /// </summary>
        public static List<ISaveTransform> CreateTransforms(SaveSettings settings)
        {
            var transforms = new List<ISaveTransform>();
            if (settings != null && settings.Compress) transforms.Add(new GZipSaveTransform());
            return transforms;
        }
    }
}
