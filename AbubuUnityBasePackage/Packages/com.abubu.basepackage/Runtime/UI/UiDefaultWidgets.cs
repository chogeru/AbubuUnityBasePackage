using UnityEngine;
using UnityEngine.UI;

namespace Abubu.UI
{
    /// <summary>
    /// 設定不要で使えるよう、確認ダイアログとトーストの見た目をコードで組み立てる。
    /// プレハブを指定していないときの既定、および Tools > Abubu > Create UI Templates でプレハブ化する元として使う。
    /// </summary>
    /// <remarks>
    /// 文字は Unity 組み込みのフォント (UI.Text) で表示する。日本語などを確実に表示したい場合は、
    /// テンプレートのプレハブを作ってフォントを差し替えるか、TextMeshPro のプレハブを用意する。
    /// </remarks>
    public static class UiDefaultWidgets
    {
        private static readonly Color PanelColor = new(0.12f, 0.12f, 0.14f, 0.96f);
        private static readonly Color ButtonColor = new(0.25f, 0.25f, 0.3f, 1f);
        private static readonly Color AccentColor = new(0.2f, 0.45f, 0.85f, 1f);

        /// <summary>確認ダイアログ (タイトル・本文・OK / Cancel) を組み立てる</summary>
        public static UiConfirmDialog CreateConfirmDialog(Transform parent = null)
        {
            var root = CreateRect("ConfirmDialog", parent);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(720f, 0f);

            root.AddComponent<Image>().color = PanelColor;
            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 32, 32);
            layout.spacing = 24f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = CreateText("Title", root.transform, 40, FontStyle.Bold);
            var message = CreateText("Message", root.transform, 30, FontStyle.Normal);

            var buttons = CreateRect("Buttons", root.transform);
            var row = buttons.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 24f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            buttons.AddComponent<LayoutElement>().preferredHeight = 80f;

            var (cancel, cancelLabel) = CreateButton("CancelButton", buttons.transform, ButtonColor);
            var (ok, okLabel) = CreateButton("OkButton", buttons.transform, AccentColor);

            var dialog = root.AddComponent<UiConfirmDialog>();
            dialog.Bind(title, message, ok, okLabel, cancel, cancelLabel);
            // ゲームパッドで開いたときは OK を選ぶ
            root.AddComponent<UiInitialSelection>().Target = ok;
            return dialog;
        }

        /// <summary>トースト (画面下の短い通知) を組み立てる</summary>
        public static UiToast CreateToast(Transform parent = null)
        {
            var root = CreateRect("Toast", parent);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 120f);

            var image = root.AddComponent<Image>();
            image.color = PanelColor;
            image.raycastTarget = false;
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 20, 20);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var message = CreateText("Message", root.transform, 30, FontStyle.Normal);
            message.raycastTarget = false;
            var toast = root.AddComponent<UiToast>();
            toast.Bind(message);
            return toast;
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static Text CreateText(string name, Transform parent, int size, FontStyle style)
        {
            var text = CreateRect(name, parent).AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static (Button, Text) CreateButton(string name, Transform parent, Color color)
        {
            var go = CreateRect(name, parent);
            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var label = CreateText("Label", go.transform, 30, FontStyle.Bold);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            return (button, label);
        }
    }
}
