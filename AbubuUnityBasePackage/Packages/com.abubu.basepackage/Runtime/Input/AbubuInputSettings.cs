using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Abubu.Inputs
{
    /// <summary>
    /// 入力モジュールの設定 (任意)。次の順に探す:
    /// <list type="number">
    /// <item>DI コンテナにバインドされたインスタンス (AbubuSettings.ModuleSettings に追加する、
    /// または独自 Installer で <c>Container.BindInstance(mySettings)</c>)</item>
    /// <item>Resources/<see cref="ResourcePath"/> (= Resources/AbubuInputSettings.asset)</item>
    /// <item>どちらも無ければ既定値 (プロジェクト共通の Input Actions を読むだけで、ActionMap の有効/無効は変更しない)</item>
    /// </list>
    /// モジュールごと使わない場合は <see cref="Enabled"/> をオフにするか、
    /// Scripting Define Symbols に <see cref="DisableDefine"/> を追加する。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/Input Settings", fileName = "AbubuInputSettings")]
    public sealed class AbubuInputSettings : ScriptableObject
    {
        /// <summary>Resources から読み込むときのパス</summary>
        public const string ResourcePath = "AbubuInputSettings";
        /// <summary>割り当て変更を保存するキーの既定値</summary>
        public const string DefaultSaveKey = "abubu.input-bindings";
        /// <summary>この Scripting Define Symbol があると、入力モジュールを自動でインストールしない</summary>
        public const string DisableDefine = "ABUBU_DISABLE_INPUT_MODULE";

        [Tooltip("オフにすると入力モジュールをインストールしない (DI でバインドした場合は、サービスは何もしない状態になる)")]
        public bool Enabled = true;

        [Tooltip("使用する Input Actions。未設定ならプロジェクト共通の Input Actions (Project Settings > Input System Package) を使う")]
        public InputActionAsset Actions;

        [Header("ActionMap の有効/無効")]
        [Tooltip("起動時に有効にする ActionMap。空なら有効/無効を一切変更しない (プロジェクト共通の Input Actions は Input System が自動で有効にする)。" +
                 "Actions に独自アセットを指定した場合は、使うマップをここに書くこと")]
        public List<string> EnabledMaps = new();

        [Tooltip("ポーズ中に無効にする ActionMap (例: \"Player\")。空ならポーズしても何も無効にしない。" +
                 "ポーズ解除の操作 (Pause ボタンなど) をここに書いたマップに置くと解除できなくなるので、UI マップなど別のマップに置くこと")]
        public List<string> PauseDisabledMaps = new();

        [Tooltip("SetGameplayEnabled で切り替える ActionMap。空なら SetGameplayEnabled は何もしない")]
        public string GameplayMap = "Player";

        [Header("割り当ての保存")]
        [Tooltip("起動時に保存済みの割り当て変更を読み込む")]
        public bool LoadBindingOverridesOnStart = true;

        [Tooltip("終了時 (Dispose) に、このサービスが読み込んだ / 変更した割り当てを元に戻す (Domain Reload 無効時に次の再生へ持ち越さない)")]
        public bool RevertBindingOverridesOnDispose = true;

        [Tooltip("割り当て変更を ISaveService に保存するキー。空なら保存しない")]
        public string SaveKey = DefaultSaveKey;

        [Header("割り当て変更 (RebindAsync)")]
        [Tooltip("割り当て変更をキャンセルする操作 (Input Control Path)。どのデバイスで押してもキャンセルする " +
                 "(キーボードの割り当てを変更中にゲームパッドの Start を押してもキャンセルできる)。ここに書いた操作は割り当てられない")]
        public List<string> RebindCancelPaths = new() { "<Keyboard>/escape", "<Gamepad>/start" };

        [Tooltip("割り当て変更で拾わない操作 (Input Control Path)。マウス移動などで意図せず確定しないようにする")]
        public List<string> RebindExcludedPaths = new() { "<Mouse>/position", "<Mouse>/delta", "<Mouse>/scroll" };

        [Tooltip("割り当て変更で、元の割り当てと同じ種類のデバイス (キーボード&マウス / ゲームパッド) の操作だけを受け付ける。" +
                 "キーボード用の割り当てにゲームパッドのボタンが入ってしまうのを防ぐ。受け付けないデバイスのボタンは、入力待ちの間 UI やゲームにも届かない")]
        public bool RebindMatchBindingDevice = true;

        [Tooltip("割り当て変更で、マウスのボタンを割り当てられるようにする。マウスのボタンは離してから 1 フレーム待って確定するので、" +
                 "キャンセルボタンをクリックしても割り当てられない")]
        public bool RebindAllowMouseButtons = true;

        [Tooltip("割り当て変更の入力待ちを、この秒数何も押されなければ自動でキャンセルする。0 なら無制限")]
        [Min(0f)]
        public float RebindTimeout = 10f;

        [Header("切断・デバッグ")]
        [Tooltip("使用中のゲームパッドが切断されたらポーズし、ゲームパッドがつながったら再開する (GamepadDisconnectPauser)。" +
                 "「コントローラーを接続してください」の表示は GamepadDisconnectPauser.IsWaitingForReconnect を見て出す")]
        public bool PauseOnGamepadDisconnect = true;

        [Tooltip("DebugOverlay をゲームパッドで操作している間、GameplayMap と PauseDisabledMaps の ActionMap を無効にする")]
        public bool BlockGameplayWhileDebugOverlayFocused = true;

        [Header("便利メンバー (Move / Jump など) のアクションパス (Map/Action)。使わないものは空でよい")]
        public string Move = "Player/Move";
        public string Look = "Player/Look";
        public string Jump = "Player/Jump";
        public string Attack = "Player/Attack";
        public string Interact = "Player/Interact";
        public string Submit = "UI/Submit";
        public string Cancel = "UI/Cancel";

        [Header("使用中のデバイスの判定 (CurrentDevice)")]
        public InputDeviceSwitchSettings DeviceSwitch = new();

        [Tooltip("ゲームパッド使用中はマウスカーソルを隠し、マウスを動かしたら表示する")]
        public bool AutoHideCursor;

        [Header("振動 (IHapticsService / GameInput.Rumble)")]
        public HapticsSettings Haptics = new();

        [Header("ボタンアイコン (IInputGlyphService / InputGlyphImage)")]
        [Tooltip("操作 → アイコンの対応表 (任意)。未設定ならアイコンは出さず、表示文字列だけを使う")]
        public InputGlyphs Glyphs;

        [Header("診断")]
        [Tooltip("オンにすると、見つからないアクション / ActionMap / Input Actions を警告する")]
        public bool StrictActionLookup;

        /// <summary>Resources/<see cref="ResourcePath"/> の設定。無ければ null (呼び出し側で既定値を使う)</summary>
        public static AbubuInputSettings FindDefault() => Resources.Load<AbubuInputSettings>(ResourcePath);
    }
}
