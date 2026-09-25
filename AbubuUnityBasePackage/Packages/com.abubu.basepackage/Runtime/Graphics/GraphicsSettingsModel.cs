using System;
using System.Collections.Generic;
using System.Linq;
using Abubu.Save;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Graphics
{
    [Serializable]
    public sealed class GraphicsSettingsData
    {
        public int Width;
        public int Height;
        public FullScreenMode FullScreenMode = FullScreenMode.FullScreenWindow;
        public int QualityLevel = -1;
        public bool VSync = true;
        /// <summary>-1 = プラットフォーム既定</summary>
        public int TargetFrameRate = -1;
    }

    /// <summary>
    /// 解像度・フルスクリーン・画質・VSync・目標フレームレートの Model。
    /// 値を変えると即座に反映され、ISaveService に保存される。
    /// </summary>
    public sealed class GraphicsSettingsModel : IInitializable, IDisposable
    {
        public const string SaveKey = "abubu.graphics";

        /// <summary>目標フレームレートの選択肢 (-1 = 無制限 / 既定)</summary>
        public static readonly int[] FrameRateOptions = { 30, 60, 120, 144, -1 };

        public IReadOnlyList<Vector2Int> Resolutions { get; }
        public IReadOnlyList<string> QualityNames { get; } = QualitySettings.names;

        /// <summary>WebGL / モバイルは解像度・ウィンドウモードを変更できない</summary>
        public bool SupportsResolution { get; } = !Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer;

        public ReactiveProperty<Vector2Int> Resolution { get; }
        public ReactiveProperty<FullScreenMode> FullScreenMode { get; }
        public ReactiveProperty<int> QualityLevel { get; }
        public ReactiveProperty<bool> VSync { get; }
        public ReactiveProperty<int> TargetFrameRate { get; }

        private readonly ISaveService _save;
        private readonly CompositeDisposable _disposables = new();

        public GraphicsSettingsModel(ISaveService save)
        {
            _save = save;

            Resolutions = Screen.resolutions
                .Select(r => new Vector2Int(r.width, r.height))
                .Append(new Vector2Int(Screen.width, Screen.height))
                .Distinct()
                .OrderBy(r => r.x * r.y)
                .ToArray();

            var data = save.Load(SaveKey, new GraphicsSettingsData());
            if (data.Width <= 0 || data.Height <= 0)
            {
                data.Width = Screen.width;
                data.Height = Screen.height;
                data.FullScreenMode = Screen.fullScreenMode;
            }
            if (data.QualityLevel < 0 || data.QualityLevel >= QualityNames.Count) data.QualityLevel = QualitySettings.GetQualityLevel();

            Resolution = new ReactiveProperty<Vector2Int>(new Vector2Int(data.Width, data.Height));
            FullScreenMode = new ReactiveProperty<FullScreenMode>(data.FullScreenMode);
            QualityLevel = new ReactiveProperty<int>(data.QualityLevel);
            VSync = new ReactiveProperty<bool>(data.VSync);
            TargetFrameRate = new ReactiveProperty<int>(data.TargetFrameRate);
        }

        public void Initialize()
        {
            if (SupportsResolution)
            {
                Resolution.CombineLatest(FullScreenMode, static (r, m) => (r, m))
                    .Subscribe(static x => Screen.SetResolution(x.r.x, x.r.y, x.m))
                    .AddTo(_disposables);
            }

            QualityLevel.Subscribe(static q => QualitySettings.SetQualityLevel(q, true)).AddTo(_disposables);
            // 画質レベルの切り替えで vSyncCount が上書きされるため、画質と合わせて再適用する
            VSync.CombineLatest(QualityLevel, static (v, _) => v)
                .Subscribe(static v => QualitySettings.vSyncCount = v ? 1 : 0)
                .AddTo(_disposables);
            TargetFrameRate.Subscribe(static f => Application.targetFrameRate = f).AddTo(_disposables);

            Observable.Merge(Resolution.Skip(1).AsUnitObservable(), FullScreenMode.Skip(1).AsUnitObservable(),
                    QualityLevel.Skip(1).AsUnitObservable(), VSync.Skip(1).AsUnitObservable(), TargetFrameRate.Skip(1).AsUnitObservable())
                .Debounce(TimeSpan.FromSeconds(0.5))
                .Subscribe(this, static (_, self) => self.Save())
                .AddTo(_disposables);
        }

        public void Save() => _save.Save(SaveKey, new GraphicsSettingsData
        {
            Width = Resolution.Value.x,
            Height = Resolution.Value.y,
            FullScreenMode = FullScreenMode.Value,
            QualityLevel = QualityLevel.Value,
            VSync = VSync.Value,
            TargetFrameRate = TargetFrameRate.Value,
        });

        public void Dispose()
        {
            _disposables.Dispose();
            Resolution.Dispose();
            FullScreenMode.Dispose();
            QualityLevel.Dispose();
            VSync.Dispose();
            TargetFrameRate.Dispose();
        }
    }
}
