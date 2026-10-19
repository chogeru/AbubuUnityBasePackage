using System;
using System.Collections.Generic;
using System.Linq;
using Abubu.DisplaySettings;
using R3;

namespace Abubu.Presentation
{
    /// <summary>
    /// <see cref="GraphicsSettingsModel"/> と <see cref="IGraphicsSettingsView"/> を双方向に結線する Presenter。
    /// 表示文字列は持たず、<see cref="IGraphicsSettingsLabels"/> に任せる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 表示文字列の決め方 (優先順): コンテナにバインドされた <see cref="IGraphicsSettingsLabels"/>
    /// → View の <see cref="IGraphicsSettingsView.Labels"/> → 英語の既定 (<see cref="GraphicsSettingsLabels"/>)。
    /// </para>
    /// <para>Model が拒否した値 (範囲外など) を選ばれた場合は、View の選択を現在値に戻す。</para>
    /// </remarks>
    public class GraphicsSettingsPresenter : PresenterBase
    {
        protected GraphicsSettingsModel Model { get; }
        protected IGraphicsSettingsView View { get; }
        protected IGraphicsSettingsLabels Labels { get; }

        /// <param name="model">画面設定の Model</param>
        /// <param name="view">表示先</param>
        /// <param name="labels">表示文字列 (任意。ローカライズ用に差し替える)</param>
        public GraphicsSettingsPresenter(GraphicsSettingsModel model, IGraphicsSettingsView view, IGraphicsSettingsLabels labels = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            View = view ?? throw new ArgumentNullException(nameof(view));
            Labels = labels ?? view.Labels ?? new GraphicsSettingsLabels();
        }

        protected override void OnInitialize()
        {
            if (Model.SupportsResolution)
            {
                BindSelector(GraphicsSettingsField.Resolution, Model.Resolution, () => Model.ResolutionOptions,
                    Labels.Resolution, Model.SetResolution);
                BindSelector(GraphicsSettingsField.WindowMode, Model.FullScreenMode, () => Model.WindowModeOptions,
                    Labels.WindowMode, Model.SetFullScreenMode);
            }
            else
            {
                // WebGL / モバイルでは変更できないので隠す
                if (View.HasSelector(GraphicsSettingsField.Resolution)) View.SetSelectorVisible(GraphicsSettingsField.Resolution, false);
                if (View.HasSelector(GraphicsSettingsField.WindowMode)) View.SetSelectorVisible(GraphicsSettingsField.WindowMode, false);
            }

            IReadOnlyList<int> qualityLevels = Enumerable.Range(0, Model.QualityNames.Count).ToArray();
            BindSelector(GraphicsSettingsField.Quality, Model.QualityLevel, () => qualityLevels,
                i => Labels.Quality(i, Model.QualityNames[i]), Model.SetQualityLevel);
            BindSelector(GraphicsSettingsField.FrameRate, Model.TargetFrameRate, () => Model.FrameRateOptions,
                Labels.FrameRate, Model.SetTargetFrameRate);

            if (View.HasVSync)
            {
                Model.VSync.Subscribe(View, static (v, view) => view.SetVSync(v)).AddTo(Disposables);
                View.OnVSyncChanged.Subscribe(Model, static (v, model) => model.SetVSync(v)).AddTo(Disposables);
            }
        }

        /// <summary>
        /// 選択式の項目を双方向に結線する。
        /// 選択肢のリストが差し替わった (Model が値を追加した) ときは View の選択肢も作り直す。
        /// </summary>
        /// <param name="field">View 側の項目</param>
        /// <param name="property">Model の値</param>
        /// <param name="getOptions">現在の選択肢 (参照が変わったら作り直す)</param>
        /// <param name="label">選択肢を表示文字列にする</param>
        /// <param name="set">Model に値を設定する。拒否されたら false</param>
        protected void BindSelector<T>(GraphicsSettingsField field, ReadOnlyReactiveProperty<T> property,
            Func<IReadOnlyList<T>> getOptions, Func<T, string> label, Func<T, bool> set)
        {
            if (!View.HasSelector(field)) return;

            IReadOnlyList<T> rendered = null;

            property.Subscribe(value =>
            {
                var options = getOptions();
                if (!ReferenceEquals(options, rendered))
                {
                    rendered = options;
                    View.SetOptions(field, options.Select(label).ToArray());
                }

                var index = IndexOf(options, value);
                if (index >= 0) View.SetSelectedIndex(field, index);
            }).AddTo(Disposables);

            View.OnSelected(field).Subscribe(index =>
            {
                if (rendered == null || index < 0 || index >= rendered.Count) return;
                if (set(rendered[index])) return;

                // 拒否された: 表示を現在値に戻す
                var current = IndexOf(rendered, property.CurrentValue);
                if (current >= 0) View.SetSelectedIndex(field, current);
            }).AddTo(Disposables);
        }

        private static int IndexOf<T>(IReadOnlyList<T> values, T value)
        {
            var comparer = EqualityComparer<T>.Default;
            for (var i = 0; i < values.Count; i++)
            {
                if (comparer.Equals(values[i], value)) return i;
            }
            return -1;
        }
    }
}
