using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Abubu.Save;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Localization
{
    [Serializable]
    public sealed class LocalizationSettings
    {
        [Tooltip("翻訳テーブル。ILocalizationSource を自前でバインドした場合は使われない")]
        public LocalizationTable Table;

        [Tooltip("初回起動時の言語。空なら端末の言語 (テーブルに無ければ先頭の言語)")]
        public string StartupLanguage = "";

        [Tooltip("選択中の言語に翻訳が無いキーで使う言語。空ならテーブルの先頭の言語")]
        public string FallbackLanguage = "";

        [Tooltip("選んだ言語を ISaveService に保存し、次回起動時に復元する")]
        public bool RememberLanguage = true;
    }

    /// <summary>
    /// ローカライズされた文字列の取得と、言語の切り替え。選んだ言語は ISaveService に保存され、次回起動時に復元される。
    /// <code>
    /// _loc.Get("menu.start");                 // "スタート"
    /// _loc.Get("result.score", 1200);          // "スコア: {0}" → "スコア: 1200"
    /// _loc.SetLanguage("en");
    /// _loc.Language.Subscribe(_ => Refresh()); // 言語が変わったら表示を更新する
    /// </code>
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>選択中の言語コード</summary>
        ReadOnlyReactiveProperty<string> Language { get; }

        /// <summary>選べる言語コードの一覧 (テーブルの Languages の順)</summary>
        IReadOnlyList<string> Languages { get; }

        /// <summary>言語を切り替えて保存する。一覧に無い言語なら何もせず false</summary>
        bool SetLanguage(string language);

        /// <summary>
        /// キーに対応する文字列。選択中の言語 → フォールバック言語 の順に探し、どちらにも無ければキーそのものを返す (警告はキーごとに 1 回)。
        /// </summary>
        string Get(string key);

        /// <summary><see cref="Get(string)"/> の結果を string.Format で整形する ("{0}" などに args が入る)</summary>
        string Get(string key, params object[] args);

        /// <summary>キーに対応する文字列があれば true (フォールバック言語も探す)。警告は出さない</summary>
        bool TryGet(string key, out string text);

        /// <summary>言語の表示名 ("ja" → "日本語")。言語選択 UI 用。テーブルに "language.ja" のキーがあればそちらを使う</summary>
        string GetLanguageName(string language);
    }

    public sealed class LocalizationService : ILocalizationService, IDisposable
    {
        public const string SaveKey = "abubu.language";

        /// <summary>言語名をテーブルで上書きするときのキーの接頭辞 ("language.ja")</summary>
        public const string LanguageNameKeyPrefix = "language.";

        private readonly ILocalizationSource _source;
        private readonly ISaveService _save;
        private readonly string[] _languages;
        private readonly string _fallback;
        private readonly IReadOnlyDictionary<string, string> _fallbackValues;
        private readonly ReactiveProperty<string> _language;
        private readonly HashSet<string> _warnedKeys = new();
        private IReadOnlyDictionary<string, string> _current;

        public ReadOnlyReactiveProperty<string> Language => _language;
        public IReadOnlyList<string> Languages => _languages;

        /// <param name="source">翻訳の取得元</param>
        /// <param name="save">選んだ言語の保存先。null なら保存しない</param>
        public LocalizationService(ILocalizationSource source, [InjectOptional] LocalizationSettings settings = null,
            [InjectOptional] ISaveService save = null)
        {
            settings ??= new LocalizationSettings();
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _save = settings.RememberLanguage ? save : null;
            _languages = (source.Languages ?? Array.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (_languages.Length == 0)
            {
                Debug.LogWarning("[Abubu.Localization] 言語が 1 つも登録されていません。AbubuSettings > Localization > Table を設定してください。");
            }

            _fallback = Resolve(settings.FallbackLanguage) ?? _languages.FirstOrDefault() ?? "";
            _fallbackValues = _source.Load(_fallback) ?? new Dictionary<string, string>();

            var saved = _save != null && _save.TryLoad<string>(SaveKey, out var s) ? s : null;
            var initial = Resolve(saved) ?? Resolve(settings.StartupLanguage) ?? Resolve(ToLanguageCode(Application.systemLanguage)) ?? _fallback;
            _language = new ReactiveProperty<string>(initial);
            _current = Load(initial);
        }

        public bool SetLanguage(string language)
        {
            var resolved = Resolve(language);
            if (resolved == null)
            {
                Debug.LogWarning($"[Abubu.Localization] 言語 \"{language}\" はテーブルにありません。使える言語: {string.Join(", ", _languages)}");
                return false;
            }

            if (resolved == _language.Value) return true;
            _current = Load(resolved);
            _save?.Save(SaveKey, resolved);
            _language.Value = resolved;
            return true;
        }

        public string Get(string key)
        {
            if (TryGet(key, out var text)) return text;
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (_warnedKeys.Add(key))
            {
                var hint = KeySuggestion.Hint(key, _current.Keys.Concat(_fallbackValues.Keys));
                Debug.LogWarning($"[Abubu.Localization] キー \"{key}\" の翻訳がありません ({_language.Value}).{hint}");
            }
            return key;
        }

        public string Get(string key, params object[] args)
        {
            var format = Get(key);
            if (args == null || args.Length == 0) return format;
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException e)
            {
                Debug.LogError($"[Abubu.Localization] \"{key}\" の書式が不正です ({_language.Value}): {e.Message}");
                return format;
            }
        }

        public bool TryGet(string key, out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (_current.TryGetValue(key, out text)) return true;
            return _fallbackValues.TryGetValue(key, out text);
        }

        public string GetLanguageName(string language)
        {
            if (string.IsNullOrEmpty(language)) return string.Empty;
            if (TryGet(LanguageNameKeyPrefix + language, out var name)) return name;
            return language switch
            {
                "ja" => "日本語",
                "en" => "English",
                "zh-Hans" => "简体中文",
                "zh-Hant" => "繁體中文",
                "ko" => "한국어",
                "fr" => "Français",
                "de" => "Deutsch",
                "es" => "Español",
                "it" => "Italiano",
                "pt" => "Português",
                "ru" => "Русский",
                _ => language,
            };
        }

        public void Dispose() => _language.Dispose();

        private IReadOnlyDictionary<string, string> Load(string language) =>
            language == _fallback ? _fallbackValues : _source.Load(language) ?? new Dictionary<string, string>();

        /// <summary>
        /// テーブルにある言語コードに合わせる。完全一致 (大文字小文字は無視) → 地域なしでの一致 ("pt-BR" と "pt") の順に探し、無ければ null
        /// </summary>
        private string Resolve(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return null;
            language = language.Trim();

            foreach (var l in _languages)
            {
                if (string.Equals(l, language, StringComparison.OrdinalIgnoreCase)) return l;
            }

            var baseLanguage = BaseOf(language);
            foreach (var l in _languages)
            {
                if (string.Equals(BaseOf(l), baseLanguage, StringComparison.OrdinalIgnoreCase)) return l;
            }
            return null;

            static string BaseOf(string code)
            {
                var index = code.IndexOf('-');
                return index < 0 ? code : code.Substring(0, index);
            }
        }

        /// <summary>端末の言語を言語コードに変換する。対応表に無ければ null</summary>
        public static string ToLanguageCode(SystemLanguage language) => language switch
        {
            SystemLanguage.Japanese => "ja",
            SystemLanguage.English => "en",
            SystemLanguage.Chinese => "zh-Hans",
            SystemLanguage.ChineseSimplified => "zh-Hans",
            SystemLanguage.ChineseTraditional => "zh-Hant",
            SystemLanguage.Korean => "ko",
            SystemLanguage.French => "fr",
            SystemLanguage.German => "de",
            SystemLanguage.Spanish => "es",
            SystemLanguage.Italian => "it",
            SystemLanguage.Portuguese => "pt",
            SystemLanguage.Russian => "ru",
            _ => null,
        };
    }
}

namespace Abubu
{
    using Abubu.Localization;

    /// <summary>
    /// ローカライズの static ショートカット。
    /// <code>
    /// label.text = Loc.Get("menu.start");
    /// Loc.SetLanguage("en");
    /// </code>
    /// ProjectContext が無いときはキーをそのまま返す。
    /// </summary>
    public static class Loc
    {
        public static ILocalizationService Service => AbubuServices.TryResolve<ILocalizationService>();

        public static string Get(string key) => Service?.Get(key) ?? key;
        public static string Get(string key, params object[] args) => Service?.Get(key, args) ?? key;
        public static bool SetLanguage(string language) => Service?.SetLanguage(language) ?? false;
        public static string Language => Service?.Language.CurrentValue;
        public static Observable<string> OnLanguageChanged => Service?.Language ?? Observable.Empty<string>();
    }
}
