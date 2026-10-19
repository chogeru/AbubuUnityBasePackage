using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;

namespace Abubu.Save
{
    /// <summary>
    /// セーブの非同期 API (System.Threading.Tasks.Task) を UniTask で受け取る拡張メソッド。
    /// Abubu.Save 本体は依存なしで使えるよう Task を返すため、UniTask で書いているプロジェクトはこちらを使う。
    /// <code>
    /// var result = await _save.SaveUniTaskAsync("world", world);
    /// var world = (await _save.LoadUniTaskAsync&lt;World&gt;("world")).GetValueOrDefault(new World());
    /// var result2 = await _slots.Active.SaveAsync("progress", progress).ToUniTask();
    /// </code>
    /// </summary>
    /// <remarks>
    /// 完了済みの Task (PlayerPrefs など、同期で処理される保存先の場合) はそのまま完了済みの UniTask にする。
    /// 未完了の場合は呼び出し元の SynchronizationContext (メインスレッド) で再開する。
    /// </remarks>
    public static class SaveUniTaskExtensions
    {
        /// <summary>保存の結果の Task を UniTask にする</summary>
        public static UniTask<SaveResult> ToUniTask(this Task<SaveResult> task) =>
            task.Status == TaskStatus.RanToCompletion ? UniTask.FromResult(task.Result) : task.AsUniTask();

        /// <summary>読み込みの結果の Task を UniTask にする</summary>
        public static UniTask<SaveLoadResult<T>> ToUniTask<T>(this Task<SaveLoadResult<T>> task) =>
            task.Status == TaskStatus.RanToCompletion ? UniTask.FromResult(task.Result) : task.AsUniTask();

        /// <summary><see cref="IAsyncSaveService.SaveAsync{T}"/> の UniTask 版 (非同期非対応の実装では同期版を呼ぶ)</summary>
        public static UniTask<SaveResult> SaveUniTaskAsync<T>(this ISaveService save, string key, T data) =>
            save.SaveAsync(key, data).ToUniTask();

        /// <summary><see cref="IAsyncSaveService.ForceSaveAsync{T}"/> の UniTask 版</summary>
        public static UniTask<SaveResult> ForceSaveUniTaskAsync<T>(this ISaveService save, string key, T data) =>
            save.ForceSaveAsync(key, data).ToUniTask();

        /// <summary><see cref="IAsyncSaveService.LoadAsync{T}"/> の UniTask 版</summary>
        public static UniTask<SaveLoadResult<T>> LoadUniTaskAsync<T>(this ISaveService save, string key) =>
            save.LoadAsync<T>(key).ToUniTask();

        /// <summary><see cref="IAsyncSaveService.FlushAsync"/> の UniTask 版。非同期非対応の実装では完了済み</summary>
        public static UniTask FlushUniTaskAsync(this ISaveService save)
        {
            if (save is not IAsyncSaveService asyncSave) return UniTask.CompletedTask;
            var task = asyncSave.FlushAsync();
            return task.Status == TaskStatus.RanToCompletion ? UniTask.CompletedTask : task.AsUniTask();
        }
    }

    /// <summary>
    /// セーブの C# イベントを R3 の Observable で受け取る拡張メソッド。
    /// <code>
    /// _slots.ActiveSlotChangedAsObservable().Subscribe(id =&gt; RefreshHud(id)).AddTo(this);
    /// _notifier.SaveFailedAsObservable().Where(f =&gt; f.Reason == SaveFailureReason.DiskFull).Subscribe(_ =&gt; ShowDiskFullDialog()).AddTo(this);
    /// </code>
    /// </summary>
    /// <remarks>購読している間だけイベントにハンドラを登録する。購読を Dispose (AddTo など) すれば解除される。</remarks>
    public static class SaveObservableExtensions
    {
        /// <summary><see cref="ISaveSlotService.ActiveSlotChanged"/> (新しいスロット ID。未選択になったら null)</summary>
        /// <param name="cancellationToken">キャンセルされたら完了する</param>
        public static Observable<string> ActiveSlotChangedAsObservable(this ISaveSlotService slots, CancellationToken cancellationToken = default) =>
            Observable.FromEvent<string>(h => slots.ActiveSlotChanged += h, h => slots.ActiveSlotChanged -= h, cancellationToken);

        /// <summary><see cref="ISaveFailureNotifier.SaveFailed"/> (メインスレッドで流れる)</summary>
        /// <param name="cancellationToken">キャンセルされたら完了する</param>
        public static Observable<SaveFailure> SaveFailedAsObservable(this ISaveFailureNotifier notifier, CancellationToken cancellationToken = default) =>
            Observable.FromEvent<SaveFailure>(h => notifier.SaveFailed += h, h => notifier.SaveFailed -= h, cancellationToken);
    }
}
