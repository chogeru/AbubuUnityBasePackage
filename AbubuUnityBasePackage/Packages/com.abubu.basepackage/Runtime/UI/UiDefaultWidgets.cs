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
        /// <summary>確認ダイアログ (タイトル・本文・OK / Cancel) を組み立てる</summary>
        public static UiConfirmDialog CreateConfirmDialog(Transform parent = null, UiWidgetStyle style = null)
        {
            style ??= new UiWidgetStyle();
            var root = CreateRect("ConfirmDialog", parent);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(style.DialogWidth, 0f);

            root.AddComponent<Image>().color = style.PanelColor;
            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 32, 32);
            layout.spacing = 24f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = CreateText("Title", root.transform, style.TitleFontSize, FontStyle.Bold, style.TextColor);
            var message = CreateText("Message", root.transform, style.BodyFontSize, FontStyle.Normal, style.TextColor);

            var buttons = CreateRect("Buttons", root.transform);
            var row = buttons.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 24f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            buttons.AddComponent<LayoutElement>().preferredHeight = 80f;

            var (cancel, cancelLabel) = CreateButton("CancelButton", buttons.transform, style.ButtonColor, style);
            var (ok, okLabel) = CreateButton("OkButton", buttons.transform, style.AccentColor, style);

            var dialog = root.AddComponent<UiConfirmDialog>();
            dialog.Bind(title, message, ok, okLabel, cancel, cancelLabel);
            // ゲームパッドで開いたときは Cancel を選ぶ (連打で確定しないように。UiConfirmDialog.Setup で設定に合わせて選び直す)
            root.AddComponent<UiInitialSelection>().Target = cancel;
            return dialog;
        }

        /// <summary>トースト (画面下の短い通知) を組み立てる</summary>
        public static UiToast CreateToast(Transform parent = null, UiWidgetStyle style = null)
        {
            style ??= new UiWidgetStyle();
            var root = CreateRect("Toast", parent);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 120f);

            var image = root.AddComponent<Image>();
            image.color = style.PanelColor;
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

            var message = CreateText("Message", root.transform, style.BodyFontSize, FontStyle.Normal, style.TextColor);
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

        private static Text CreateText(string name, Transform parent, int size, FontStyle style, Color color)
        {
            var text = CreateRect(name, parent).AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static (Button, Text) CreateButton(string name, Transform parent, Color color, UiWidgetStyle style)
        {
            var go = CreateRect(name, parent);
            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var label = CreateText("Label", go.transform, style.BodyFontSize, FontStyle.Bold, style.TextColor);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            return (button, label);
        }
    }
}
