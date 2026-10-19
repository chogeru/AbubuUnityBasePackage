using System;
using UnityEngine;

namespace Abubu.Save
{
    /// <summary>
    /// 操作の終わりに、ためたログを出して失敗を <see cref="Failed"/> で通知する (メインスレッド)。
    /// <see cref="SaveService.SaveFailed"/> の実体。
    /// </summary>
    internal sealed class SaveFailureReporter
    {
        /// <summary>失敗したときに呼ばれる。ハンドラの例外は捕まえてログに出す</summary>
        public event Action<SaveFailure> Failed;

        /// <summary>ためたログを出し、失敗していれば <see cref="Failed"/> を呼ぶ (メインスレッド)。失敗を返す (成功なら null)</summary>
        public SaveFailure Complete(SaveOperationLog log)
        {
            log.FlushLogs();
            var failure = log.Failure;
            if (failure == null) return null;
            try
            {
                Failed?.Invoke(failure);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return failure;
        }
    }
}
