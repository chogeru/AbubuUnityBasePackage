using Abubu.Audio;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Presentation
{
    /// <summary>
    /// 音量設定画面の View。使わないスライダーは未設定のままで良い。
    /// Canvas 上のオブジェクトに付けてスライダーを割り当てるだけで動作する。
    /// </summary>
    public sealed class VolumeSettingsView : ViewBase<VolumeSettingsPresenter>
    {
        [SerializeField] private Slider master;
        [SerializeField] private Slider bgm;
        [SerializeField] private Slider se;
        [SerializeField] private Slider ambient;
        [SerializeField] private Toggle mute;

        [Tooltip("SE スライダーを離したときに鳴らす確認用 SE のキー (任意)")]
        [SerializeField] private string sePreviewKey;

        public Slider Master => master;
        public Slider Bgm => bgm;
        public Slider Se => se;
        public Slider Ambient => ambient;
        public Toggle Mute => mute;
        public string SePreviewKey => sePreviewKey;
    }

    public sealed class VolumeSettingsPresenter : PresenterBase
    {
        private readonly AudioVolumeModel _model;
        private readonly VolumeSettingsView _view;
        private readonly ISePlayer _se;

        public VolumeSettingsPresenter(AudioVolumeModel model, VolumeSettingsView view, ISePlayer se)
        {
            _model = model;
            _view = view;
            _se = se;
        }

        protected override void OnInitialize()
        {
            Bind(_view.Master, _model.Master);
            Bind(_view.Bgm, _model.Bgm);
            Bind(_view.Se, _model.Se);
            Bind(_view.Ambient, _model.Ambient);

            if (_view.Mute != null)
            {
                _model.Mute.Subscribe(_view.Mute, static (v, toggle) => toggle.SetIsOnWithoutNotify(v)).AddTo(Disposables);
                _view.Mute.OnValueChangedAsObservable().Subscribe(_model.Mute, static (v, m) => m.Value = v).AddTo(Disposables);
            }

            if (_view.Se != null && !string.IsNullOrEmpty(_view.SePreviewKey))
            {
                _model.Se.Skip(1)
                    .Debounce(System.TimeSpan.FromSeconds(0.15))
                    .Subscribe(this, static (_, self) => self._se.Play(self._view.SePreviewKey))
                    .AddTo(Disposables);
            }
        }

        // Model → View / View → Model の双方向バインド
        private void Bind(Slider slider, ReactiveProperty<float> property)
        {
            if (slider == null) return;

            slider.minValue = 0f;
            slider.maxValue = 1f;
            property.Subscribe(slider, static (v, s) => s.SetValueWithoutNotify(v)).AddTo(Disposables);
            slider.OnValueChangedAsObservable().Subscribe(property, static (v, p) => p.Value = v).AddTo(Disposables);
        }
    }
}
