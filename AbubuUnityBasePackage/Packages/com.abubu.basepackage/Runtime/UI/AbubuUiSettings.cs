using System;
using TMPro;
using UnityEngine;

namespace Abubu.UI
{
    /// <summary>ポップアップ・画面を開いたときに、最初のボタンを選択するか</summary>
    public enum UiSelectOnOpen
    {
        /// <summary>UI Initial Selection が付いているときだけ選択する</summary>
        Never,
        /// <summary>ゲームパッドで操作中なら最初のボタンを選択し、マウス・タッチなら選択を外す (裏の UI が選ばれたままにならないように)</summary>
        Auto,
        /// <summary>常に最初のボタンを選択する (ゲームパッド・キーボード専用のゲーム向け)</summary>
        Always,
    }

    /// <summary>確認ダイアログを開いたときに選択しておくボタン</summary>
    public enum ConfirmDefaultButton
    {
        /// <summary>キャンセル (連打で削除などを確定してしまわないように)</summary>
        Cancel,
        Ok,
    }

    /// <summary>組み込みの確認ダイアログ・トーストの見た目 (プレハブを指定しないとき)</summary>
    [Serializable]
    public sealed class UiWidgetStyle
    {
        public Color PanelColor = new(0.12f, 0.12f, 0.14f, 0.96f);
        public Color ButtonColor = new(0.25f, 0.25f, 0.3f, 1f);
        public Color AccentColor = new(0.2f, 0.45f, 0.85f, 1f);
        public Color TextColor = Color.white;
        [Min(1)] public int TitleFontSize = 40;
        [Min(1)] public int BodyFontSize = 30;
        [Min(1f)] public float DialogWidth = 720f;
    }

    /// <summary>
    /// ポップアップ・画面遷移・ダイアログの設定。AbubuSettings > Module Settings に入れるか、Resources/AbubuUiSettings として置く。無ければ既定値を使う。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/UI Settings", fileName = "AbubuUiSettings")]
    public sealed class AbubuUiSettings : ScriptableObject
    {
        public const string ResourcePath = "AbubuUiSettings";

        private static AbubuUiSettings _default;

        /// <summary>設定アセットが無いときに使う既定値 (共有のインスタンス)</summary>
        public static AbubuUiSettings Default
        {
            get
            {
                if (_default != null) return _default;
                _default = CreateInstance<AbubuUiSettings>();
                _default.hideFlags = HideFlags.HideAndDontSave;
                return _default;
            }
        }

        [Tooltip("ポップアップが 1 つでも開いている間、IPauseService でポーズする")]
        public bool PauseWhileOpen;

        [Tooltip("ポップアップの背面を覆う色。背後のボタンを押せなくする (alpha 0 でも操作は遮断される)。Backdrop Prefab を指定した場合は使わない")]
        public Color BackdropColor = new(0f, 0f, 0f, 0.5f);

        [Tooltip("ポップアップの背面に置くプレハブ (任意)。全画面を覆い、Raycast Target が有効な Graphic を含めること。未設定なら Backdrop Color の Image を作る")]
        public GameObject BackdropPrefab;

        [Tooltip("背面をクリックしたら一番上のポップアップを閉じる")]
        public bool CloseOnBackdropClick;

        [Tooltip("シーンが切り替わったら、開いているポップアップをすべて閉じる")]
        public bool CloseOnSceneChange = true;

        [Tooltip("ポップアップ用 Canvas の Sorting Order")]
        public int SortingOrder = 100;

        [Tooltip("Canvas Scaler の基準解像度 (Canvas Prefab 未設定時)")]
        public Vector2 ReferenceResolution = new(1920f, 1080f);

        [Tooltip("Canvas Scaler の Match (0 = 幅に合わせる, 1 = 高さに合わせる)。Canvas Prefab 未設定時")]
        [Range(0f, 1f)] public float MatchWidthOrHeight = 0.5f;

        [Tooltip("自動で作る Canvas の中身を、ノッチ・角丸を避けた範囲 (Screen.safeArea) に収める。Backdrop は画面全体を覆う")]
        public bool UseSafeArea = true;

        [Tooltip("ポップアップ用 Canvas のプレハブ (任意)。VR (World Space) や独自の Canvas Scaler 設定を使いたいときに指定する。未設定なら Sorting Order / Reference Resolution から自動で作る")]
        public Canvas PopupCanvasPrefab;

        [Header("Screens")]
        [Tooltip("画面スタック用 Canvas のプレハブ (任意)。未設定なら Screen Sorting Order / Reference Resolution から自動で作る")]
        public Canvas ScreenCanvasPrefab;

        [Tooltip("画面スタック (IScreenService) 用 Canvas の Sorting Order。ポップアップより後ろに出すため SortingOrder より小さくする")]
        public int ScreenSortingOrder = 50;

        [Tooltip("次の画面を開いたとき、前の画面を非表示にして残す (戻ったときにスクロール位置などが残る)。オフなら破棄し、戻るときに作り直す")]
        public bool KeepPreviousScreens = true;

        [Header("Dialog / Toast")]
        [Tooltip("Popups.Confirm / Alert で使う確認ダイアログのプレハブ (任意)。未設定なら組み込みの見た目を使う")]
        public UiConfirmDialog ConfirmDialogPrefab;

        [Tooltip("Popups.Toast で使うトーストのプレハブ (任意)。未設定なら組み込みの見た目を使う")]
        public UiToast ToastPrefab;

        [Tooltip("確認ダイアログを開いたときに選択しておくボタン")]
        public ConfirmDefaultButton ConfirmDefaultButton = ConfirmDefaultButton.Cancel;

        [Tooltip("トーストを表示する秒数")]
        [Min(0f)] public float ToastDuration = 2f;

        [Tooltip("トーストが現れる・消える時間 (秒)")]
        [Min(0f)] public float ToastFadeInSeconds = 0.15f;
        [Min(0f)] public float ToastFadeOutSeconds = 0.25f;

        [Tooltip("ダイアログの OK ボタンの文言 (ローカライズが有効なら abubu.common.ok が優先)")]
        public string OkLabel = "OK";

        [Tooltip("ダイアログの Cancel ボタンの文言 (ローカライズが有効なら abubu.common.cancel が優先)")]
        public string CancelLabel = "Cancel";

        [Tooltip("組み込みのダイアログ・トーストのフォント (UI.Text 用)。未設定なら Unity 組み込みのフォント (日本語などが表示できない場合がある)。ローカライズの言語ごとのフォントがあればそちらが優先")]
        public Font DefaultFont;

        [Tooltip("TextMeshPro を使う自作プレハブ用の既定フォント (ローカライズの言語ごとのフォントがあればそちらが優先)")]
        public TMP_FontAsset DefaultTmpFont;

        [Tooltip("組み込みのダイアログ・トーストの色・大きさ")]
        public UiWidgetStyle WidgetStyle = new();

        [Header("Navigation (ゲームパッド・キーボード)")]
        [Tooltip("ポップアップ・画面を開いたときに最初のボタンを選択するか。Auto はゲームパッドで操作中だけ選択する")]
        public UiSelectOnOpen SelectOnOpen = UiSelectOnOpen.Auto;

        [Tooltip("ゲームパッド・キーボードで方向入力したとき、選択が無い・一番手前の UI の外にあれば選び直す (Input System 導入時)")]
        public bool KeepFocusOnNavigate = true;

        [Tooltip("方向入力の Input Action のパス (Keep Focus On Navigate で使う)")]
        public string NavigateActionPath = "UI/Navigate";

        [Tooltip("決定入力の Input Action のパス (何も選択されていないときに押したら、決定せずに選択を用意する)")]
        public string SubmitActionPath = "UI/Submit";

        [Tooltip("Cancel 入力 (Esc / ゲームパッドの B など) で、一番手前のポップアップを閉じる。ポップアップが無ければ前の画面に戻る (Input System 導入時)。自前で Cancel を結線している場合はオフにする")]
        public bool BackOnCancel = true;

        [Header("EventSystem")]
        [Tooltip("ポップアップ・画面・トーストを出すときに EventSystem が無ければ、シーンをまたいで残る EventSystem を用意する。シーンに EventSystem があればそちらを優先し、用意したものは止める")]
        public bool EnsureEventSystem = true;
    }
}
