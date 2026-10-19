using System;
using System.Collections.Generic;

namespace Abubu.Save
{
    /// <summary>
    /// 読めないが壊れてもいないデータを、Save による上書きから守る (<see cref="ISaveService"/> の remarks 参照)。対象は 2 種類:
    /// <list type="bullet">
    /// <item>今のビルドより新しいバージョンで保存されたデータ (<see cref="SaveFailureReason.NewerVersion"/>)。元データを 1 度だけ "キー + .newer" に複製する</item>
    /// <item>読み込みに必要な変換 (暗号化の鍵など) が登録されていないデータ (<see cref="SaveFailureReason.TransformFailed"/>)。設定を直せば読めるので複製はしない</item>
    /// </list>
    /// どちらも、そのキーへの Save はこの実行中 ForceSave / Delete まで拒否する。
    /// </summary>
    /// <remarks>状態は内部で排他しているので、非同期版の別スレッドから呼んでもよい (保存先がスレッドセーフな場合)。</remarks>
    internal sealed class SaveOverwriteGuard
    {
        private readonly ISaveStorage _storage;
        private readonly SaveEnvelopeCodec _codec;

        /// <summary>_protectedKeys / _checkedKeys の排他</summary>
        private readonly object _lock = new();

        /// <summary>上書きを拒否しているキーと、その理由 (NewerVersion / TransformFailed)</summary>
        private readonly Dictionary<string, SaveFailureReason> _protectedKeys = new();

        /// <summary>保存済みデータの確認が済んだキー (確認は 1 キーにつき 1 回だけ行う)</summary>
        private readonly HashSet<string> _checkedKeys = new();

        public SaveOverwriteGuard(ISaveStorage storage, SaveEnvelopeCodec codec)
        {
            _storage = storage;
            _codec = codec;
        }

        /// <summary>
        /// この実行中にまだ読み書きしていないキーなら、保存済みデータの見出し (バージョンと必要な変換) を確認する。
        /// データ本体の変換の復元はしない。読めない・壊れている場合は何もしない (通常どおり上書きされ、FileSaveStorage なら .bak に残る)
        /// </summary>
        public void EnsureChecked(string key, int currentVersion, SaveOperationLog log)
        {
            lock (_lock)
            {
                if (!_checkedKeys.Add(key)) return;
            }
            try
            {
                if (!_storage.TryRead(key, out var text)) return;
                if (!_codec.TryPeekHeader(text, out var version, out var missingTransform)) return;
                if (version > currentVersion) ProtectNewer(key, text, version, currentVersion, log);
                else if (missingTransform != null) ProtectMissingTransform(key, missingTransform, log);
            }
            catch (Exception)
            {
                // 壊れたデータの判定は読み込み側の役目。ここでは上書きを止める理由にしない
            }
        }

        /// <summary>上書きを拒否しているキーか。拒否している場合は理由を返す</summary>
        public bool IsProtected(string key, out SaveFailureReason reason)
        {
            lock (_lock) return _protectedKeys.TryGetValue(key, out reason);
        }

        /// <summary>読み込んだので、以降の保存で確認をしない</summary>
        public void MarkChecked(string key)
        {
            lock (_lock) _checkedKeys.Add(key);
        }

        /// <summary>ForceSave で上書きした: 保護を解除し、確認済みにする</summary>
        public void OnForceWritten(string key)
        {
            lock (_lock)
            {
                _protectedKeys.Remove(key);
                _checkedKeys.Add(key);
            }
        }

        /// <summary>Delete した: 保護と確認済みの記録を消す</summary>
        public void Reset(string key)
        {
            lock (_lock)
            {
                _protectedKeys.Remove(key);
                _checkedKeys.Remove(key);
            }
        }

        /// <summary>新しいバージョンのデータを見つけたとき: キーを保護し、元データを 1 度だけ "キー + .newer" に複製する</summary>
        public void ProtectNewer(string key, string text, int savedVersion, int currentVersion, SaveOperationLog log)
        {
            if (!TryAdd(key, SaveFailureReason.NewerVersion)) return;

            log.Error($"\"{key}\" は今のビルドより新しいバージョン (v{savedVersion} > v{currentVersion}) で保存されているため読み込みません。" +
                      "データを守るため、このキーへの Save は拒否されます (ForceSave / Delete で解除)。");
            if (SaveRecovery.IsSideKey(key)) return;
            var newerKey = key + SaveService.NewerSuffix;
            try
            {
                if (!_storage.Exists(newerKey)) _storage.Write(newerKey, text);
            }
            catch (Exception e)
            {
                log.Error($"\"{newerKey}\" への退避に失敗しました: {e}");
            }
        }

        /// <summary>
        /// 読み込みに必要な変換が登録されていないデータを見つけたとき: キーを保護する。
        /// 読み込み時は読み込みの失敗としてエラーが出るので、ここでは保存時に見つけた場合だけログを出す (logError)
        /// </summary>
        public void ProtectMissingTransform(string key, string transformId, SaveOperationLog log, bool logError = true)
        {
            if (!TryAdd(key, SaveFailureReason.TransformFailed) || !logError) return;
            log.Error($"\"{key}\" は変換 \"{transformId}\" が登録されていないため読み込めないデータです (ISaveTransform を登録してください)。" +
                      "データを守るため、このキーへの Save は拒否されます (ForceSave / Delete で解除)。");
        }

        private bool TryAdd(string key, SaveFailureReason reason)
        {
            lock (_lock)
            {
                if (_protectedKeys.ContainsKey(key)) return false;
                _protectedKeys.Add(key, reason);
                return true;
            }
        }
    }
}
