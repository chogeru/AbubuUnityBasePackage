using UnityEngine;

namespace Abubu.UI
{
    /// <summary>
    /// RectTransform を Screen.safeArea (ノッチ・角丸・ホームバーを避けた範囲) に合わせる。
    /// 自動で作る Canvas には AbubuUiSettings.UseSafeArea で付く。自分の Canvas では、中身をまとめる子オブジェクトに付ける。
    /// </summary>
    [AddComponentMenu("Abubu/UI Safe Area")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public sealed class UiSafeArea : MonoBehaviour
    {
        private Rect _applied;
        private Vector2Int _screen;

        private void OnEnable() => Apply(force: true);

        // 端末の回転・解像度の変更に追従する (変わったときだけ書き換える)
        private void Update() => Apply(force: false);

        private void Apply(bool force)
        {
            var safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == _applied && screen == _screen) return;
            if (screen.x <= 0 || screen.y <= 0) return;
            _applied = safe;
            _screen = screen;

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            rect.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
