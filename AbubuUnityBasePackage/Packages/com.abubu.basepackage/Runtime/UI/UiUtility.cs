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
            return canvas;
        }
    }
}
