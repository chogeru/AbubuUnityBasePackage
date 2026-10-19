using System.Collections.Generic;
using System.Linq;
using UnityEngine.Scripting;
using Zenject;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.Localization.LocalizationUiModuleInstaller))]
[assembly: AlwaysLinkAssembly]

namespace Abubu.Localization
{
    /// <summary>
    /// 選択中の言語のフォント。LocalizedText などが使う。
    /// <code>
    /// var font = _fonts.GetFont();   // 設定が無ければ null
    /// if (font?.TmpFont != null) label.font = font.TmpFont;
    /// </code>
    /// </summary>
    public interface ILocalizedFontService
    {
        /// <summary>選択中の言語 (と、その親の言語) のフォント。無ければ「*」のもの、それも無ければ null</summary>
        LocalizationFont GetFont();
    }

    public sealed class LocalizedFontService : ILocalizedFontService
    {
        private readonly ILocalizationService _localization;
        private readonly IReadOnlyList<LocalizationFont> _fonts;

        public LocalizedFontService(ILocalizationService localization, [InjectOptional] LocalizationFontSettings settings = null)
            : this(localization, settings != null ? settings.Fonts : null)
        {
        }

        /// <summary>フォントの一覧を直接渡す (テスト・コードでの設定用)</summary>
        public LocalizedFontService(ILocalizationService localization, IReadOnlyList<LocalizationFont> fonts)
        {
            _localization = localization;
            _fonts = fonts ?? new List<LocalizationFont>();
        }

        public LocalizationFont GetFont()
        {
            if (_fonts.Count == 0) return null;

            // 選択中の言語と、その親 (zh-Hant-TW → zh-Hant → zh) だけを見る。フォールバック言語のフォントは使わない
            foreach (var candidate in LanguageCode.Chain(_localization.Language.CurrentValue))
            {
                foreach (var font in _fonts)
                {
                    if (font != null && font.Language != "*" && LanguageCode.Normalize(font.Language) == candidate) return font;
                }
            }
            return _fonts.FirstOrDefault(f => f != null && f.Language == "*");
        }
    }

    /// <summary>ローカライズが有効なときに AbubuInstaller から自動で呼ばれる (言語ごとのフォント)</summary>
    [Preserve]
    public sealed class LocalizationUiModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public LocalizationUiModuleInstaller() { }

        public void Install(DiContainer container)
        {
            if (!container.HasBinding<ILocalizationService>() || container.HasBinding<ILocalizedFontService>()) return;
            container.Bind<ILocalizedFontService>().To<LocalizedFontService>().AsSingle();
        }
    }
}
