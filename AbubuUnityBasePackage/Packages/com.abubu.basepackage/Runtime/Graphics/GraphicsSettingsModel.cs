using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;

namespace Abubu.DisplaySettings
{
    /// <summary>
    /// 解像度・表示モード・画質・VSync・目標フレームレートの Model (状態のみ)。
    /// Unity への反映は <see cref="GraphicsSettingsApplier"/>、保存は <see cref="GraphicsSettingsStore"/> が担当する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 初期値は実行環境の現在値 (<see cref="GraphicsPlatformInfo"/>) で、この時点では「未設定」扱い。
    /// Set* で変更されるか、保存データから <see cref="Restore"/> された項目だけが「明示的に設定済み」になり、
    /// Applier はその項目だけを Unity に反映する (プロジェクト側の設定を勝手に上書きしない)。
    /// </para>
    /// <para>
    /// 各選択肢 (*Options) には現在値が必ず含まれる。選択肢に無い値が設定されると、その値を追加した新しいリストに差し替わる
    /// (参照が変わるので、View 側は参照比較で選択肢の作り直しを判定できる)。
    /// </para>
    /// </remarks>
    public sealed class GraphicsSettingsModel : IDisposable
    {
        private readonly ReactiveProperty<Vector2Int> _resolution;
        private readonly ReactiveProperty<FullScreenMode> _fullScreenMode;
        private readonly ReactiveProperty<int> _qualityLevel;
        private readonly ReactiveProperty<bool> _vSync;
        private readonly ReactiveProperty<int> _targetFrameRate;
        private readonly Subject<GraphicsSettingKind> _changed = new();
        private readonly Subject<Unit> _saveRequested = new();
        private int _explicitMask;

        /// <param name="options">選択肢などの構成。null なら既定値</param>
        /// <param name="platform">実行環境の情報。null なら <see cref="GraphicsPlatformInfo.Capture"/> の結果を使う</param>
        public GraphicsSettingsModel(GraphicsSettingsOptions options = null, GraphicsPlatformInfo platform = null)
        {
            Options = options ?? new GraphicsSettingsOptions();
            platform ??= GraphicsPlatformInfo.Capture();

            SupportsResolution = platform.SupportsResolution;
            QualityNames = platform.QualityNames ?? Array.Empty<string>();

            ResolutionOptions = (platform.Resolutions ?? Array.Empty<Vector2Int>()).Where(IsValidResolution).Distinct().ToArray();
            WindowModeOptions = (Options.WindowModes ?? new List<FullScreenMode>()).Where(IsDefinedMode).Distinct().ToArray();
            FrameRateOptions = (Options.FrameRateOptions ?? new List<int>()).Where(IsValidFrameRate).Distinct().ToArray();

            var quality = platform.CurrentQualityLevel;
            if (quality < 0 || quality >= QualityNames.Count) quality = 0;
            var frameRate = IsValidFrameRate(platform.CurrentTargetFrameRate) ? platform.CurrentTargetFrameRate : -1;

            _resolution = new ReactiveProperty<Vector2Int>(platform.CurrentResolution);
            _fullScreenMode = new ReactiveProperty<FullScreenMode>(platform.CurrentFullScreenMode);
            _qualityLevel = new ReactiveProperty<int>(quality);
            _vSync = new ReactiveProperty<bool>(platform.CurrentVSync);
            _targetFrameRate = new ReactiveProperty<int>(frameRate);

            // 現在値が選択肢に無ければ追加しておく (ドロップダウンの表示と値がずれないように)
            if (SupportsResolution && IsValidResolution(platform.CurrentResolution)) EnsureResolutionOption(platform.CurrentResolution);
            if (SupportsResolution && IsDefinedMode(platform.CurrentFullScreenMode)) EnsureWindowModeOption(platform.CurrentFullScreenMode);
            EnsureFrameRateOption(frameRate);
        }

        /// <summary>この Model の構成</summary>
        public GraphicsSettingsOptions Options { get; }

        /// <summary>解像度・表示モードを変更できるか (WebGL / モバイルは不可)</summary>
        public bool SupportsResolution { get; }

        /// <summary>解像度の選択肢 (面積の小さい順)。現在値を必ず含む</summary>
        public IReadOnlyList<Vector2Int> ResolutionOptions { get; private set; }

        /// <summary>表示モードの選択肢。現在値を必ず含む</summary>
        public IReadOnlyList<FullScreenMode> WindowModeOptions { get; private set; }

        /// <summary>画質レベルの名前。インデックスがそのまま画質レベル</summary>
        public IReadOnlyList<string> QualityNames { get; }

        /// <summary>目標フレームレートの選択肢 (-1 = プラットフォーム既定)。現在値を必ず含む</summary>
        public IReadOnlyList<int> FrameRateOptions { get; private set; }

        public ReadOnlyReactiveProperty<Vector2Int> Resolution => _resolution;
        public ReadOnlyReactiveProperty<FullScreenMode> FullScreenMode => _fullScreenMode;
        public ReadOnlyReactiveProperty<int> QualityLevel => _qualityLevel;
        public ReadOnlyReactiveProperty<bool> VSync => _vSync;
        public ReadOnlyReactiveProperty<int> TargetFrameRate => _targetFrameRate;

        /// <summary>
        /// 項目が明示的に設定された (Set* / <see cref="Restore"/>) ときに、その項目を通知する。
        /// 同じ値の再設定でも、未設定 → 設定済みに変わった場合は通知する。
        /// </summary>
        public Observable<GraphicsSettingKind> Changed => _changed;

        /// <summary><see cref="RequestSave"/> が呼ばれたことの通知。保存担当 (<see cref="GraphicsSettingsStore"/>) が購読する</summary>
        public Observable<Unit> SaveRequested => _saveRequested;

        /// <summary>項目が明示的に設定済みか (未設定なら Unity には反映されない)</summary>
        public bool IsExplicit(GraphicsSettingKind kind) => (_explicitMask & Bit(kind)) != 0;

        /// <summary>解像度を設定する。変更できない環境や 0 以下のサイズなら false</summary>
        public bool SetResolution(Vector2Int resolution)
        {
            if (!SupportsResolution || !IsValidResolution(resolution)) return false;
            EnsureResolutionOption(resolution);
            Commit(GraphicsSettingKind.Resolution, _resolution, resolution);
            return true;
        }

        /// <summary>表示モードを設定する。変更できない環境や未定義の値なら false</summary>
        public bool SetFullScreenMode(FullScreenMode mode)
        {
            if (!SupportsResolution || !IsDefinedMode(mode)) return false;
            EnsureWindowModeOption(mode);
            Commit(GraphicsSettingKind.FullScreenMode, _fullScreenMode, mode);
            return true;
        }

        /// <summary>画質レベルを設定する。範囲外なら false</summary>
        public bool SetQualityLevel(int level)
        {
            if (level < 0 || level >= QualityNames.Count) return false;
            Commit(GraphicsSettingKind.QualityLevel, _qualityLevel, level);
            return true;
        }

        /// <summary>VSync を設定する</summary>
        public bool SetVSync(bool enabled)
        {
            Commit(GraphicsSettingKind.VSync, _vSync, enabled);
            return true;
        }

        /// <summary>目標フレームレートを設定する。-1 (既定) か 1 以上でなければ false</summary>
        public bool SetTargetFrameRate(int frameRate)
        {
            if (!IsValidFrameRate(frameRate)) return false;
            EnsureFrameRateOption(frameRate);
            Commit(GraphicsSettingKind.TargetFrameRate, _targetFrameRate, frameRate);
            return true;
        }

        /// <summary>
        /// 保存データから、記録されている項目だけを復元する。不正な値の項目は無視する (未設定のまま)。
        /// </summary>
        public void Restore(GraphicsSettingsData data)
        {
            if (data == null) return;
            if (data.Has(GraphicsSettingKind.Resolution)) SetResolution(new Vector2Int(data.Width, data.Height));
            if (data.Has(GraphicsSettingKind.FullScreenMode)) SetFullScreenMode(data.FullScreenMode);
            if (data.Has(GraphicsSettingKind.QualityLevel)) SetQualityLevel(data.QualityLevel);
            if (data.Has(GraphicsSettingKind.VSync)) SetVSync(data.VSync);
            if (data.Has(GraphicsSettingKind.TargetFrameRate)) SetTargetFrameRate(data.TargetFrameRate);
        }

        /// <summary>現在の状態を保存形式にする (明示的に設定された項目のマスク付き)</summary>
        public GraphicsSettingsData ToData() => new()
        {
            Width = _resolution.Value.x,
            Height = _resolution.Value.y,
            FullScreenMode = _fullScreenMode.Value,
            QualityLevel = _qualityLevel.Value,
            VSync = _vSync.Value,
            TargetFrameRate = _targetFrameRate.Value,
            ExplicitMask = _explicitMask,
        };

        /// <summary>
        /// すぐに保存してほしいことを通知する (中断時など)。Model 自体は保存しない。
        /// 保存担当が登録されていなければ何も起きない。
        /// </summary>
        public void RequestSave() => _saveRequested.OnNext(Unit.Default);

        /// <summary>旧 API。<see cref="RequestSave"/> と同じ</summary>
        [Obsolete("GraphicsSettingsModel は保存しなくなりました。RequestSave() か GraphicsSettingsStore.Save() を使ってください。")]
        public void Save() => RequestSave();

        public void Dispose()
        {
            _changed.Dispose();
            _saveRequested.Dispose();
            _resolution.Dispose();
            _fullScreenMode.Dispose();
            _qualityLevel.Dispose();
            _vSync.Dispose();
            _targetFrameRate.Dispose();
        }

        private void Commit<T>(GraphicsSettingKind kind, ReactiveProperty<T> property, T value)
        {
            var wasExplicit = IsExplicit(kind);
            var changed = !EqualityComparer<T>.Default.Equals(property.Value, value);
            _explicitMask |= Bit(kind);
            property.Value = value;
            if (changed || !wasExplicit) _changed.OnNext(kind);
        }

        private void EnsureResolutionOption(Vector2Int value)
        {
            if (ResolutionOptions.Contains(value)) return;
            ResolutionOptions = ResolutionOptions.Append(value).OrderBy(r => r.x * r.y).ThenBy(r => r.x).ToArray();
        }

        private void EnsureWindowModeOption(FullScreenMode value)
        {
            if (WindowModeOptions.Contains(value)) return;
            WindowModeOptions = WindowModeOptions.Append(value).ToArray();
        }

        private void EnsureFrameRateOption(int value)
        {
            if (FrameRateOptions.Contains(value)) return;
            // -1 (既定) は末尾、それ以外は昇順の位置に入れる
            FrameRateOptions = FrameRateOptions.Append(value)
                .OrderBy(f => f < 0 ? int.MaxValue : f)
                .ToArray();
        }

        private static int Bit(GraphicsSettingKind kind) => 1 << (int)kind;
        private static bool IsValidResolution(Vector2Int r) => r.x > 0 && r.y > 0;
        private static bool IsDefinedMode(FullScreenMode mode) => Enum.IsDefined(typeof(FullScreenMode), mode);
        private static bool IsValidFrameRate(int f) => f == -1 || f > 0;
    }
}
