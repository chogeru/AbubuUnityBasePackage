using System;

namespace Abubu.Save
{
    /// <summary>復元の結果</summary>
    internal enum SaveDecodeResult
    {
        /// <summary>復元できた</summary>
        Ok,
        /// <summary>壊れている・変換できないなどで復元できなかった (バックアップからの復旧・.broken への退避の対象)</summary>
        Failed,
        /// <summary>
        /// 必要な変換 (暗号化など) が登録されていないため復元できなかった。データは壊れていないので、
        /// 復旧・退避はせずそのまま残す
        /// </summary>
        TransformMissing,
        /// <summary>今のビルドより新しいバージョンで保存されていた</summary>
        Newer,
    }

    /// <summary>別スレッドで Envelope の解析と変換の復元まで済ませた結果</summary>
    internal sealed class SaveUnwrapped
    {
        public SaveEnvelope Envelope;
        public string Data;
    }

    /// <summary>
    /// 保存されていた文字列からデータを復元する: Envelope の解析 → 新しいバージョンの確認 → 変換の復元 → マイグレーション → デシリアライズ。
    /// 失敗はエラーログと log の失敗として記録し、例外は投げない。
    /// </summary>
    internal sealed class SaveDecoder
    {
        private readonly SaveEnvelopeCodec _codec;
        private readonly SaveMigrator _migrator;
        private readonly ISaveSerializer _serializer;
        private readonly SaveOverwriteGuard _guard;

        public SaveDecoder(SaveEnvelopeCodec codec, SaveMigrator migrator, ISaveSerializer serializer, SaveOverwriteGuard guard)
        {
            _codec = codec;
            _migrator = migrator;
            _serializer = serializer;
            _guard = guard;
        }

        /// <summary>
        /// 別スレッドでできるところ (Envelope の解析と変換の復元) だけ先に済ませる。
        /// 壊れている・新しいバージョンなどで済ませられなかったら null (メインスレッドの <see cref="Decode{T}"/> で改めて扱う)
        /// </summary>
        public SaveUnwrapped TryUnwrap(string text, int currentVersion)
        {
            try
            {
                var envelope = SaveEnvelopeCodec.Parse(text);
                if (envelope.version > currentVersion) return null;
                return new SaveUnwrapped { Envelope = envelope, Data = _codec.Unwrap(envelope) };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>保存されていた文字列からデータを復元する (メインスレッド)</summary>
        /// <param name="unwrapped">別スレッドで解析・変換の復元を済ませていればその結果 (無ければ null)</param>
        /// <param name="needsResave">旧バージョンから変換した (新しい形式で保存し直すべき) なら true</param>
        public SaveDecodeResult Decode<T>(string key, string text, SaveUnwrapped unwrapped, out T data, out bool needsResave, SaveOperationLog log)
        {
            data = default;
            needsResave = false;
            try
            {
                var envelope = unwrapped?.Envelope ?? SaveEnvelopeCodec.Parse(text);

                var current = SaveDataVersion<T>.Value;
                if (envelope.version > current)
                {
                    _guard.ProtectNewer(key, text, envelope.version, current, log);
                    log.Fail(SaveFailureReason.NewerVersion, $"\"{key}\" は今のビルドより新しいバージョン (v{envelope.version} > v{current}) で保存されています。", log: false);
                    return SaveDecodeResult.Newer;
                }

                if (!string.IsNullOrEmpty(envelope.type) && envelope.type != typeof(T).FullName)
                {
                    log.Warning($"\"{key}\" は {envelope.type} として保存されていますが {typeof(T).FullName} で読み込もうとしています。");
                }
                var serialized = unwrapped != null ? unwrapped.Data : _codec.Unwrap(envelope);

                if (!_migrator.TryMigrate<T>(key, serialized, envelope.version, log, out serialized)) return SaveDecodeResult.Failed;

                data = _serializer.Deserialize<T>(serialized);
                needsResave = envelope.version < current;
                return SaveDecodeResult.Ok;
            }
            catch (SaveTransformException e)
            {
                data = default;
                log.Fail(SaveFailureReason.TransformFailed, $"\"{key}\" の読み込みに失敗しました: {e}", e);
                if (!e.IsMissingTransform) return SaveDecodeResult.Failed;
                // データは壊れていない。設定を直す前に Save で上書きされないよう保護する (エラーは上で出している)
                _guard.ProtectMissingTransform(key, null, log, logError: false);
                return SaveDecodeResult.TransformMissing;
            }
            catch (Exception e)
            {
                data = default;
                log.Fail(SaveFailureReason.Corrupted, $"\"{key}\" の読み込みに失敗しました: {e}", e);
                return SaveDecodeResult.Failed;
            }
        }
    }
}
