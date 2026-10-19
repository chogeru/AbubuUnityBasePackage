using System.Reflection;
using Abubu.Presentation;
using Abubu.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Abubu.Editor
{
    /// <summary>
    /// GameObject > Abubu > Volume Settings Panel
    /// スライダー付きの音量設定パネルをワンクリックで生成する。
    /// </summary>
    /// <remarks>
    /// Build*Panel は public なので、サンプル生成などほかのエディタ拡張からも呼び出せる。
    /// View の private シリアライズフィールドには SerializedObject 経由で UI 部品を割り当てている。
    /// フィールド名は <see cref="VolumeFields"/> / <see cref="GraphicsFields"/> に集約してあるので、
    /// View 側のフィールド名を変えた場合はそこを直すこと (見つからない場合はエラーログを出す)。
    /// </remarks>
    public static class AbubuUiMenu
    {
        /// <summary><see cref="VolumeSettingsView"/> のシリアライズフィールド名</summary>
        public static class VolumeFields
        {
            public const string Master = "master";
            public const string Bgm = "bgm";
            public const string Se = "se";
            public const string Ambient = "ambient";
            public const string Voice = "voice";
            public const string Mute = "mute";
        }

        /// <summary><see cref="GraphicsSettingsView"/> のシリアライズフィールド名</summary>
        public static class GraphicsFields
        {
            public const string Resolution = "resolution";
            public const string WindowMode = "windowMode";
            public const string Quality = "quality";
            public const string VSync = "vSync";
            public const string FrameRate = "frameRate";
        }

        /// <summary>Hierarchy の右クリックメニューから音量設定パネルを作る。Undo に対応。</summary>
        [MenuItem("GameObject/Abubu/Volume Settings Panel", priority = 10)]
        private static void CreateVolumeSettings(MenuCommand command)
        {
            if (!IsFirstInvocation(command)) return;
            var panel = BuildVolumeSettingsPanel(FindOrCreateCanvas(command.context as GameObject));
            Undo.RegisterCreatedObjectUndo(panel, "Create Volume Settings");
            Selection.activeGameObject = panel;
        }

        /// <summary>
        /// Master / BGM / SE / Ambient / Voice のスライダーと Mute トグルを持つパネルを生成し、
        /// <see cref="VolumeSettingsView"/> に各 UI を結線する。
        /// </summary>
        /// <param name="canvas">パネルを配置する Canvas</param>
        /// <returns>生成したパネルのルート GameObject</returns>
        public static GameObject BuildVolumeSettingsPanel(Canvas canvas)
        {
            var resources = GetResources();

            // 画面中央に固定サイズで置き、子要素は縦に並べる
            var panel = CreatePanel(canvas, resources, "VolumeSettings", new Vector2(480f, 368f));

            // View の各フィールドに「ラベル + スライダー」の行を割り当てる
            var view = panel.AddComponent<VolumeSettingsView>();
            var so = new SerializedObject(view);
            Assign(so, VolumeFields.Master, CreateRow(panel.transform, "Master", resources));
            Assign(so, VolumeFields.Bgm, CreateRow(panel.transform, "BGM", resources));
            Assign(so, VolumeFields.Se, CreateRow(panel.transform, "SE", resources));
            Assign(so, VolumeFields.Ambient, CreateRow(panel.transform, "Ambient", resources));
            Assign(so, VolumeFields.Voice, CreateRow(panel.transform, "Voice", resources));
            Assign(so, VolumeFields.Mute, CreateToggle(panel.transform, "Mute", resources));
            so.ApplyModifiedPropertiesWithoutUndo();
            AddNavigationHelpers(panel);
            return panel;
        }

        /// <summary>Hierarchy の右クリックメニューからグラフィック設定パネルを作る。Undo に対応。</summary>
        [MenuItem("GameObject/Abubu/Graphics Settings Panel", priority = 11)]
        private static void CreateGraphicsSettings(MenuCommand command)
        {
            if (!IsFirstInvocation(command)) return;
            var panel = BuildGraphicsSettingsPanel(FindOrCreateCanvas(command.context as GameObject));
            Undo.RegisterCreatedObjectUndo(panel, "Create Graphics Settings");
            Selection.activeGameObject = panel;
        }

        /// <summary>
        /// 解像度 / 表示モード / 画質 / フレームレートのドロップダウンと VSync トグルを持つパネルを生成し、
        /// <see cref="GraphicsSettingsView"/> に各 UI を結線する。
        /// ドロップダウンの選択肢は実行時に GraphicsSettingsPresenter が埋める。
        /// </summary>
        /// <param name="canvas">パネルを配置する Canvas</param>
        /// <returns>生成したパネルのルート GameObject</returns>
        public static GameObject BuildGraphicsSettingsPanel(Canvas canvas)
        {
            var resources = GetResources();

            var panel = CreatePanel(canvas, resources, "GraphicsSettings", new Vector2(560f, 380f));
            var view = panel.AddComponent<GraphicsSettingsView>();
            var so = new SerializedObject(view);
            // 表示ラベルは英語。選択肢の文字列は View の Labels (既定は英語) で変更できる
            Assign(so, GraphicsFields.Resolution, CreateLabeled(panel.transform, "Resolution", DefaultControls.CreateDropdown(resources), resources));
            Assign(so, GraphicsFields.WindowMode, CreateLabeled(panel.transform, "Window Mode", DefaultControls.CreateDropdown(resources), resources));
            Assign(so, GraphicsFields.Quality, CreateLabeled(panel.transform, "Quality", DefaultControls.CreateDropdown(resources), resources));
            Assign(so, GraphicsFields.FrameRate, CreateLabeled(panel.transform, "Frame Rate", DefaultControls.CreateDropdown(resources), resources));
            Assign(so, GraphicsFields.VSync, CreateToggle(panel.transform, "VSync", resources));
            so.ApplyModifiedPropertiesWithoutUndo();
            AddNavigationHelpers(panel);
            return panel;
        }

        /// <summary>
        /// メニューが複数選択で選択数ぶん呼ばれたとき、最初の 1 回だけ true を返す (パネルの重複生成を防ぐ)。
        /// </summary>
        private static bool IsFirstInvocation(MenuCommand command)
        {
            var selected = Selection.objects;
            return command.context == null || selected.Length <= 1 || command.context == selected[0];
        }

        /// <summary>
        /// SerializedObject のフィールドに参照を割り当てる。フィールドが見つからなければエラーログを出す
        /// (View のフィールド名を変えたのにここを直し忘れた場合に気づけるように)。
        /// </summary>
        private static void Assign(SerializedObject so, string fieldName, Object value)
        {
            var property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[Abubu] {so.targetObject.GetType().Name} にフィールド \"{fieldName}\" が見つかりません。AbubuUiMenu のフィールド名定数を更新してください。");
                return;
            }
            property.objectReferenceValue = value;
        }

        /// <summary>ラベル付きのトグルを作る</summary>
        private static Toggle CreateToggle(Transform parent, string label, DefaultControls.Resources resources)
        {
            var toggleGo = DefaultControls.CreateToggle(resources);
            toggleGo.name = label;
            toggleGo.transform.SetParent(parent, false);
            toggleGo.GetComponentInChildren<Text>().text = label;
            toggleGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            var toggle = toggleGo.GetComponent<Toggle>();
            toggle.isOn = false;
            return toggle;
        }

        /// <summary>
        /// 画面中央に固定サイズで置く、縦並びレイアウトのパネルを生成する。
        /// </summary>
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

        /// <summary>
        /// 「ラベル + 任意のコントロール」の行を作り、コントロールから <typeparamref name="T"/> を取り出して返す。
        /// </summary>
        private static T CreateLabeled<T>(Transform parent, string label, GameObject control, DefaultControls.Resources resources) where T : Component =>
            CreateLabeledRow(parent, label, control, resources).GetComponent<T>();

        /// <summary><see cref="CreateLabeled{T}"/> の Dropdown 版の省略形。</summary>
        private static Dropdown CreateLabeled(Transform parent, string label, GameObject control, DefaultControls.Resources resources) =>
            CreateLabeled<Dropdown>(parent, label, control, resources);

        /// <summary>
        /// 横並びの行 (左: 幅固定のラベル / 右: 残り幅いっぱいのコントロール) を作る。
        /// </summary>
        /// <returns>行の中に移動した <paramref name="control"/> 自身</returns>
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

            // flexibleWidth = 1 で残りの幅をすべてコントロールに割り当てる
            control.transform.SetParent(row.transform, false);
            control.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return control;
        }

        /// <summary>
        /// 音量設定用の「ラベル + スライダー」行を作る。
        /// </summary>
        /// <returns>生成したスライダー</returns>
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

        /// <summary>
        /// パネルの配置先 Canvas を決める (<see cref="FindOrCreateCanvas(GameObject)"/> を選択中のオブジェクトで呼ぶ)。
        /// </summary>
        public static Canvas FindOrCreateCanvas() => FindOrCreateCanvas(Selection.activeGameObject);

        /// <summary>
        /// パネルの配置先 Canvas を決める。
        /// 優先順: <paramref name="context"/> の親 Canvas → (Prefab 編集中なら) Prefab 内の Canvas → シーン内の画面用 Canvas
        /// (World Space 以外のルート Canvas) → 新規作成。
        /// シーンに EventSystem が無ければ (Canvas が既にあっても) 一緒に作る。Prefab 編集中は EventSystem を作らない。
        /// </summary>
        /// <param name="context">基準にするオブジェクト (右クリックしたオブジェクトなど)。null 可</param>
        public static Canvas FindOrCreateCanvas(GameObject context)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

            if (context != null && (prefabStage == null || prefabStage.IsPartOfPrefabContents(context)))
            {
                var selected = context.GetComponentInParent<Canvas>();
                if (selected != null) return selected;
            }

            if (prefabStage != null)
            {
                var root = prefabStage.prefabContentsRoot;
                var inPrefab = FindScreenCanvas(root.GetComponentsInChildren<Canvas>(true));
                if (inPrefab != null) return inPrefab;
                // Prefab 内に Canvas が無ければ、Prefab のルートの下に作る
                var created = CreateCanvas();
                GameObjectUtility.SetParentAndAlign(created.gameObject, root);
                return created;
            }

            EnsureEventSystem();
            var canvas = FindScreenCanvas(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            return canvas != null ? canvas : CreateCanvas();
        }

        /// <summary>画面に重ねる (World Space 以外の) ルート Canvas を探す。無ければ null</summary>
        private static Canvas FindScreenCanvas(Canvas[] canvases)
        {
            Canvas fallback = null;
            foreach (var canvas in canvases)
            {
                if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) continue;
                if (canvas.isRootCanvas) return canvas;
                fallback ??= canvas;
            }
            return fallback;
        }

        /// <summary>1920x1080 基準で画面サイズに合わせて拡縮する Canvas を作る (Undo 対応)</summary>
        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            StageUtility.PlaceGameObjectInCurrentStage(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            return canvas;
        }

        /// <summary>
        /// シーンに EventSystem が無ければ作る (EventSystem が無いと UI がクリック・ゲームパッドに反応しない)。
        /// Input System があれば InputSystemUIInputModule を付け、プロジェクト共通の Input Actions (InputSystem.actions) に
        /// UI マップがあればそれを使う (キー割り当ての変更が UI 操作にも効くように)。無ければ StandaloneInputModule。
        /// </summary>
        /// <returns>既存または作成した EventSystem</returns>
        public static EventSystem EnsureEventSystem()
        {
            var existing = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var go = new GameObject("EventSystem", typeof(EventSystem));
            var moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                var module = go.AddComponent(moduleType);
                AssignProjectWideActions(module);
            }
            else
            {
                go.AddComponent<StandaloneInputModule>();
            }
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            return go.GetComponent<EventSystem>();
        }

        /// <summary>
        /// InputSystemUIInputModule.actionsAsset に InputSystem.actions (Project-wide Actions) を設定する。
        /// UI マップが無い場合は、モジュール既定のアクション (DefaultInputActions) のままにする。
        /// Abubu.Editor は Input System に依存しないので、リフレクションで設定する。
        /// </summary>
        private static void AssignProjectWideActions(Component module)
        {
            var inputSystem = System.Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
            var actions = inputSystem?.GetProperty("actions", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as ScriptableObject;
            if (actions == null) return;

            var findMap = actions.GetType().GetMethod("FindActionMap", new[] { typeof(string), typeof(bool) });
            if (findMap == null || findMap.Invoke(actions, new object[] { "UI", false }) == null) return;

            var property = module.GetType().GetProperty("actionsAsset", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite) return;
            property.SetValue(module, actions);
            EditorUtility.SetDirty(module);
        }

        /// <summary>
        /// ゲームパッド・キーボード操作用の部品をパネルに付ける: 表示時に最初の項目を選択する UiInitialSelection と、
        /// ScrollRect があれば選択した項目までスクロールする UiScrollToSelection
        /// </summary>
        public static void AddNavigationHelpers(GameObject panel)
        {
            if (panel == null) return;
            if (panel.GetComponent<UiInitialSelection>() == null) panel.AddComponent<UiInitialSelection>();
            foreach (var scroll in panel.GetComponentsInChildren<ScrollRect>(true))
            {
                if (scroll.GetComponent<UiScrollToSelection>() == null) scroll.gameObject.AddComponent<UiScrollToSelection>();
            }
        }

        /// <summary>
        /// uGUI 標準の見た目 (UI/Skin 配下の組み込みスプライト) を DefaultControls 用にまとめて返す。
        /// </summary>
        public static DefaultControls.Resources GetResources() => new()
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
