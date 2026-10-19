using System;

namespace Abubu.Save
{
    /// <summary>
    /// 読み込めなかったデータの後始末: バックアップ (<see cref="ISaveBackupStorage"/>、FileSaveStorage なら .bak) からの復旧と、
    /// 復旧できなかったデータの "キー + .broken" への退避。メインスレッドから使う。
    /// </summary>
    /// <remarks>"キー + .newer" への退避 (新しいバージョンのデータ) は <see cref="SaveOverwriteGuard"/> が行う。</remarks>
    internal sealed class SaveRecovery
    {
        private readonly ISaveStorage _storage;
        private readonly SaveDecoder _decoder;

        public SaveRecovery(ISaveStorage storage, SaveDecoder decoder)
        {
            _storage = storage;
            _decoder = decoder;
        }

        /// <summary>退避用のキー (.broken / .newer) か。退避の退避を作らないために使う</summary>
        public static bool IsSideKey(string key) =>
            key.EndsWith(SaveService.BrokenSuffix, StringComparison.Ordinal) || key.EndsWith(SaveService.NewerSuffix, StringComparison.Ordinal);

        /// <summary>
        /// 本体が読めなかったとき、バックアップから復元して本体を戻す。
        /// 壊れた本体は "キー + .broken" に退避し、バックアップは壊れた本体で上書きせずに残す
        /// </summary>
        /// <param name="needsResave">復元したデータが旧バージョンだった (呼び出し側で新しい形式で保存し直す)</param>
        public bool TryRecoverFromBackup<T>(string key, string brokenText, out T data, out bool needsResave, SaveOperationLog log)
        {
            data = default;
            needsResave = false;
            if (_storage is not ISaveBackupStorage backup) return false;

            string backupText;
            try
            {
                if (!backup.TryReadBackup(key, out backupText) || backupText == brokenText) return false;
            }
            catch (Exception e)
            {
                log.Error($"\"{key}\" のバックアップの読み込みに失敗しました: {e}");
                return false;
            }

            // バックアップも読めなかった場合のログ・失敗は本体のもの (log) だけにする
            if (_decoder.Decode(key, backupText, null, out data, out needsResave, new SaveOperationLog(SaveOperation.Load, key)) != SaveDecodeResult.Ok)
            {
                data = default;
                needsResave = false;
                return false;
            }

            log.Warning($"\"{key}\" が壊れていたため、1 つ前のデータから復旧しました。");
            KeepBroken(key, brokenText, log);
            try
            {
                backup.RestoreFromBackup(key);
            }
            catch (Exception e)
            {
                log.Error($"\"{key}\" をバックアップから戻せませんでした (データは読み込めています): {e}");
            }
            return true;
        }

        /// <summary>読み込めなかったデータを、次の Save で上書きされて失われないよう "キー + .broken" に退避する</summary>
        public void KeepBroken(string key, string text, SaveOperationLog log)
        {
            if (string.IsNullOrEmpty(text) || IsSideKey(key)) return;
            var brokenKey = key + SaveService.BrokenSuffix;
            try
            {
                _storage.Write(brokenKey, text);
            }
            catch (Exception e)
            {
                log.Error($"\"{brokenKey}\" の書き込みに失敗しました: {e}");
            }
        }
    }
}
