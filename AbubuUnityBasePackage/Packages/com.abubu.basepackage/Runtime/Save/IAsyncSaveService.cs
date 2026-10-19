using System.Threading.Tasks;

namespace Abubu.Save
{
    /// <summary>
    /// 大きなデータをフレーム落ちさせずに保存/読み込みする非同期 API。<see cref="SaveService"/> が実装している。
    /// <code>
    /// var result = await _save.SaveAsync("world", world);   // シリアライズはメインスレッド、書き込みは別スレッド
    /// var world = (await _save.LoadAsync&lt;World&gt;("world")).GetValueOrDefault(new World());
    /// </code>
    /// </summary>
    /// <remarks>
    /// メインスレッドから呼び、メインスレッドで await すること (失敗のログ・<see cref="ISaveFailureNotifier.SaveFailed"/> はメインスレッドで出る)。
    /// <para>
    /// 保存先が <see cref="IThreadSafeSaveStorage"/> (<see cref="FileSaveStorage"/> など) の場合だけ、書き込み・読み込みと
    /// 圧縮/暗号化 (<see cref="ISaveTransform"/>) を別スレッドで行う。そうでない保存先 (PlayerPrefs など) では
    /// 同期版と同じくメインスレッドで処理し、完了済みの Task を返す。
    /// </para>
    /// <para>
    /// 同じキーへの操作は呼んだ順に 1 つずつ実行される (後から呼んだ Save が先に呼んだ Save に上書きされることはない)。
    /// 書き込み途中のキーに同期版 (Save / TryLoad / Exists / Delete) を呼ぶと、そのキーの書き込みが終わるまで待ってから処理する。
    /// アプリ終了前など、書き込みを確実に終わらせたい時は <see cref="Flush"/> を呼ぶ。
    /// </para>
    /// </remarks>
    public interface IAsyncSaveService
    {
        /// <summary><see cref="ISaveService.Save{T}"/> の非同期版。例外は投げず、結果で失敗を返す</summary>
        Task<SaveResult> SaveAsync<T>(string key, T data);

        /// <summary><see cref="ISaveService.ForceSave{T}"/> の非同期版</summary>
        Task<SaveResult> ForceSaveAsync<T>(string key, T data);

        /// <summary>
        /// <see cref="ISaveService.TryLoad{T}"/> の非同期版。読み込みと圧縮/暗号化の復元は別スレッド、
        /// マイグレーションとデシリアライズはメインスレッドで行う。
        /// </summary>
        Task<SaveLoadResult<T>> LoadAsync<T>(string key);

        /// <summary>まだ終わっていない非同期の書き込みがあるか</summary>
        bool HasPendingWrites { get; }

        /// <summary>まだ終わっていない非同期の書き込みをすべて待つ (メインスレッドをブロックする)。終了処理の前などに呼ぶ</summary>
        void Flush();

        /// <summary>まだ終わっていない非同期の書き込みがすべて終わったら完了する Task</summary>
        Task FlushAsync();
    }

    /// <summary>
    /// どのスレッドから呼んでもよい保存先であることを示す目印。
    /// 実装していると <see cref="IAsyncSaveService"/> が書き込み・読み込みを別スレッドで行う。
    /// <code>
    /// public sealed class MyCloudStorage : IThreadSafeSaveStorage { ... } // Unity API を使わない保存先だけに付ける
    /// </code>
    /// </summary>
    /// <remarks>
    /// Unity の API (PlayerPrefs など) を使う保存先には付けないこと。
    /// 同じキーへの呼び出しは SaveService が順番に 1 つずつ行うが、別のキーへの呼び出しは同時に来ることがある。
    /// </remarks>
    public interface IThreadSafeSaveStorage : ISaveStorage
    {
    }

    /// <summary>
    /// どんな <see cref="ISaveService"/> にも非同期 API を付ける拡張メソッド。
    /// <see cref="IAsyncSaveService"/> を実装していればそちらを、していなければ同期版を呼んで完了済みの Task を返す。
    /// <code>
    /// ISaveService save = ...;
    /// if (!await save.SaveAsync("world", world)) ShowError();
    /// </code>
    /// </summary>
    public static class SaveServiceAsyncExtensions
    {
        /// <summary><see cref="IAsyncSaveService.SaveAsync{T}"/>。非対応の実装では同期版を呼ぶ (失敗理由は Unknown)</summary>
        public static Task<SaveResult> SaveAsync<T>(this ISaveService save, string key, T data)
        {
            if (save is IAsyncSaveService asyncSave) return asyncSave.SaveAsync(key, data);
            return Task.FromResult(ToResult(save.Save(key, data), key));
        }

        /// <summary><see cref="IAsyncSaveService.ForceSaveAsync{T}"/>。非対応の実装では同期版を呼ぶ</summary>
        public static Task<SaveResult> ForceSaveAsync<T>(this ISaveService save, string key, T data)
        {
            if (save is IAsyncSaveService asyncSave) return asyncSave.ForceSaveAsync(key, data);
            return Task.FromResult(ToResult(save.ForceSave(key, data), key));
        }

        /// <summary><see cref="IAsyncSaveService.LoadAsync{T}"/>。非対応の実装では同期版を呼ぶ (データが無い場合と失敗は区別できない)</summary>
        public static Task<SaveLoadResult<T>> LoadAsync<T>(this ISaveService save, string key)
        {
            if (save is IAsyncSaveService asyncSave) return asyncSave.LoadAsync<T>(key);
            return Task.FromResult(save.TryLoad<T>(key, out var data) ? SaveLoadResult<T>.Loaded(data) : SaveLoadResult<T>.Missing());
        }

        /// <summary>非同期の書き込みをすべて待つ。非対応の実装では何もしない</summary>
        public static void Flush(this ISaveService save)
        {
            if (save is IAsyncSaveService asyncSave) asyncSave.Flush();
        }

        private static SaveResult ToResult(bool success, string key) => success
            ? SaveResult.Ok
            : new SaveResult(new SaveFailure(key, SaveOperation.Save, SaveFailureReason.Unknown, "保存に失敗しました (詳細はエラーログ)"));
    }
}
