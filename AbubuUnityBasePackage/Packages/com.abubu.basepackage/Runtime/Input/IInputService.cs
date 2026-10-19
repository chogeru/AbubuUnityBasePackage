using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Abubu.Inputs
{
    /// <summary>最後に操作されたデバイスの種類 (ボタン表示の切り替えなどに使う)</summary>
    public enum InputDeviceKind
    {
        KeyboardMouse,
        Gamepad,
        Touch,
        /// <summary>XR コントローラー / HMD など (TrackedDevice)</summary>
        XR,
        /// <summary>フライトスティック・ハンドルなどの Joystick</summary>
        Joystick,
        /// <summary>上記以外 (独自デバイスなど)。使用中のデバイスはこれには切り替わらない</summary>
        Other,
    }

    /// <summary>
    /// 入力の窓口。ジャンルを問わず使えるよう、アクションは "Map/Action" 形式のパスで指定する。
    /// <code>
    /// _input.Performed("Player/Jump").Subscribe(_ => Jump()).AddTo(this);
    /// _input.ReadValue&lt;Vector2&gt;("Player/Move").Subscribe(v => velocity = v * speed).AddTo(this);
    /// var action = _input.GetAction("Vehicle/Throttle");      // InputAction を直接使ってもよい
    /// </code>
    /// 見つからないアクションを指定しても警告は出さず、何も流れない Observable を返す
    /// (AbubuInputSettings.StrictActionLookup をオンにすると警告を出す)。
    /// ActionMap の有効/無効はこのサービスからは原則として変更しない。
    /// 変更するのは AbubuInputSettings.EnabledMaps / PauseDisabledMaps に書いたマップと、
    /// <see cref="SetMapEnabled"/> で明示的に指定したマップだけ (Dispose 時に元の状態へ戻す)。
    /// </summary>
    public interface IInputService
    {
        /// <summary>使用中の Input Actions。見つからない / モジュール無効時は null</summary>
        InputActionAsset Actions { get; }

        /// <summary>"Map/Action" 形式 (または Action 名・ID) でアクションを取得する。見つからなければ null</summary>
        InputAction GetAction(string actionPath);

        /// <summary>
        /// アクションの performed を購読する。CallbackContext はコールバック中だけ有効なので、
        /// 値が必要なら購読内で ReadValue すること。同じパスには同じ Observable を返す
        /// </summary>
        Observable<InputAction.CallbackContext> Performed(string actionPath);

        /// <summary>アクションの performed を値なしで購読する (ボタン向け)。同じパスには同じ Observable を返す</summary>
        Observable<Unit> OnPerformed(string actionPath);

        /// <summary>アクションの現在値 (performed で更新、canceled で既定値)。同じパス・型には同じものを返す</summary>
        ReadOnlyReactiveProperty<T> ReadValue<T>(string actionPath) where T : struct;

        /// <summary>ボタンが押されているか。同じパスには同じものを返す</summary>
        ReadOnlyReactiveProperty<bool> IsPressed(string actionPath);

        /// <summary>
        /// 最後に操作されたデバイスの種類。<see cref="IInputDeviceService"/> がバインドされていればその値
        /// (しきい値・切り替え抑制つき)、無ければアクションが反応したデバイス
        /// </summary>
        ReadOnlyReactiveProperty<InputDeviceKind> CurrentDevice { get; }

        /// <summary>
        /// 最後に使われた操作の系統 (<see cref="InputDeviceKind.KeyboardMouse"/> か <see cref="InputDeviceKind.Gamepad"/> のどちらか)。
        /// タッチ・ペンなどを触っても変わらない。割り当て・ボタンアイコンの選択に使う
        /// </summary>
        ReadOnlyReactiveProperty<InputDeviceKind> CurrentScheme { get; }

        /// <summary>
        /// <see cref="RebindAsync"/> の入力待ち中か。UI 側はこれが true の間、Cancel 入力での「戻る」やナビゲーションを止めること
        /// (入力待ち中に押したボタンで画面が閉じないように)
        /// </summary>
        ReadOnlyReactiveProperty<bool> IsRebinding { get; }

        /// <summary>
        /// ActionMap を有効/無効にする (会話中・カットシーン中など)。
        /// ポーズ中に PauseDisabledMaps のマップを有効にした場合は、ポーズ解除時に有効になる
        /// </summary>
        void SetMapEnabled(string mapName, bool enabled);

        /// <summary>AbubuInputSettings.GameplayMap で指定した ActionMap に <see cref="SetMapEnabled"/> する (未設定なら何もしない)</summary>
        void SetGameplayEnabled(bool enabled);

        /// <summary>
        /// キー割り当てを対話的に変更し、保存する。成功したら true。
        /// キャンセル操作は AbubuInputSettings.RebindCancelPaths (既定: Esc / ゲームパッドの Start。どのデバイスで押してもキャンセルする)。
        /// 入力待ち中は <see cref="IsRebinding"/> が true になり、受け付けないデバイスのボタンは UI・ゲームに届かない。
        /// すでに入力待ち中なら何もせず false を返す。
        /// bindingIndex に複合バインディング (2DVector など) の先頭を指定すると、各パート (Up/Down/...) を順に割り当てる。
        /// 特定のパートだけ変えたい場合はそのパートの bindingIndex を指定する
        /// </summary>
        UniTask<bool> RebindAsync(string actionPath, int bindingIndex = 0, CancellationToken ct = default);
        /// <summary>割り当ての表示用文字列 ("Space" / "A" など)。見つからなければ空文字</summary>
        string GetBindingDisplayString(string actionPath, int bindingIndex = 0);
        /// <summary>割り当ての変更をすべて初期状態に戻し、保存データも消す</summary>
        void ResetBindings();
        /// <summary>
        /// 1 つの割り当ての変更を初期状態に戻して保存する。複合バインディングの先頭を指定すると各パートも戻す
        /// </summary>
        void ResetBinding(string actionPath, int bindingIndex);
        /// <summary>
        /// 割り当てを直接上書きする (保存はしない。続けて <see cref="SaveBindings"/> を呼ぶ)。overridePath が null なら上書きを解除する。
        /// 割り当て変更画面で、かぶった割り当てを入れ替える場合などに使う
        /// </summary>
        void SetBindingOverride(string actionPath, int bindingIndex, string overridePath);
        /// <summary>現在の割り当ての変更を保存する</summary>
        void SaveBindings();
        /// <summary>割り当てが変わったとき (RebindAsync の成功・Reset*・SetBindingOverride) に流れる。表示の更新に使う</summary>
        Observable<Unit> BindingsChanged { get; }

        // --- 以下は AbubuInputSettings のパス (Move / Look / ...) で決まる便利メンバー。アクションが無ければ何も流れない ---

        ReadOnlyReactiveProperty<Vector2> Move { get; }
        ReadOnlyReactiveProperty<Vector2> Look { get; }
        Observable<Unit> Jump { get; }
        Observable<Unit> Attack { get; }
        Observable<Unit> Interact { get; }
        Observable<Unit> Submit { get; }
        Observable<Unit> Cancel { get; }
    }
}
