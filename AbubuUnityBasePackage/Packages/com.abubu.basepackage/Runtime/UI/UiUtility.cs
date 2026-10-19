using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Abubu.UI
{
    /// <summary>ポップアップと画面スタックで共通の処理</summary>
    internal static class UiUtility
    {
        /// <summary>
        /// シーンをまたいで残る Canvas を作る。プレハブが指定されていればそれを複製し、無ければ Screen Space Overlay の Canvas を組み立てる
        /// </summary>
        public static Canvas CreateCanvas(Canvas prefab, string name, int sortingOrder, AbubuUiSettings settings)
        {
            if (prefab != null)
            {
                var canvasFromPrefab = Object.Instantiate(prefab);
                canvasFromPrefab.name = name;
                Object.DontDestroyOnLoad(canvasFromPrefab.gameObject);
                return canvasFromPrefab;
            }

            var go = new GameObject(name);
            Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = settings.ReferenceResolution;
            scaler.matchWidthOrHeight = settings.MatchWidthOrHeight;

            go.AddComponent<GraphicRaycaster>();

            if (settings.UseSafeArea)
            {
                // 中身はノッチを避けた範囲に置く。ポップアップの Backdrop は Canvas 直下に置いて画面全体を覆う
                var safeArea = new GameObject(SafeAreaName, typeof(RectTransform), typeof(UiSafeArea));
                safeArea.transform.SetParent(go.transform, false);
            }
            return canvas;
        }

        private const string SafeAreaName = "[SafeArea]";

        /// <summary>ポップアップ・画面を置く場所 (Safe Area があればその中、無ければ Canvas 直下)</summary>
        public static Transform ContentRoot(Canvas canvas)
        {
            var safeArea = canvas.transform.Find(SafeAreaName);
            return safeArea != null ? safeArea : canvas.transform;
        }

        /// <summary>
        /// 組み込みのダイアログ・トーストにフォントを設定する (フォントが指定されていなければ何もしない)。
        /// 自作のプレハブには使わない (プレハブで指定したフォントを上書きしないように)
        /// </summary>
        public static void ApplyFont(GameObject root, IUiFontProvider fonts)
        {
            if (root == null || fonts == null) return;
            var font = fonts.Font;
            var tmpFont = fonts.TmpFont;
            if (font != null)
            {
                foreach (var text in root.GetComponentsInChildren<Text>(true)) text.font = font;
            }
            if (tmpFont != null)
            {
                foreach (var text in root.GetComponentsInChildren<TMPro.TMP_Text>(true)) text.font = tmpFont;
            }
        }
    }
}
