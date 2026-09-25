using System.Collections.Generic;
using System.Linq;
using Abubu.Graphics;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Presentation
{
    /// <summary>
    /// 画面設定の View。使わない項目は未設定のままで良い (WebGL 用に解像度を省く、など)。
    /// GameObject > Abubu > Graphics Settings Panel で自動生成できる。
    /// </summary>
    public sealed class GraphicsSettingsView : ViewBase<GraphicsSettingsPresenter>
    {
        [SerializeField] private Dropdown resolution;
        [SerializeField] private Dropdown windowMode;
        [SerializeField] private Dropdown quality;
        [SerializeField] private Toggle vSync;
        [SerializeField] private Dropdown frameRate;

        public Dropdown Resolution => resolution;
        public Dropdown WindowMode => windowMode;
        public Dropdown Quality => quality;
        public Toggle VSync => vSync;
        public Dropdown FrameRate => frameRate;
    }

    public sealed class GraphicsSettingsPresenter : PresenterBase
    {
        private static readonly (FullScreenMode mode, string label)[] WindowModes =
        {
            (FullScreenMode.FullScreenWindow, "フルスクリーン"),
            (FullScreenMode.MaximizedWindow, "ボーダーレス"),
            (FullScreenMode.Windowed, "ウィンドウ"),
        };

        private readonly GraphicsSettingsModel _model;
        private readonly GraphicsSettingsView _view;

        public GraphicsSettingsPresenter(GraphicsSettingsModel model, GraphicsSettingsView view)
        {
            _model = model;
            _view = view;
        }

        protected override void OnInitialize()
        {
            if (_model.SupportsResolution)
            {
                BindDropdown(_view.Resolution, _model.Resolution, _model.Resolutions, r => $"{r.x} x {r.y}");
                BindDropdown(_view.WindowMode, _model.FullScreenMode, WindowModes.Select(w => w.mode).ToArray(),
                    m => WindowModes.First(w => w.mode == m).label);
            }
            else
            {
                if (_view.Resolution != null) _view.Resolution.gameObject.SetActive(false);
                if (_view.WindowMode != null) _view.WindowMode.gameObject.SetActive(false);
            }

            BindDropdown(_view.Quality, _model.QualityLevel, Enumerable.Range(0, _model.QualityNames.Count).ToArray(),
                i => _model.QualityNames[i]);
            BindDropdown(_view.FrameRate, _model.TargetFrameRate, GraphicsSettingsModel.FrameRateOptions,
                f => f < 0 ? "無制限" : $"{f} FPS");

            if (_view.VSync != null)
            {
                _model.VSync.Subscribe(_view.VSync, static (v, t) => t.SetIsOnWithoutNotify(v)).AddTo(Disposables);
                _view.VSync.OnValueChangedAsObservable().Subscribe(_model.VSync, static (v, p) => p.Value = v).AddTo(Disposables);
            }
        }

        /// <summary>選択肢リストと ReactiveProperty を Dropdown に双方向バインドする</summary>
        private void BindDropdown<T>(Dropdown dropdown, ReactiveProperty<T> property, IReadOnlyList<T> values, System.Func<T, string> label)
        {
            if (dropdown == null) return;

            dropdown.ClearOptions();
            dropdown.AddOptions(values.Select(label).ToList());

            property.Subscribe(v =>
            {
                var index = IndexOf(values, v);
                if (index >= 0) dropdown.SetValueWithoutNotify(index);
            }).AddTo(Disposables);

            dropdown.onValueChanged.AsObservable()
                .Subscribe(i => property.Value = values[i])
                .AddTo(Disposables);
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
