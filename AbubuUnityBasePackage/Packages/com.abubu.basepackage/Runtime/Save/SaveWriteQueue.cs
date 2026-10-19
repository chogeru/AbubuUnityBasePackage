using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Abubu.Save
{
    /// <summary>
    /// 非同期版の別スレッドの処理をキーごとに 1 列に並べる。同じキーの処理は予約した順に 1 つずつ、別のキーの処理は並行して実行する。
    /// メインスレッドからだけ使う (予約された処理そのものは別スレッドで動く)。
    /// </summary>
    internal sealed class SaveWriteQueue
    {
        /// <summary>キーごとの、最後に予約した処理。同じキーの処理はこれに繋いで順番に実行する</summary>
        private readonly Dictionary<string, Task> _tails = new();

        /// <summary>まだ終わっていない処理があるか</summary>
        public bool HasPending => _tails.Values.Any(t => !t.IsCompleted);

        /// <summary>
        /// 同じキーの前の処理が終わってから、別スレッドで work を実行するよう予約する。
        /// work は例外を投げない前提 (失敗はすべてログ・結果に記録する)。
        /// </summary>
        public Task Enqueue(string key, Action work)
        {
            var previous = _tails.TryGetValue(key, out var tail) ? tail : Task.CompletedTask;
            // 前の処理の結果に関係なく続ける
            var task = previous.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
            _tails[key] = task;
            return task;
        }

        /// <summary>
        /// このキーの処理が残っていれば終わるまで待つ (同期版の操作と重ならないように)。
        /// 予約した処理はメインスレッドを必要としないので、ここでブロックしてもデッドロックしない
        /// </summary>
        public void WaitPending(string key)
        {
            if (_tails.Count == 0 || !_tails.TryGetValue(key, out var task)) return;
            WaitQuietly(task);
            _tails.Remove(key);
        }

        /// <summary>すべての処理が終わるまで待つ (メインスレッドをブロックする)</summary>
        public void Flush()
        {
            foreach (var task in _tails.Values) WaitQuietly(task);
            _tails.Clear();
        }

        /// <summary>今予約されている処理がすべて終わったら完了する Task</summary>
        public Task FlushAsync() => Task.WhenAll(_tails.Values.ToArray());

        private static void WaitQuietly(Task task)
        {
            try
            {
                task.Wait();
            }
            catch (Exception)
            {
                // 予約した処理の失敗はログ・結果で報告済み
            }
        }
    }
}
