using Abubu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Abubu.Editor
{
    /// <summary>
    /// GameObject > Abubu > Volume Settings Panel
    /// スライダー付きの音量設定パネルをワンクリックで生成する。
    /// </summary>
    public static class AbubuUiMenu
    {
        [MenuItem("GameObject/Abubu/Volume Settings Panel", priority = 10)]
        private static void CreateVolumeSettings(MenuCommand command)
        {
            var panel = BuildVolumeSettingsPanel(FindOrCreateCanvas());
            Undo.RegisterCreatedObjectUndo(panel, "Create Volume Settings");
            Selection.activeGameObject = panel;
        }

        internal static GameObject BuildVolumeSettingsPanel(Canvas canvas)
        {
            var resources = GetResources();

            var panel = DefaultControls.CreatePanel(resources);
            panel.name = "VolumeSettings";
            GameObjectUtility.SetParentAndAlign(panel, canvas.gameObject);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(480f, 320f);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            var view = panel.AddComponent<VolumeSettingsView>();
            var so = new SerializedObject(view);
            so.FindProperty("master").objectReferenceValue = CreateRow(panel.transform, "Master", resources);
            so.FindProperty("bgm").objectReferenceValue = CreateRow(panel.transform, "BGM", resources);
            so.FindProperty("se").objectReferenceValue = CreateRow(panel.transform, "SE", resources);
            so.FindProperty("ambient").objectReferenceValue = CreateRow(panel.transform, "Ambient", resources);

            var toggleGo = DefaultControls.CreateToggle(resources);
            toggleGo.name = "Mute";
            toggleGo.transform.SetParent(panel.transform, false);
            toggleGo.GetComponentInChildren<Text>().text = "Mute";
            toggleGo.GetComponent<Toggle>().isOn = false;
            toggleGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            so.FindProperty("mute").objectReferenceValue = toggleGo.GetComponent<Toggle>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        [MenuItem("GameObject/Abubu/Graphics Settings Panel", priority = 11)]
        private static void CreateGraphicsSettings(MenuCommand command)
        {
            var panel = BuildGraphicsSettingsPanel(FindOrCreateCanvas());
            Undo.RegisterCreatedObjectUndo(panel, "Create Graphics Settings");
            Selection.activeGameObject = panel;
        }

        internal static GameObject BuildGraphicsSettingsPanel(Canvas canvas)
        {
            var resources = GetResources();

            var panel = CreatePanel(canvas, resources, "GraphicsSettings", new Vector2(560f, 380f));
            var view = panel.AddComponent<GraphicsSettingsView>();
            var so = new SerializedObject(view);
            so.FindProperty("resolution").objectReferenceValue = CreateLabeled(panel.transform, "解像度", DefaultControls.CreateDropdown(resources), resources);
            so.FindProperty("windowMode").objectReferenceValue = CreateLabeled(panel.transform, "表示モード", DefaultControls.CreateDropdown(resources), resources);
            so.FindProperty("quality").objectReferenceValue = CreateLabeled(panel.transform, "画質", DefaultControls.CreateDropdown(resources), resources);
            so.FindProperty("frameRate").objectReferenceValue = CreateLabeled(panel.transform, "フレームレート", DefaultControls.CreateDropdown(resources), resources);

            var toggleGo = DefaultControls.CreateToggle(resources);
            toggleGo.name = "VSync";
            toggleGo.transform.SetParent(panel.transform, false);
            toggleGo.GetComponentInChildren<Text>().text = "VSync";
            toggleGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            so.FindProperty("vSync").objectReferenceValue = toggleGo.GetComponent<Toggle>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        private static GameObject CreatePanel(Canvas canvas, DefaultControls.Resources resources, string name, Vector2 size)
        {
            var panel = DefaultControls.CreatePanel(resources);
            panel.name = name;
            GameObjectUtility.SetParentAndAlign(panel, canvas.gameObject);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return panel;
        }

        private static T CreateLabeled<T>(Transform parent, string label, GameObject control, DefaultControls.Resources resources) where T : Component =>
            CreateLabeledRow(parent, label, control, resources).GetComponent<T>();

        private static Dropdown CreateLabeled(Transform parent, string label, GameObject control, DefaultControls.Resources resources) =>
            CreateLabeled<Dropdown>(parent, label, control, resources);

        private static GameObject CreateLabeledRow(Transform parent, string label, GameObject control, DefaultControls.Resources resources)
        {
            var row = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 40f;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;

            var text = DefaultControls.CreateText(resources);
            text.transform.SetParent(row.transform, false);
            var t = text.GetComponent<Text>();
            t.text = label;
            t.alignment = TextAnchor.MiddleLeft;
            text.AddComponent<LayoutElement>().preferredWidth = 150f;

            control.transform.SetParent(row.transform, false);
            control.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return control;
        }

        private static Slider CreateRow(Transform parent, string label, DefaultControls.Resources resources)
        {
            var row = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 40f;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;

            var text = DefaultControls.CreateText(resources);
            text.transform.SetParent(row.transform, false);
            var t = text.GetComponent<Text>();
            t.text = label;
            t.alignment = TextAnchor.MiddleLeft;
            text.AddComponent<LayoutElement>().preferredWidth = 110f;

            var sliderGo = DefaultControls.CreateSlider(resources);
            sliderGo.transform.SetParent(row.transform, false);
            sliderGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return sliderGo.GetComponent<Slider>();
        }

        internal static Canvas FindOrCreateCanvas()
        {
            if (Selection.activeGameObject != null)
            {
                var selected = Selection.activeGameObject.GetComponentInParent<Canvas>();
                if (selected != null) return selected;
            }

            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas != null) return canvas;

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                // Input System / 旧 Input のどちらでも動くモジュールを付ける
                var moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem")
                                 ?? typeof(StandaloneInputModule);
                es.AddComponent(moduleType);
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }

            return canvas;
        }

        internal static DefaultControls.Resources GetResources() => new()
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };
    }
}
