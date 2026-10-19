using System;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.DisplaySettings
{
    /// <summary>
    /// <see cref="GraphicsSettingsModel"/> の値を Unity (Screen / QualitySettings / Application) に反映する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 反映するのは「明示的に設定された項目」(ユーザーが変更した、または保存済みだった項目) だけ。
    /// 初回起動時に既定値を押し付けることはしないので、プロジェクト側の設定 (モバイルの 30fps、XR の VSync など) はそのまま残る。
    /// </para>
    /// <para>
    /// 画質レベルを切り替えると Unity が vSyncCount を画質設定の値で上書きするため、
    /// VSync が明示的に設定されている場合に限り、画質変更の後に VSync を反映し直す。
    /// </para>
    /// <para>反映方法を変えたい場合は継承して Apply* をオーバーライドし、こちらの代わりにバインドする。</para>
    /// </remarks>
    public class GraphicsSettingsApplier : IInitializable, IDisposable
    {
        /// <summary>反映元の Model</summary>
        protected GraphicsSettingsModel Model { get; }

        private IDisposable _subscription;

        public GraphicsSettingsApplier(GraphicsSettingsModel model) => Model = model;

        /// <summary>明示的に設定済みの項目をまとめて反映し、以降の変更を購読する</summary>
        public void Initialize()
        {
            if (_subscription != null) return;

            // 画質 → VSync の順に反映する (画質変更で vSyncCount が上書きされるため)
            if (Model.IsExplicit(GraphicsSettingKind.QualityLevel)) Apply(GraphicsSettingKind.QualityLevel);
            else if (Model.IsExplicit(GraphicsSettingKind.VSync)) Apply(GraphicsSettingKind.VSync);
            if (Model.IsExplicit(GraphicsSettingKind.TargetFrameRate)) Apply(GraphicsSettingKind.TargetFrameRate);
            if (Model.IsExplicit(GraphicsSettingKind.Resolution) || Model.IsExplicit(GraphicsSettingKind.FullScreenMode))
            {
                Apply(GraphicsSettingKind.Resolution);
            }

            _subscription = Model.Changed.Subscribe(this, static (kind, self) => self.Apply(kind));
        }

        /// <summary>指定項目を反映する</summary>
        protected virtual void Apply(GraphicsSettingKind kind)
        {
            switch (kind)
            {
                case GraphicsSettingKind.Resolution:
                case GraphicsSettingKind.FullScreenMode:
                    // 解像度と表示モードは同時に指定する API なので、片方だけ設定済みでももう片方は現在値を渡す
                    if (Model.SupportsResolution) ApplyResolution(Model.Resolution.CurrentValue, Model.FullScreenMode.CurrentValue);
                    break;
                case GraphicsSettingKind.QualityLevel:
                    ApplyQualityLevel(Model.QualityLevel.CurrentValue);
                    if (Model.IsExplicit(GraphicsSettingKind.VSync)) ApplyVSync(Model.VSync.CurrentValue);
                    break;
                case GraphicsSettingKind.VSync:
                    ApplyVSync(Model.VSync.CurrentValue);
                    break;
                case GraphicsSettingKind.TargetFrameRate:
                    ApplyTargetFrameRate(Model.TargetFrameRate.CurrentValue);
                    break;
            }
        }

        /// <summary>解像度と表示モードを反映する</summary>
        protected virtual void ApplyResolution(Vector2Int resolution, FullScreenMode mode) =>
            Screen.SetResolution(resolution.x, resolution.y, mode);

        /// <summary>画質レベルを反映する (高コストな変更も適用する)</summary>
        protected virtual void ApplyQualityLevel(int level) => QualitySettings.SetQualityLevel(level, true);

        /// <summary>VSync を反映する (有効 = 毎フレーム同期)</summary>
        protected virtual void ApplyVSync(bool enabled) => QualitySettings.vSyncCount = enabled ? 1 : 0;

        /// <summary>目標フレームレートを反映する</summary>
        protected virtual void ApplyTargetFrameRate(int frameRate) => Application.targetFrameRate = frameRate;

        public virtual void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
