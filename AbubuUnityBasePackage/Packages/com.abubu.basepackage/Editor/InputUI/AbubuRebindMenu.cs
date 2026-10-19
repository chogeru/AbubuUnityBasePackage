using System.Collections.Generic;
using Abubu.Editor;
using Abubu.Inputs.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Abubu.Inputs.UI.Editor
{
    /// <summary>
    /// GameObject &gt; Abubu &gt; Rebind Panel
    /// キー割り当て変更 (キーコンフィグ) 画面 (<see cref="RebindView"/>) を、行テンプレート付きでワンクリックで生成する。
    /// <code>
    /// // 他のエディタ拡張から: 表示する行を指定して生成する
    /// AbubuRebindMenu.BuildRebindPanel(AbubuUiMenu.FindOrCreateCanvas(), new[] { new RebindEntry("Player/Jump", "Jump") });
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>表示する行は、プロジェクト共通の Input Actions (InputSystem.actions) のゲーム操作のマップ
    /// ("Player"、無ければ "UI" 以外の最初のマップ) のアクションから作る。Input Actions が無ければ Player/Move・Jump などの既定</item>
    /// <item>行テンプレートはパネルの子に非表示で置き、RebindView の Row Prefab に設定する (実行時に行の数だけ複製される)。
    /// 見た目はテンプレートを編集すれば全行に反映される</item>
    /// <item>ラベルは英語。文言は RebindView の Labels で変えられる</item>
    /// <item>RebindView / RebindRowView の private フィールドには SerializedObject 経由で割り当てる。
    /// フィールド名を変えた場合は <see cref="ViewFields"/> / <see cref="RowFields"/> を直すこと (見つからない場合はエラーログを出す)</item>
    /// </list>
    /// Abubu.Inputs.UI (Input System) に依存するため、Abubu.Editor とは別のアセンブリ (Abubu.Inputs.UI.Editor) にある。
    /// </remarks>
    public static class AbubuRebindMenu
    {
        /// <summary><see cref="RebindView"/> のシリアライズフィールド名</summary>
        public static class ViewFields
        {
            public const string RowPrefab = "rowPrefab";
            public const string RowContainer = "rowContainer";
            public const string ResetAllButton = "resetAllButton";
            public const string CancelButton = "cancelButton";
            public const string ListeningPanel = "listeningPanel";
            public const string ListeningLabel = "listeningLabel";
            public const string MessageLabel = "messageLabel";
        }

        /// <summary><see cref="RebindRowView"/> のシリアライズフィールド名</summary>
        public static class RowFields
        {
            public const string ActionLabel = "actionLabel";
            public const string BindingLabel = "bindingLabel";
            public const string GlyphImage = "glyphImage";
            public const string RebindButton = "rebindButton";
            public const string ResetButton = "resetButton";
            public const string ConflictIndicator = "conflictIndicator";
            public const string ConflictLabel = "conflictLabel";
        }

        private const int MaxDefaultRows = 16;

        /// <summary>Hierarchy の右クリックメニューから割り当て変更パネルを作る。Undo に対応。</summary>
        [MenuItem("GameObject/Abubu/Rebind Panel", priority = 12)]
        private static void CreateRebindPanel(MenuCommand command)
        {
            // 複数選択で選択数ぶん呼ばれたときは最初の 1 回だけ作る
            var selected = Selection.objects;
            if (command.context != null && selected.Length > 1 && command.context != selected[0]) return;

            var panel = BuildRebindPanel(AbubuUiMenu.FindOrCreateCanvas(command.context as GameObject));
            Undo.RegisterCreatedObjectUndo(panel, "Create Rebind Panel");
            Selection.activeGameObject = panel;
        }

        /// <summary>
        /// 割り当て変更パネル (タイトル・スクロールする行の一覧・Reset All・入力待ち表示) を生成し、<see cref="RebindView"/> に結線する。
        /// </summary>
        /// <param name="canvas">パネルを配置する Canvas</param>
        /// <param name="entries">表示する行 (null ならプロジェクトの Input Actions から作る)</param>
        /// <returns>生成したパネルのルート GameObject</returns>
        public static GameObject BuildRebindPanel(Canvas canvas, IEnumerable<RebindEntry> entries = null)
        {
            var resources = AbubuUiMenu.GetResources();

            var panel = DefaultControls.CreatePanel(resources);
            panel.name = "RebindPanel";
            GameObjectUtility.SetParentAndAlign(panel, canvas.gameObject);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(720f, 600f);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            var title = CreateText(panel.transform, "Title", "Controls", resources, 28);
            title.alignment = TextAnchor.MiddleCenter;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            // 行の一覧 (縦にスクロール)
            var scrollGo = DefaultControls.CreateScrollView(resources);
            scrollGo.name = "Rows";
            scrollGo.transform.SetParent(panel.transform, false);
            scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            if (scroll.horizontalScrollbar != null) Object.DestroyImmediate(scroll.horizontalScrollbar.gameObject);
            scroll.horizontalScrollbar = null;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            var content = scroll.content;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.spacing = 6f;
            contentLayout.childControlHeight = contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var message = CreateText(panel.transform, "Message", string.Empty, resources, 18);
            message.alignment = TextAnchor.MiddleCenter;
            message.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var footer = CreateHorizontal(panel.transform, "Footer", 44f);
            footer.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
            var resetAll = CreateButton(footer.transform, "Reset All", resources, 160f);

            // 行テンプレート (非表示。実行時に RebindView が行の数だけ複製する)
            var row = BuildRowTemplate(panel.transform, resources);

            // 入力待ちの表示 (パネル全体を覆う)
            var listening = BuildListeningOverlay(panel.transform, resources, out var listeningLabel, out var cancel);

            var view = panel.AddComponent<RebindView>();
            foreach (var entry in entries ?? GetDefaultEntries()) view.EntryList.Add(entry);
            var so = new SerializedObject(view);
            Assign(so, ViewFields.RowPrefab, row);
            Assign(so, ViewFields.RowContainer, content);
            Assign(so, ViewFields.ResetAllButton, resetAll);
            Assign(so, ViewFields.CancelButton, cancel);
            Assign(so, ViewFields.ListeningPanel, listening);
            Assign(so, ViewFields.ListeningLabel, listeningLabel);
            Assign(so, ViewFields.MessageLabel, message);
            so.ApplyModifiedPropertiesWithoutUndo();

            AbubuUiMenu.AddNavigationHelpers(panel);
            return panel;
        }

        /// <summary>1 行分のテンプレート: [アクション名] [割り当て (アイコン + 文字) のボタン] [Reset] [!]</summary>
        private static RebindRowView BuildRowTemplate(Transform parent, DefaultControls.Resources resources)
        {
            var row = CreateHorizontal(parent, "RowTemplate", 44f);
            row.GetComponent<LayoutElement>().ignoreLayout = true;

            var actionLabel = CreateText(row.transform, "Action", "Action", resources, 20);
            actionLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 220f;

            // 割り当てボタン: 中にアイコンと文字を横に並べる
            var rebind = CreateButton(row.transform, string.Empty, resources, 0f);
            rebind.name = "Binding";
            rebind.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var buttonText = rebind.GetComponentInChildren<Text>();
            if (buttonText != null) Object.DestroyImmediate(buttonText.gameObject);
            var inner = rebind.gameObject.AddComponent<HorizontalLayoutGroup>();
            inner.padding = new RectOffset(8, 8, 4, 4);
            inner.spacing = 8f;
            inner.childAlignment = TextAnchor.MiddleCenter;
            inner.childControlWidth = inner.childControlHeight = true;
            inner.childForceExpandWidth = false;

            var glyphGo = new GameObject("Glyph", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            glyphGo.transform.SetParent(rebind.transform, false);
            var glyphLayout = glyphGo.GetComponent<LayoutElement>();
            glyphLayout.preferredWidth = glyphLayout.preferredHeight = 32f;
            var glyph = glyphGo.GetComponent<Image>();
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;
            glyph.enabled = false;

            var bindingLabel = CreateText(rebind.transform, "Text", "-", resources, 20);
            bindingLabel.alignment = TextAnchor.MiddleCenter;
            bindingLabel.raycastTarget = false;

            var reset = CreateButton(row.transform, "Reset", resources, 90f);

            var conflict = CreateText(row.transform, "Conflict", "!", resources, 22);
            conflict.color = new Color(1f, 0.55f, 0.1f);
            conflict.alignment = TextAnchor.MiddleCenter;
            conflict.gameObject.AddComponent<LayoutElement>().preferredWidth = 24f;
            conflict.gameObject.SetActive(false);

            var view = row.AddComponent<RebindRowView>();
            var so = new SerializedObject(view);
            Assign(so, RowFields.ActionLabel, actionLabel);
            Assign(so, RowFields.BindingLabel, bindingLabel);
            Assign(so, RowFields.GlyphImage, glyph);
            Assign(so, RowFields.RebindButton, rebind);
            Assign(so, RowFields.ResetButton, reset);
            Assign(so, RowFields.ConflictIndicator, conflict.gameObject);
            so.ApplyModifiedPropertiesWithoutUndo();

            row.SetActive(false);
            return view;
        }

        /// <summary>入力待ちの表示: 半透明の背景 + 「ボタンを押してください」 + Cancel ボタン</summary>
        private static GameObject BuildListeningOverlay(Transform parent, DefaultControls.Resources resources, out Text label, out Button cancel)
        {
            var overlay = new GameObject("Listening", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            overlay.transform.SetParent(parent, false);
            overlay.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = overlay.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            var layout = overlay.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 16f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            label = CreateText(overlay.transform, "Prompt", "Press a button", resources, 24);
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            cancel = CreateButton(overlay.transform, "Cancel", resources, 180f);
            overlay.SetActive(false);
            return overlay;
        }

        /// <summary>プロジェクト共通の Input Actions のゲーム操作のマップから行を作る。無ければ Player/Move などの既定</summary>
        public static List<RebindEntry> GetDefaultEntries()
        {
            var entries = new List<RebindEntry>();
            var asset = InputSystem.actions;
            if (asset != null)
            {
                var map = asset.FindActionMap("Player");
                if (map == null)
                {
                    foreach (var candidate in asset.actionMaps)
                    {
                        if (candidate.name == "UI") continue;
                        map = candidate;
                        break;
                    }
                }
                if (map != null)
                {
                    foreach (var action in map.actions)
                    {
                        // マウス位置・視点などの値は割り当て変更の対象にしないことが多いので、ボタンと Move だけにする
                        if (action.type != InputActionType.Button && action.name != "Move") continue;
                        entries.Add(new RebindEntry(map.name + "/" + action.name, action.name));
                        if (entries.Count >= MaxDefaultRows) break;
                    }
                }
            }

            if (entries.Count == 0)
            {
                entries.Add(new RebindEntry("Player/Move", "Move"));
                entries.Add(new RebindEntry("Player/Jump", "Jump"));
                entries.Add(new RebindEntry("Player/Attack", "Attack"));
                entries.Add(new RebindEntry("Player/Interact", "Interact"));
            }
            return entries;
        }

        private static GameObject CreateHorizontal(Transform parent, string name, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            return go;
        }

        private static Text CreateText(Transform parent, string name, string value, DefaultControls.Resources resources, int fontSize)
        {
            var go = DefaultControls.CreateText(resources);
            go.name = name;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static Button CreateButton(Transform parent, string label, DefaultControls.Resources resources, float width)
        {
            var go = DefaultControls.CreateButton(resources);
            go.name = string.IsNullOrEmpty(label) ? "Button" : label;
            go.transform.SetParent(parent, false);
            var element = go.AddComponent<LayoutElement>();
            if (width > 0f) element.preferredWidth = width;
            element.preferredHeight = 40f;
            var text = go.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                text.fontSize = 20;
            }
            return go.GetComponent<Button>();
        }

        private static void Assign(SerializedObject so, string fieldName, Object value)
        {
            var property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[Abubu] {so.targetObject.GetType().Name} にフィールド \"{fieldName}\" が見つかりません。AbubuRebindMenu のフィールド名定数を更新してください。");
                return;
            }
            property.objectReferenceValue = value;
        }
    }
}
