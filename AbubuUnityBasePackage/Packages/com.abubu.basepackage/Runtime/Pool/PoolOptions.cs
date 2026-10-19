using System;
using UnityEngine;

namespace Abubu.Pool
{
    /// <summary>プレハブ 1 種類分のプール設定。<see cref="IPoolService.Configure"/> で渡す</summary>
    [Serializable]
    public struct PoolOptions
    {
        [Tooltip("待機中の在庫の上限。超えた分は返却時に破棄する。0 なら上限なし")]
        [Min(0)] public int MaxInactive;

        public PoolOptions(int maxInactive) => MaxInactive = Mathf.Max(0, maxInactive);

        /// <summary>上限なし</summary>
        public static PoolOptions Default => default;
    }
}
