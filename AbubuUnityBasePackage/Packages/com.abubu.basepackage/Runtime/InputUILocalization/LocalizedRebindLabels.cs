using Abubu.Inputs.UI;
using Abubu.Localization;
using UnityEngine.Scripting;
using Zenject;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.Inputs.UI.LocalizedRebindLabelsInstaller))]
[assembly: AlwaysLinkAssembly]

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// キー割り当て画面 (RebindView) の文言を ILocalizationService で翻訳する。テーブルにキーが無い項目は英語の既定値を使う。
    /// アクション名は "abubu.input.action.{アクション名を小文字にしたもの}" (例: abubu.input.action.jump) で翻訳する。
    /// 使うキーはパッケージ同梱の AbubuBuiltInStrings.csv を参照。
    /// </summary>
    public sealed class LocalizedRebindLabels : IRebindLabels
    {
        public const string ActionKeyPrefix = "abubu.input.action.";
        public const string UnboundKey = "abubu.rebind.unbound";
        public const string ListeningKey = "abubu.rebind.listening";
        public const string PromptKey = "abubu.rebind.prompt";
        public const string ConflictKey = "abubu.rebind.conflict";
        public const string SwappedKey = "abubu.rebind.swapped";
        public const string RejectedKey = "abubu.rebind.rejected";

        private readonly ILocalizationService _localization;
        private readonly RebindLabels _fallback = new();

        public LocalizedRebindLabels(ILocalizationService localization) => _localization = localization;

        public string ActionName(string actionPath, string actionName)
        {
            var key = ActionKeyPrefix + (actionName ?? string.Empty).ToLowerInvariant().Replace(' ', '_');
            return _localization.TryGet(key, out _) ? _localization.Get(key) : _fallback.ActionName(actionPath, actionName);
        }

        public string Unbound => Has(UnboundKey) ? _localization.Get(UnboundKey) : _fallback.Unbound;
        public string Listening => Has(ListeningKey) ? _localization.Get(ListeningKey) : _fallback.Listening;

        public string ListeningPrompt(string actionName) =>
            Has(PromptKey) ? _localization.Get(PromptKey, actionName) : _fallback.ListeningPrompt(actionName);

        public string Conflict(string otherActions) =>
            Has(ConflictKey) ? _localization.Get(ConflictKey, otherActions) : _fallback.Conflict(otherActions);

        public string Swapped(string otherActions) =>
            Has(SwappedKey) ? _localization.Get(SwappedKey, otherActions) : _fallback.Swapped(otherActions);

        public string Rejected(string binding, string otherActions) =>
            Has(RejectedKey) ? _localization.Get(RejectedKey, binding, otherActions) : _fallback.Rejected(binding, otherActions);

        private bool Has(string key) => _localization.TryGet(key, out _);
    }

    /// <summary>ローカライズと Input UI の両方が有効なときに AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class LocalizedRebindLabelsInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public LocalizedRebindLabelsInstaller() { }

        public void Install(DiContainer container)
        {
            // View の Labels を自分で編集している場合に上書きしないよう、Integration.LocalizePackageUi に従う
            if (!container.HasBinding<PackageUiLocalizationEnabled>()) return;
            if (!container.HasBinding<ILocalizationService>() || container.HasBinding<IRebindLabels>()) return;
            container.Bind<IRebindLabels>().To<LocalizedRebindLabels>().AsSingle();
        }
    }
}
