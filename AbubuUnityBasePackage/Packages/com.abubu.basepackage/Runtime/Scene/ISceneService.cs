using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace Abubu.SceneFlow
{
    /// <summary>
    /// シーン遷移の窓口。
    /// <code>
    /// [Inject] private ISceneService _scene;
    /// await _scene.LoadAsync("Title");                                         // フェード付きで遷移 (受け付けなければ false)
    /// await _scene.LoadWithPayloadAsync("Game", new StageInfo(3));            // 遷移先で [Inject] StageInfo で受け取れる
    /// await _scene.LoadAdditiveAsync("Hud");                                  // 追加読み込み (フェードなし)
    /// _scene.OnLoaded.Subscribe(name =&gt; Debug.Log(name)).AddTo(this);
    /// </code>
    /// </summary>
    /// <remarks>
    /// 遷移の順序 (LoadAsync):
    /// <list type="number">
    /// <item>フェードアウト (<see cref="ISceneTransition.FadeOutAsync"/>)</item>
    /// <item><see cref="ISceneTransitionHook.OnBeforeUnloadAsync"/> (登録順に 1 つずつ await)</item>
    /// <item>遷移先シーンの読み込み (アクティベート前まで)。ShowLoadingScreen が true になる</item>
    /// <item><see cref="OnBeforeSceneUnload"/> の通知 (同期)</item>
    /// <item>アクティベート (旧シーンの破棄・遷移先の Awake / SceneContext の Install)</item>
    /// <item>1 フレーム待って遷移先の Start を走らせる</item>
    /// <item><see cref="ISceneTransitionHook.OnAfterLoadAsync"/> (登録順に 1 つずつ await)</item>
    /// <item>フェードイン → <see cref="OnLoaded"/> の通知</item>
    /// </list>
    /// キャンセル: ct が効くのは 2 まで。そこでキャンセルされた場合は元のシーンに留まってフェードインし、
    /// OperationCanceledException を投げる。3 以降は読み込みを途中で止められない (Unity の制約) ため ct は無視される。
    /// SceneService が 2 までに破棄された場合も同様に OperationCanceledException になる (フェードインはしない)。
    /// 3 以降に破棄された場合は読み込みだけ最後まで進め、IsLoading などの通知は行わない。
    /// <para>
    /// 拒否: 遷移中の LoadAsync / LoadAdditiveAsync / UnloadAsync の呼び出しや、存在しないシーンの指定は
    /// 例外にせずログを出して false を返す。
    /// </para>
    /// 起動直後に遷移する場合は、先に IBootService.WaitReadyAsync を await しておくこと (起動処理より先に遷移しないため)。
    /// </remarks>
    public interface ISceneService
    {
        /// <summary>LoadAsync / ReloadAsync による遷移中は true (フェードアウト開始〜フェードイン完了)</summary>
        ReadOnlyReactiveProperty<bool> IsLoading { get; }
        /// <summary>ロード画面を出すべき区間 (フェードアウト後〜フェードイン前) で true</summary>
        ReadOnlyReactiveProperty<bool> ShowLoadingScreen { get; }
        /// <summary>0〜1 のロード進捗</summary>
        ReadOnlyReactiveProperty<float> Progress { get; }
        /// <summary>シーン遷移 (フェードイン) 完了時にシーン名を通知</summary>
        Observable<string> OnLoaded { get; }
        /// <summary>
        /// 現在のシーンが破棄される直前 (フェードアウト・ロード完了後、アクティベート前) に通知。
        /// プールの回収など、シーン上のオブジェクトを退避する処理に使う。
        /// 待ち合わせが必要な処理 (保存など) は <see cref="ISceneTransitionHook"/> を使う
        /// </summary>
        Observable<Unit> OnBeforeSceneUnload { get; }
        string CurrentSceneName { get; }

        /// <summary>
        /// フェードアウト → ロード → フェードイン でシーンを切り替える。
        /// エディタでは "Assets/.../Foo.unity" のようなパスも指定でき、Build Settings 未登録のシーンも開ける
        /// </summary>
        /// <returns>遷移したら true。遷移中だった・シーンが見つからないなどで受け付けなかった場合は false</returns>
        /// <exception cref="System.OperationCanceledException">フェードアウト完了までに ct がキャンセルされた (元のシーンに留まる)</exception>
        UniTask<bool> LoadAsync(string sceneName, SceneLoadOptions options = null, CancellationToken ct = default);

        /// <summary>
        /// 遷移先にデータを渡してシーンを切り替える。
        /// 遷移先では [Inject] TPayload で受け取るか、TryGetPayload で取得できる。
        /// </summary>
        /// <returns><see cref="LoadAsync"/> と同じ</returns>
        UniTask<bool> LoadWithPayloadAsync<TPayload>(string sceneName, TPayload payload, SceneLoadOptions options = null, CancellationToken ct = default);

        /// <summary>現在のシーンを読み込み直す。直前の遷移で渡したデータは引き継がれる</summary>
        /// <returns><see cref="LoadAsync"/> と同じ</returns>
        UniTask<bool> ReloadAsync(SceneLoadOptions options = null, CancellationToken ct = default);

        /// <summary>
        /// シーンを追加で読み込む (フェードなし)。すでに読み込み済みなら何もせず true。
        /// </summary>
        /// <returns>
        /// 呼び出し後にシーンが読み込まれていれば true。LoadAsync による遷移中・同じシーンの追加/破棄の処理中・
        /// シーンが見つからない場合は false。
        /// ct でキャンセルすると待つのをやめて OperationCanceledException を投げるが、読み込み自体は裏で続く (Unity の制約)。
        /// </returns>
        UniTask<bool> LoadAdditiveAsync(string sceneName, CancellationToken ct = default);

        /// <summary>追加で読み込んだシーンを破棄する。読み込まれていなければ何もせず true。</summary>
        /// <returns>
        /// 呼び出し後にシーンが読み込まれていなければ true。LoadAsync による遷移中・同じシーンの処理中・
        /// 破棄できない (最後の 1 シーンなど) 場合は false。ct の扱いは <see cref="LoadAdditiveAsync"/> と同じ。
        /// </returns>
        UniTask<bool> UnloadAsync(string sceneName, CancellationToken ct = default);

        /// <summary>直近の LoadAsync で渡されたデータを取得する</summary>
        bool TryGetPayload<T>(out T payload);
    }

    /// <summary>
    /// 遷移演出。既定は画面全体を単色でフェードする <see cref="FadeTransition"/>。
    /// <code>
    /// public sealed class XrFade : ISceneTransition
    /// {
    ///     public UniTask FadeOutAsync(float duration, Color color, CancellationToken ct) =&gt; _xrFader.FadeToAsync(1f, duration, color, ct);
    ///     public UniTask FadeInAsync(float duration, CancellationToken ct) =&gt; _xrFader.FadeToAsync(0f, duration, default, ct);
    /// }
    /// </code>
    /// VR や独自演出にしたい場合はこれを実装し、AbubuInstaller より前の Installer で
    /// Container.Bind&lt;ISceneTransition&gt;().To&lt;MyTransition&gt;().AsSingle() とバインドする。
    /// </summary>
    public interface ISceneTransition
    {
        /// <summary>画面を覆う。完了したら (覆い終えたら) 戻る</summary>
        UniTask FadeOutAsync(float duration, UnityEngine.Color color, CancellationToken ct);
        /// <summary>覆いを外す。完了したら戻る</summary>
        UniTask FadeInAsync(float duration, CancellationToken ct);
    }

    /// <summary>
    /// シーン遷移の前後に、待ち合わせが必要な処理を差し込む (保存・通信の完了待ち・リソースの事前読み込みなど)。
    /// <code>
    /// public sealed class AutoSaveHook : ISceneTransitionHook
    /// {
    ///     public UniTask OnBeforeUnloadAsync(string from, string to, CancellationToken ct) =&gt; _save.SaveAsync(ct);
    ///     public UniTask OnAfterLoadAsync(string from, string to, CancellationToken ct) =&gt; UniTask.CompletedTask;
    /// }
    /// </code>
    /// Installer で Container.Bind&lt;ISceneTransitionHook&gt;().To&lt;MyHook&gt;().AsSingle() と登録すると SceneService が使う
    /// (登録が無くても動く)。複数登録した場合は登録順に 1 つずつ await される。
    /// </summary>
    /// <remarks>
    /// 例外を投げても遷移は止まらない (エラーログを出して次へ進む)。ct がキャンセルされていない OperationCanceledException
    /// (フック内部のタイムアウトなど) も失敗として同じ扱いになる。
    /// ただし OnBeforeUnloadAsync で LoadAsync の ct がキャンセルされた場合だけは遷移を中止する (残りのフックは呼ばない)。
    /// OnLoaded / OnBeforeSceneUnload の購読側の例外も遷移を止めない (R3 の未処理例外ハンドラに渡される)。
    /// </remarks>
    public interface ISceneTransitionHook
    {
        /// <summary>
        /// フェードアウト完了後、遷移先の読み込みを始める前に呼ばれる。旧シーンのオブジェクトはまだ生きている。
        /// </summary>
        /// <param name="from">遷移元のシーン名</param>
        /// <param name="to">遷移先のシーン名 (LoadAsync に渡された文字列)</param>
        /// <param name="ct">LoadAsync に渡された ct (SceneService の破棄時にもキャンセルされる)</param>
        UniTask OnBeforeUnloadAsync(string from, string to, CancellationToken ct);

        /// <summary>
        /// 遷移先のアクティベートと Start の後、フェードインの前に呼ばれる (画面はまだ覆われている)。
        /// </summary>
        /// <param name="from">遷移元のシーン名</param>
        /// <param name="to">遷移先のシーン名 (LoadAsync に渡された文字列)</param>
        /// <param name="ct">SceneService の破棄時にキャンセルされる</param>
        UniTask OnAfterLoadAsync(string from, string to, CancellationToken ct);
    }
}
