using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Presentation
{
    /// <summary>
    /// 画面設定画面の uGUI 実装。使わない項目は未設定のままで良い (WebGL 用に解像度を省く、など)。
    /// GameObject > Abubu > Graphics Settings Panel で自動生成できる。
    /// </summary>
    /// <remarks>
    /// 選択肢の表示文字列は Labels で変更できる (既定は英語)。
    /// TextMeshPro や UI Toolkit で作る場合は <see cref="IGraphicsSettingsView"/> を実装し、
    /// <c>ViewBase&lt;GraphicsSettingsPresenter&gt;</c> を継承すれば同じ Presenter がそのまま使える。
    /// </remarks>
    public sealed class GraphicsSettingsView : ViewBase<GraphicsSettingsPresenter>, IGraphicsSettingsView
    {
        [SerializeField] private Dropdown resolution;
        [SerializeField] private Dropdown windowMode;
        [SerializeField] private Dropdown quality;
        [SerializeField] private Toggle vSync;
        [SerializeField] private Dropdown frameRate;

        [Tooltip("選択肢の表示文字列。コンテナに IGraphicsSettingsLabels がバインドされている場合はそちらが優先される")]
        [SerializeField] private GraphicsSettingsLabels labels = new();

        public Dropdown Resolution => resolution;
        public Dropdown WindowMode => windowMode;
        public Dropdown Quality => quality;
        public Toggle VSync => vSync;
        public Dropdown FrameRate => frameRate;

        /// <inheritdoc />
        public IGraphicsSettingsLabels Labels => labels;

        /// <inheritdoc />
        public bool HasVSync => vSync != null;

        /// <inheritdoc />
        public Observable<bool> OnVSyncChanged => vSync != null ? vSync.onValueChanged.AsObservable() : Observable.Empty<bool>();

        /// <inheritdoc />
        public bool HasSelector(GraphicsSettingsField field) => GetDropdown(field) != null;

        /// <inheritdoc />
        public void SetSelectorVisible(GraphicsSettingsField field, bool visible)
        {
            var dropdown = GetDropdown(field);
            if (dropdown != null) dropdown.gameObject.SetActive(visible);
        }

        /// <inheritdoc />
        public void SetOptions(GraphicsSettingsField field, IReadOnlyList<string> options)
        {
            var dropdown = GetDropdown(field);
            if (dropdown == null) return;
            dropdown.ClearOptions();
            dropdown.AddOptions(options.ToList());
        }

        /// <inheritdoc />
        public void SetSelectedIndex(GraphicsSettingsField field, int index)
        {
            var dropdown = GetDropdown(field);
            if (dropdown != null) dropdown.SetValueWithoutNotify(index);
        }

        /// <inheritdoc />
        public Observable<int> OnSelected(GraphicsSettingsField field)
        {
            var dropdown = GetDropdown(field);
            return dropdown != null ? dropdown.onValueChanged.AsObservable() : Observable.Empty<int>();
        }

        /// <inheritdoc />
        public void SetVSync(bool enabled)
        {
            if (vSync != null) vSync.SetIsOnWithoutNotify(enabled);
        }

        private Dropdown GetDropdown(GraphicsSettingsField field) => field switch
        {
            GraphicsSettingsField.Resolution => resolution,
            GraphicsSettingsField.WindowMode => windowMode,
            GraphicsSettingsField.Quality => quality,
            GraphicsSettingsField.FrameRate => frameRate,
            _ => null,
        };
    }
}
