using UnityEngine;

namespace Abubu.UI
{
    /// <summary>
    /// 任意。Resources/AbubuUiSettings として置くと読み込まれる。無ければ既定値を使う。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/UI Settings", fileName = "AbubuUiSettings")]
    public sealed class AbubuUiSettings : ScriptableObject
    {
        public const string ResourcePath = "AbubuUiSettings";

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

        [Tooltip("ポップアップ用 Canvas のプレハブ (任意)。SafeArea 対応や独自の Canvas Scaler 設定を使いたいときに指定する。未設定なら Sorting Order / Reference Resolution から自動で作る")]
        public Canvas PopupCanvasPrefab;

        [Header("Screens")]
        [Tooltip("画面スタック用 Canvas のプレハブ (任意)。未設定なら Screen Sorting Order / Reference Resolution から自動で作る")]
        public Canvas ScreenCanvasPrefab;

        [Tooltip("画面スタック (IScreenService) 用 Canvas の Sorting Order。ポップアップより後ろに出すため SortingOrder より小さくする")]
        public int ScreenSortingOrder = 50;

        [Tooltip("次の画面を開いたとき、前の画面を非表示にして残す (戻ったときにスクロール位置などが残る)。オフなら破棄し、戻るときに作り直す")]
        public bool KeepPreviousScreens = true;

        [Header("Navigation")]
        [Tooltip("Cancel 入力 (Esc / ゲームパッドの B など) で、一番手前のポップアップを閉じる。ポップアップが無ければ前の画面に戻る (Input System 導入時)")]
        public bool BackOnCancel;
    }
}
