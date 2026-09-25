using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace Abubu.Scene
{
    public interface ISceneService
    {
        ReadOnlyReactiveProperty<bool> IsLoading { get; }
        /// <summary>ロード画面を出すべき区間 (フェードアウト後〜フェードイン前) で true</summary>
        ReadOnlyReactiveProperty<bool> ShowLoadingScreen { get; }
        /// <summary>0〜1 のロード進捗</summary>
        ReadOnlyReactiveProperty<float> Progress { get; }
        /// <summary>シーン遷移 (フェードイン) 完了時にシーン名を通知</summary>
        Observable<string> OnLoaded { get; }
        /// <summary>
        /// 現在のシーンが破棄される直前 (フェードアウト・ロード完了後、アクティベート前) に通知。
        /// プールの回収など、シーン上のオブジェクトを退避する処理に使う
        /// </summary>
        Observable<Unit> OnBeforeSceneUnload { get; }
        string CurrentSceneName { get; }

        /// <summary>
        /// フェードアウト → ロード → フェードイン でシーンを切り替える。
        /// エディタでは "Assets/.../Foo.unity" のようなパスも指定でき、Build Settings 未登録のシーンも開ける
        /// </summary>
        UniTask LoadAsync(string sceneName, SceneLoadOptions options = null, CancellationToken ct = default);

        /// <summary>
        /// 遷移先にデータを渡してシーンを切り替える。
        /// 遷移先では [Inject] TPayload で受け取るか、TryGetPayload で取得できる。
        /// </summary>
        UniTask LoadWithPayloadAsync<TPayload>(string sceneName, TPayload payload, SceneLoadOptions options = null, CancellationToken ct = default);

        UniTask ReloadAsync(SceneLoadOptions options = null, CancellationToken ct = default);

        UniTask LoadAdditiveAsync(string sceneName, CancellationToken ct = default);
        UniTask UnloadAsync(string sceneName, CancellationToken ct = default);

        /// <summary>直近の LoadAsync で渡されたデータを取得する</summary>
        bool TryGetPayload<T>(out T payload);
    }

    public interface ISceneTransition
    {
        UniTask FadeOutAsync(float duration, UnityEngine.Color color, CancellationToken ct);
        UniTask FadeInAsync(float duration, CancellationToken ct);
    }
}
