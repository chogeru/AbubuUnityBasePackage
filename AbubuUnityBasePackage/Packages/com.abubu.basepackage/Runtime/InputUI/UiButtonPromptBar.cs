using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// 画面の下などに「Ⓐ 決定  Ⓑ 戻る」のようなボタン操作の案内を横に並べる。アイコンは使用中のデバイス
    /// (キーボード / Xbox / PlayStation / Switch ...) と割り当てに合わせて自動で切り替わる。
    /// <code>
    /// // Canvas 上の空のオブジェクトに付け、Default Prompts に "UI/Submit" + "OK" などを並べるだけ
    /// bar.SetPrompts(new[] { new ButtonPrompt("UI/Submit", "Buy"), new ButtonPrompt("UI/Cancel", "Back") }); // コードから差し替え
    /// bar.ResetToDefault();                                                                                 // 既定に戻す
    /// bar.SetSource(myPromptSource);                                                                        // 画面ごとに切り替える
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>表示する案内の優先順: <see cref="SetPrompts"/> で設定したもの → <see cref="IButtonPromptSource"/> (Inject されるか SetSource) の値 → Default Prompts</item>
    /// <item>Item Prefab が未設定なら、Image + Text の簡単な項目をコードで作る (表示は Inspector で調整できない)</item>
    /// <item>割り当てが無いアクションの項目は隠す (Hide Unbound)</item>
    /// <item>IInputGlyphService は各項目の <see cref="InputGlyphImage"/> が取得する (Zenject の Inject、無ければ ProjectContext)</item>
    /// </list>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UiButtonPromptBar : MonoBehaviour
    {
        [Tooltip("既定で表示する案内 (IButtonPromptSource が null を返す間・SetPrompts をしていない間に出す)")]
        [SerializeField] private List<ButtonPrompt> defaultPrompts = new()
        {
            new ButtonPrompt("UI/Submit", "OK"),
            new ButtonPrompt("UI/Cancel", "Back"),
        };

        [Tooltip("1 項目分のプレハブ (任意)。未設定なら Image + Text の簡単な項目を作る")]
        [SerializeField] private UiButtonPromptItem itemPrefab;
        [Tooltip("項目を並べる親 (未設定ならこのオブジェクト。HorizontalLayoutGroup が無ければ追加する)")]
        [SerializeField] private Transform container;
        [Tooltip("割り当てが無いアクションの項目を隠す")]
        [SerializeField] private bool hideUnbound = true;

        [Header("Item Prefab が未設定のときの見た目")]
        [SerializeField] private float iconSize = 40f;
        [SerializeField] private int fontSize = 24;
        [SerializeField] private Color textColor = Color.white;

        private readonly List<UiButtonPromptItem> _items = new();
        private IButtonPromptSource _source;
        private IDisposable _sourceSubscription;
        private IReadOnlyList<ButtonPrompt> _override;
        private bool _started;

        /// <summary>既定の案内 (Start より前、または変更後に <see cref="Rebuild"/> を呼ぶ)</summary>
        public List<ButtonPrompt> DefaultPrompts => defaultPrompts;

        /// <summary>表示中の項目</summary>
        public IReadOnlyList<UiButtonPromptItem> Items => _items;

        [Inject]
        private void InjectSource([InjectOptional] IButtonPromptSource source)
        {
            if (source != null) SetSource(source);
        }

        private void Start()
        {
            _started = true;
            Rebuild();
        }

        private void OnDestroy()
        {
            _sourceSubscription?.Dispose();
            _sourceSubscription = null;
        }

        /// <summary>案内の供給元を設定する (null で外す)</summary>
        public void SetSource(IButtonPromptSource source)
        {
            if (ReferenceEquals(source, _source)) return;
            _sourceSubscription?.Dispose();
            _sourceSubscription = null;
            _source = source;
            // 購読時に現在値が流れて Rebuild される
            if (source != null) _sourceSubscription = source.Prompts.Subscribe(this, static (_, self) => self.Rebuild());
            else Rebuild();
        }

        /// <summary>表示する案内を直接設定する (供給元・既定より優先。null で解除)</summary>
        public void SetPrompts(IReadOnlyList<ButtonPrompt> prompts)
        {
            _override = prompts;
            Rebuild();
        }

        /// <summary><see cref="SetPrompts"/> を解除し、供給元・既定の案内に戻す</summary>
        public void ResetToDefault() => SetPrompts(null);

        /// <summary>表示を作り直す</summary>
        public void Rebuild()
        {
            if (!_started || this == null) return;
            var prompts = _override ?? _source?.Prompts.CurrentValue ?? defaultPrompts;

            var parent = container != null ? container : transform;
            EnsureLayout(parent);
            while (_items.Count < prompts.Count)
            {
                var item = itemPrefab != null ? Instantiate(itemPrefab, parent, false) : CreateDefaultItem(parent);
                if (item.Glyph != null) item.Glyph.Refreshed += OnGlyphRefreshed;
                _items.Add(item);
            }

            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var active = i < prompts.Count;
                if (item.gameObject.activeSelf != active) item.gameObject.SetActive(active);
                if (!active) continue;
                item.Set(prompts[i]);
                UpdateVisibility(item);
            }
        }

        private void OnGlyphRefreshed(InputGlyphImage glyph)
        {
            foreach (var item in _items)
            {
                if (item != null && item.Glyph == glyph) UpdateVisibility(item);
            }
        }

        private void UpdateVisibility(UiButtonPromptItem item)
        {
            item.SetHidden(hideUnbound && item.IsUnbound);

            // コードで作った項目は、アイコンが無い (文字で出す) ときにアイコンの幅を詰める
            if (itemPrefab != null || item.Glyph == null || item.Glyph.Image == null) return;
            var iconLayout = item.Glyph.GetComponent<LayoutElement>();
            if (iconLayout != null) iconLayout.ignoreLayout = !item.Glyph.Image.enabled;
        }

        private static void EnsureLayout(Transform parent)
        {
            if (parent.GetComponent<LayoutGroup>() != null) return;
            var layout = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        }

        /// <summary>Item Prefab が無いときの簡単な項目 (アイコン + 文言)</summary>
        private UiButtonPromptItem CreateDefaultItem(Transform parent)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var root = new GameObject("Prompt", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            root.transform.SetParent(parent, false);
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var iconGo = new GameObject("Glyph", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(root.transform, false);
            var iconLayout = iconGo.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = iconLayout.preferredHeight = iconSize;
            var icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            // アイコンが無いときの文字 ("Space" など)。アイコンと同じ場所に重ねる
            var fallbackGo = new GameObject("GlyphText", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            fallbackGo.transform.SetParent(root.transform, false);
            var fallback = fallbackGo.GetComponent<Text>();
            SetupText(fallback, font);
            fallback.fontStyle = FontStyle.Bold;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(root.transform, false);
            var label = labelGo.GetComponent<Text>();
            SetupText(label, font);

            var glyph = iconGo.AddComponent<InputGlyphImage>();
            glyph.Image = icon;
            glyph.FallbackText = fallback;

            var item = root.AddComponent<UiButtonPromptItem>();
            item.Glyph = glyph;
            item.Label = label;
            return item;
        }

        private void SetupText(Text text, Font font)
        {
            text.font = font;
            text.fontSize = fontSize;
            text.color = textColor;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
        }
    }
}
