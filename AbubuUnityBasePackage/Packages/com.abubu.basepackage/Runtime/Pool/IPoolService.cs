using UnityEngine;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Abubu.Pool
{
    /// <summary>
    /// プレハブ単位で GameObject をプールする汎用 PoolManager (弾・敵・エフェクト・ダメージ表記など)。
    /// <code>
    /// var bullet = _pool.Rent(bulletPrefab, muzzle.position, muzzle.rotation);
    /// _pool.Return(bullet);            // 手動で返す
    /// _pool.ReturnAfter(bullet, 2f);   // 2 秒後に返す
    /// </code>
    /// 貸し出し / 返却時に IPoolCallbackReceiver (uPools) の OnRent / OnReturn が子を含めて呼ばれるので、状態リセットはそこに書く。
    /// </summary>
    /// <remarks>
    /// インスタンスの置き場所:
    /// ・待機中 … プール専用のルート (DontDestroyOnLoad、非アクティブ) の下
    /// ・貸し出し中 … parent を指定すればその子、省略すればアクティブシーンのルート
    /// シーン遷移で親ごと破棄される前に回収したい場合は <see cref="ReturnAll(UnityScene)"/> を遷移前に呼ぶ (Installer 側で結線)。
    /// 回収されずに破棄されたインスタンスは自動でプールの管理から外れる (例外にはならない)。
    /// </remarks>
    public interface IPoolService
    {
        /// <summary>
        /// プレハブのインスタンスをプールから借りる。空きが無ければ新しく生成する。
        /// 戻り値は常にアクティブで、OnRent 通知済み。
        /// </summary>
        /// <param name="prefab">元になるプレハブ。このプレハブごとに別々のプールが作られる</param>
        /// <param name="parent">親 Transform。null ならアクティブシーンのルートに置かれる</param>
        GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null);

        /// <summary>コンポーネント型でプレハブを指定して借りる版。戻り値も同じ型で受け取れる。</summary>
        T Rent<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component;

        /// <summary>
        /// プールから借りたものは返却、プール管理外のものは Destroy する。
        /// 返却済みのもの (二重返却) は何もしない。
        /// </summary>
        void Return(GameObject instance);

        /// <summary>
        /// 指定秒数後に返却する。プール管理外のオブジェクトなら、同じ秒数後に Destroy する。
        /// 待っている間に手動で Return した場合、タイマーは取り消される。
        /// </summary>
        /// <param name="ignoreTimeScale">true なら Time.timeScale (ポーズ) の影響を受けない</param>
        void ReturnAfter(GameObject instance, float seconds, bool ignoreTimeScale = false);

        /// <summary>
        /// 待機中の在庫が <paramref name="count"/> 個になるまで、先にインスタンスを作っておく。
        /// ゲーム中に初めて Rent したときの Instantiate によるカクつきを防ぐ。Prewarm では OnRent / OnReturn は呼ばれない。
        /// </summary>
        void Prewarm(GameObject prefab, int count);

        /// <summary>プレハブごとのプール設定 (在庫の上限など) を変更する。Rent より前でも後でも呼べる</summary>
        void Configure(GameObject prefab, PoolOptions options);

        /// <summary>貸し出し中のものをすべて返却する</summary>
        void ReturnAll();

        /// <summary>指定シーンに置かれている貸し出し中のものだけを返却する (DontDestroyOnLoad 側のものは残る)</summary>
        void ReturnAll(UnityScene scene);

        /// <summary>指定プレハブの待機中の在庫を破棄してメモリを空ける (貸し出し中のものはそのまま)</summary>
        void Clear(GameObject prefab);

        /// <summary>指定プレハブの貸し出し中 / 待機中の数 (デバッグ表示・テスト用)</summary>
        PoolStats GetStats(GameObject prefab);
    }

    /// <summary>プール 1 つ分の利用状況</summary>
    public readonly struct PoolStats
    {
        /// <summary>貸し出し中の数</summary>
        public int Active { get; }
        /// <summary>待機中 (すぐ貸し出せる) の数</summary>
        public int Inactive { get; }

        public PoolStats(int active, int inactive)
        {
            Active = active;
            Inactive = inactive;
        }

        public override string ToString() => $"Active={Active}, Inactive={Inactive}";
    }
}
