using Abubu.Localization;
using UnityEngine;
using UnityEngine.Scripting;
using Zenject;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.Presentation.GraphicsUiLocalizationModuleInstaller))]
[assembly: AlwaysLinkAssembly]

namespace Abubu.Presentation
{
    /// <summary>
    /// 画面設定 UI の選択肢 (表示モード・FPS など) を ILocalizationService で翻訳する。
    /// テーブルにキーが無い項目は英語の既定値 (<see cref="GraphicsSettingsLabels"/>) を使う。
    /// 使うキーはパッケージ同梱の AbubuBuiltInStrings.csv を参照 (Setup Project で翻訳テーブルに追加される)。
    /// </summary>
    public sealed class LocalizedGraphicsSettingsLabels : IGraphicsSettingsLabels
    {
        public const string ResolutionKey = "abubu.graphics.resolution";
        public const string ExclusiveFullScreenKey = "abubu.graphics.window.exclusive";
        public const string FullScreenWindowKey = "abubu.graphics.window.fullscreen";
        public const string MaximizedWindowKey = "abubu.graphics.window.borderless";
        public const string WindowedKey = "abubu.graphics.window.windowed";
        public const string FrameRateKey = "abubu.graphics.fps";
        public const string UnlimitedFrameRateKey = "abubu.graphics.fps.unlimited";
        /// <summary>画質の名前 ("abubu.graphics.quality.high" など。名前は QualitySettings の名前を小文字にしたもの)</summary>
        public const string QualityKeyPrefix = "abubu.graphics.quality.";

        private readonly ILocalizationService _localization;
        private readonly GraphicsSettingsLabels _fallback = new();

        public LocalizedGraphicsSettingsLabels(ILocalizationService localization) => _localization = localization;

        public string Resolution(Vector2Int resolution) =>
            _localization.TryGet(ResolutionKey, out _) ? _localization.Get(ResolutionKey, resolution.x, resolution.y) : _fallback.Resolution(resolution);

        public string WindowMode(FullScreenMode mode)
        {
            var key = mode switch
            {
                FullScreenMode.ExclusiveFullScreen => ExclusiveFullScreenKey,
                FullScreenMode.FullScreenWindow => FullScreenWindowKey,
                FullScreenMode.MaximizedWindow => MaximizedWindowKey,
                FullScreenMode.Windowed => WindowedKey,
                _ => null,
            };
            return key != null && _localization.TryGet(key, out _) ? _localization.Get(key) : _fallback.WindowMode(mode);
        }

        public string Quality(int level, string qualityName)
        {
            var key = QualityKeyPrefix + (qualityName ?? string.Empty).ToLowerInvariant().Replace(' ', '_');
            return _localization.TryGet(key, out _) ? _localization.Get(key) : _fallback.Quality(level, qualityName);
        }

        public string FrameRate(int frameRate)
        {
            if (frameRate < 0) return _localization.TryGet(UnlimitedFrameRateKey, out _) ? _localization.Get(UnlimitedFrameRateKey) : _fallback.FrameRate(frameRate);
            return _localization.TryGet(FrameRateKey, out _) ? _localization.Get(FrameRateKey, frameRate) : _fallback.FrameRate(frameRate);
        }
    }

    /// <summary>画面設定 UI とローカライズの両方が有効なときに AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class GraphicsUiLocalizationModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public GraphicsUiLocalizationModuleInstaller() { }

        public void Install(DiContainer container)
        {
            // View の Labels を自分で編集している場合に上書きしないよう、Integration.LocalizePackageUi に従う
            if (!container.HasBinding<PackageUiLocalizationEnabled>() || !container.HasBinding<ILocalizationService>()) return;
            if (container.HasBinding<IGraphicsSettingsLabels>()) return;
            container.Bind<IGraphicsSettingsLabels>().To<LocalizedGraphicsSettingsLabels>().AsSingle();
        }
    }
}
