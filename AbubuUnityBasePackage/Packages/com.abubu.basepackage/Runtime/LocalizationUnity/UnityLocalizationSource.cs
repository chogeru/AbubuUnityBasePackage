using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Abubu.Localization;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Scripting;
using Zenject;
using UnityLocalizationSettings = UnityEngine.Localization.Settings.LocalizationSettings;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.Localization.UnityLocalizationModuleInstaller))]
[assembly: AlwaysLinkAssembly]

namespace Abubu.Localization
{
    /// <summary>
    /// Unity Localization パッケージ (com.unity.localization) の String Table を <see cref="ILocalizationSource"/> として使う。
    /// 言語は Unity Localization の Locale (Identifier.Code)、文字列は指定した String Table Collection から取る。
    /// </summary>
    /// <remarks>
    /// com.unity.localization が入っていて、<see cref="UnityLocalizationSourceSettings"/> を AbubuSettings > Module Settings に登録したときだけ有効になる。
    /// 文字列は Smart Format を通さず、テーブルに書かれた文字列をそのまま返す (書式化は Abubu 側の "{0}" "{name}" で行う)。
    /// </remarks>
    public sealed class UnityLocalizationSource : IAsyncLocalizationSource
    {
        private readonly string _tableName;
        private string[] _languages;

        public UnityLocalizationSource(string tableName) => _tableName = tableName;

        public IReadOnlyList<string> Languages =>
            _languages ??= UnityLocalizationSettings.AvailableLocales?.Locales?.Select(l => l.Identifier.Code).ToArray() ?? Array.Empty<string>();

        /// <summary>同期で読み込む (WebGL では使えない。非同期の <see cref="LoadAsync"/> が優先して使われる)</summary>
        public IReadOnlyDictionary<string, string> Load(string language)
        {
            var locale = FindLocale(language);
            if (locale == null) return new Dictionary<string, string>();
            var handle = UnityLocalizationSettings.StringDatabase.GetTableAsync(_tableName, locale);
            return ToDictionary(handle.WaitForCompletion());
        }

        public async UniTask<IReadOnlyDictionary<string, string>> LoadAsync(string language, CancellationToken ct)
        {
            await WaitAsync(UnityLocalizationSettings.InitializationOperation, ct);
            _languages = null;

            var locale = FindLocale(language);
            if (locale == null) return new Dictionary<string, string>();

            var handle = UnityLocalizationSettings.StringDatabase.GetTableAsync(_tableName, locale);
            await WaitAsync(handle, ct);
            return ToDictionary(handle.Result);
        }

        private static async UniTask WaitAsync<T>(AsyncOperationHandle<T> handle, CancellationToken ct)
        {
            while (!handle.IsDone) await UniTask.Yield(ct);
        }

        private static Locale FindLocale(string language)
        {
            var locales = UnityLocalizationSettings.AvailableLocales?.Locales;
            if (locales == null) return null;
            var code = LanguageCode.FindExact(language, locales.Select(l => l.Identifier.Code).ToArray());
            return code == null ? null : locales.FirstOrDefault(l => l.Identifier.Code == code);
        }

        private static IReadOnlyDictionary<string, string> ToDictionary(StringTable table)
        {
            var result = new Dictionary<string, string>();
            if (table == null) return result;
            foreach (var entry in table.Values)
            {
                var key = entry?.SharedEntry?.Key;
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(entry.Value)) result[key] = entry.Value;
            }
            return result;
        }
    }

    /// <summary>Unity Localization を使う設定。AbubuSettings > Module Settings に登録する</summary>
    [CreateAssetMenu(menuName = "Abubu/Unity Localization Source", fileName = "UnityLocalizationSourceSettings")]
    public sealed class UnityLocalizationSourceSettings : ScriptableObject
    {
        [Tooltip("使う String Table Collection の名前")]
        public string TableName = "Strings";
    }

    /// <summary>com.unity.localization が入っているときに AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class UnityLocalizationModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public UnityLocalizationModuleInstaller() { }

        public void Install(DiContainer container)
        {
            // 設定アセットが登録されているときだけ使う。利用者が自前の取得元をバインドしている場合 (既定の目印が無い) は差し替えない
            if (!container.HasBinding<UnityLocalizationSourceSettings>()) return;
            var isDefault = container.HasBinding<DefaultLocalizationSourceMarker>();
            if (container.HasBinding<ILocalizationSource>() && !isDefault) return;

            // インストール中に設定を Resolve しないよう、生成は使われるときまで遅らせる
            container.Rebind<ILocalizationSource>()
                .FromMethod(ctx => new UnityLocalizationSource(ctx.Container.Resolve<UnityLocalizationSourceSettings>().TableName))
                .AsSingle();
        }
    }
}
