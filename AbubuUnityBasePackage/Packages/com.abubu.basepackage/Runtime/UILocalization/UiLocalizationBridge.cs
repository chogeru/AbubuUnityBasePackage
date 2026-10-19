using Abubu.Localization;
using Abubu.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting;
using Zenject;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.UI.UiLocalizationModuleInstaller))]
[assembly: AlwaysLinkAssembly]

namespace Abubu.UI
{
    /// <summary>ダイアログのボタン (OK / Cancel) を翻訳する。キーが無ければ AbubuUiSettings の文言を使う</summary>
    public sealed class LocalizedDialogLabels : IDialogLabels
    {
        public const string OkKey = "abubu.common.ok";
        public const string CancelKey = "abubu.common.cancel";

        private readonly ILocalizationService _localization;
        private readonly IDialogLabels _fallback;

        public LocalizedDialogLabels(ILocalizationService localization, [InjectOptional] AbubuUiSettings settings = null)
        {
            _localization = localization;
            _fallback = new DialogLabels(settings != null ? settings : AbubuUiSettings.Default);
        }

        public string Ok => _localization.TryGet(OkKey, out _) ? _localization.Get(OkKey) : _fallback.Ok;
        public string Cancel => _localization.TryGet(CancelKey, out _) ? _localization.Get(CancelKey) : _fallback.Cancel;
    }

    /// <summary>
    /// 組み込みのダイアログ・トーストのフォントを、言語ごとのフォント (LocalizationFontSettings) にする。
    /// その言語の指定が無ければ AbubuUiSettings.DefaultFont / DefaultTmpFont
    /// </summary>
    public sealed class LocalizedUiFontProvider : IUiFontProvider
    {
        private readonly ILocalizedFontService _fonts;
        private readonly IUiFontProvider _fallback;

        public LocalizedUiFontProvider(ILocalizedFontService fonts, [InjectOptional] AbubuUiSettings settings = null)
        {
            _fonts = fonts;
            _fallback = new SettingsUiFontProvider(settings);
        }

        public Font Font
        {
            get
            {
                var font = _fonts.GetFont();
                return font != null && font.Font != null ? font.Font : _fallback.Font;
            }
        }

        public TMP_FontAsset TmpFont
        {
            get
            {
                var font = _fonts.GetFont();
                return font != null && font.TmpFont != null ? font.TmpFont : _fallback.TmpFont;
            }
        }
    }

    /// <summary>UI とローカライズの両方が有効なときに AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class UiLocalizationModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public UiLocalizationModuleInstaller() { }

        public void Install(DiContainer container)
        {
            if (!container.HasBinding<ILocalizationService>()) return;

            // 言語ごとのフォントの登録 (Abubu.Localization.UI) より先に呼ばれても使えるよう、生成は使うときまで遅らせる
            if (!container.HasBinding<IUiFontProvider>())
            {
                container.Bind<IUiFontProvider>().FromMethod(ctx =>
                {
                    var fonts = ctx.Container.TryResolve<ILocalizedFontService>();
                    var settings = ctx.Container.TryResolve<AbubuUiSettings>();
                    return fonts != null ? new LocalizedUiFontProvider(fonts, settings) : new SettingsUiFontProvider(settings);
                }).AsSingle();
            }

            // View の文言を自分で編集している場合に上書きしないよう、Integration.LocalizePackageUi に従う
            if (container.HasBinding<PackageUiLocalizationEnabled>() && !container.HasBinding<IDialogLabels>())
            {
                container.Bind<IDialogLabels>().To<LocalizedDialogLabels>().AsSingle();
            }
        }
    }
}
