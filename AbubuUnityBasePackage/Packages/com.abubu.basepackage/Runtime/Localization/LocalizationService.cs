using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Abubu.Save;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.Localization
{
    /// <summary>
    /// ローカライズされた文字列・アセットの取得と、言語の切り替え。選んだ言語は ISaveService に保存され、次回起動時に復元される。
    /// <code>
    /// _loc.Get("menu.start");                                         // "スタート"
    /// _loc.Get("result.score", 1200);                                  // "スコア: {0:N0}" → "スコア: 1,200"
    /// _loc.Get("result.rank", new Dictionary&lt;string, object&gt; { ["rank"] = 3 });   // "{rank} 位"
    /// _loc.GetPlural("item.count", 3);                                // item.count.one / item.count.other から選ぶ
    /// await _loc.SetLanguageAsync("en");
    /// _loc.Changed.Subscribe(_ => Refresh());                         // 言語・疑似ローカライズの切り替えで表示を更新する
    /// </code>
    /// 翻訳が無いキーは「選択中の言語 → その親 (zh-Hant-TW → zh-Hant → zh) → Fallback Language → 先頭の言語」の順に探す。
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>選択中の言語コード</summary>
        ReadOnlyReactiveProperty<string> Language { get; }

        /// <summary>翻訳を読み込み中か (非同期の取得元で、起動直後や言語の切り替え中に true)</summary>
        ReadOnlyReactiveProperty<bool> IsLoading { get; }

        /// <summary>表示を更新すべきとき (言語の切り替え・疑似ローカライズの切り替え・初回の読み込み完了) に流れる</summary>
        Observable<Unit> Changed { get; }

        /// <summary>選べる言語コードの一覧</summary>
        IReadOnlyList<string> Languages { get; }

        /// <summary>選択中の言語の書式 (数値・日付)。<see cref="Get(string, object[])"/> の書式化に使われる</summary>
        CultureInfo Culture { get; }

        /// <summary>疑似ローカライズ ("Start" → "[!!Ŝţåŕţ~~!!]")。切り替えると <see cref="Changed"/> が流れる</summary>
        bool PseudoLocalization { get; set; }

        /// <summary>
        /// 言語を切り替えて保存する。一覧に無い言語なら何もせず false。
        /// 非同期の取得元では読み込みを始めるだけで、読み込みが終わってから Language が変わる (待つ場合は <see cref="SetLanguageAsync"/>)
        /// </summary>
        bool SetLanguage(string language);

        /// <summary>言語を切り替える。翻訳の読み込みが終わってから Language が変わる。続けて呼ばれた場合は最後の言語だけが反映される</summary>
        UniTask<bool> SetLanguageAsync(string language, CancellationToken ct = default);

        /// <summary>最初の言語の翻訳を読み込み終わるまで待つ (同期の取得元ではすぐ終わる)</summary>
        UniTask WaitReadyAsync(CancellationToken ct = default);

        /// <summary>キーに対応する文字列。見つからなければ設定 (MissingKey) に従ってキーなどを返す</summary>
        string Get(string key);

        /// <summary>"{0}" "{1:N0}" などに args を入れる (選択中の言語の書式で)</summary>
        string Get(string key, params object[] args);

        /// <summary>"{score}" のような名前付きのプレースホルダーに値を入れる</summary>
        string Get(string key, IReadOnlyDictionary<string, object> named);

        /// <summary>
        /// 数に応じて "key.zero" (0 のときだけ・任意) / "key.one" / "key.few" / "key.many" / "key.other" から選び、{0} と {count} に数を入れる。
        /// どれも無ければ "key" を使う
        /// </summary>
        string GetPlural(string key, long count, IReadOnlyDictionary<string, object> named = null);

        /// <summary>キーに対応する文字列があれば true (フォールバックも探す)。警告は出さない</summary>
        bool TryGet(string key, out string text);

        /// <summary>言語の表示名 ("ja" → "日本語")。言語選択 UI 用。テーブルに "language.ja" のキーがあればそちらを使う</summary>
        string GetLanguageName(string language);

        /// <summary>選択中の言語のフォント (設定が無ければ null)</summary>
        LocalizationFont GetFont();

        /// <summary>言語ごとのアセット (画像・音声など)。フォールバックも探し、無ければ null</summary>
        T GetAsset<T>(string key) where T : Object;

        /// <summary>言語ごとのアセットを非同期で取得する (Addressables などの取得元用。同期の取得元ならすぐ終わる)</summary>
        UniTask<T> GetAssetAsync<T>(string key, CancellationToken ct = default) where T : Object;
    }

    public sealed class LocalizationService : ILocalizationService, IDisposable
    {
        public const string SaveKey = "abubu.language";

        /// <summary>言語名をテーブルで上書きするときのキーの接頭辞 ("language.ja")</summary>
        public const string LanguageNameKeyPrefix = "language.";

        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

        private readonly ILocalizationSource _source;
        private readonly IAsyncLocalizationSource _asyncSource;
        private readonly LocalizationSettings _settings;
        private readonly ISaveService _save;
        private readonly string[] _languages;
        private readonly string _fallback;
        private readonly ReactiveProperty<string> _language;
        private readonly ReactiveProperty<bool> _isLoading = new(false);
        private readonly Subject<Unit> _changed = new();
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _loaded = new();
        private readonly HashSet<string> _warnedKeys = new();
        private readonly CancellationTokenSource _disposeCts = new();

        /// <summary>選択中の言語で探す順の言語コード (選択中 → 親 → フォールバック → 先頭)</summary>
        private string[] _chain;
        private CultureInfo _culture;
        private bool _pseudo;
        private bool _ready;
        private int _requestVersion;
        private UniTaskCompletionSource _readySource;

        public ReadOnlyReactiveProperty<string> Language => _language;
        public ReadOnlyReactiveProperty<bool> IsLoading => _isLoading;
        public Observable<Unit> Changed => _changed;
        public IReadOnlyList<string> Languages => _languages;
        public CultureInfo Culture => _culture;

        public bool PseudoLocalization
        {
            get => _pseudo;
            set
            {
                if (_pseudo == value) return;
                _pseudo = value;
                _changed.OnNext(Unit.Default);
            }
        }

        /// <param name="source">翻訳の取得元</param>
        /// <param name="save">選んだ言語の保存先。null なら保存しない</param>
        public LocalizationService(ILocalizationSource source, [InjectOptional] LocalizationSettings settings = null,
            [InjectOptional] ISaveService save = null)
        {
            _settings = settings ?? new LocalizationSettings();
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _asyncSource = source as IAsyncLocalizationSource;
            _save = _settings.RememberLanguage ? save : null;
            _pseudo = _settings.PseudoLocalization;
            _languages = (source.Languages ?? Array.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (_languages.Length == 0)
            {
                Debug.LogWarning("[Abubu.Localization] 言語が 1 つも登録されていません。AbubuSettings > Localization > Table を設定してください。");
            }

            _fallback = LanguageCode.Resolve(_settings.FallbackLanguage, _languages) ?? _languages.FirstOrDefault() ?? "";

            var saved = _save != null && _save.TryLoad<string>(SaveKey, out var s) ? s : null;
            var initial = LanguageCode.Resolve(saved, _languages)
                          ?? LanguageCode.Resolve(_settings.StartupLanguage, _languages)
                          ?? LanguageCode.Resolve(LanguageCode.FromSystemLanguage(Application.systemLanguage), _languages)
                          ?? _fallback;
            _language = new ReactiveProperty<string>(initial);
            _chain = BuildChain(initial);
            _culture = LocalizationFormatter.GetCulture(initial);

            if (_asyncSource == null)
            {
                foreach (var language in _chain) _loaded[language] = _source.Load(language) ?? Empty;
                _ready = true;
            }
            else
            {
                // 非同期の取得元は WaitReadyAsync (起動処理から呼ばれる) で読み込む
                _isLoading.Value = true;
            }
        }

        public bool SetLanguage(string language)
        {
            var resolved = Resolve(language);
            if (resolved == null) return false;
            if (_asyncSource != null)
            {
                SetLanguageAsync(resolved, _disposeCts.Token).Forget();
                return true;
            }

            Apply(resolved);
            return true;
        }

        public async UniTask<bool> SetLanguageAsync(string language, CancellationToken ct = default)
        {
            var resolved = Resolve(language);
            if (resolved == null) return false;
            // 続けて切り替えられた場合は、最後に要求された言語だけを反映する
            var version = ++_requestVersion;
            if (resolved == _language.Value && _ready)
            {
                // 読み込み中の別の言語があれば、それを取り消して今の言語のままにする
                _isLoading.Value = false;
                return true;
            }

            _isLoading.Value = true;
            try
            {
                await LoadChainAsync(BuildChain(resolved), ct);
            }
            finally
            {
                if (version == _requestVersion) _isLoading.Value = false;
            }

            if (version != _requestVersion) return false;
            Apply(resolved);
            return true;
        }

        public async UniTask WaitReadyAsync(CancellationToken ct = default)
        {
            if (_ready) return;
            if (_readySource == null)
            {
                _readySource = new UniTaskCompletionSource();
                LoadInitialAsync().Forget();
            }
            await _readySource.Task.AttachExternalCancellation(ct);
        }

        private async UniTaskVoid LoadInitialAsync()
        {
            try
            {
                await LoadChainAsync(_chain, _disposeCts.Token);
                _ready = true;
                _isLoading.Value = false;
                _changed.OnNext(Unit.Default);
                _readySource.TrySetResult();
            }
            catch (Exception e)
            {
                _isLoading.Value = false;
                _readySource.TrySetException(e);
            }
        }

        private async UniTask LoadChainAsync(IEnumerable<string> chain, CancellationToken ct)
        {
            foreach (var language in chain)
            {
                if (_loaded.ContainsKey(language)) continue;
                var values = _asyncSource != null ? await _asyncSource.LoadAsync(language, ct) : _source.Load(language);
                _loaded[language] = values ?? Empty;
            }
        }

        /// <summary>読み込み済みの言語に切り替える</summary>
        private void Apply(string language)
        {
            var chain = BuildChain(language);
            foreach (var code in chain)
            {
                if (!_loaded.ContainsKey(code)) _loaded[code] = _source.Load(code) ?? Empty;
            }

            _chain = chain;
            _culture = LocalizationFormatter.GetCulture(language);
            _ready = true;
            if (language != _language.Value)
            {
                _save?.Save(SaveKey, language);
                _language.Value = language;
            }
            _changed.OnNext(Unit.Default);
        }

        private string Resolve(string language)
        {
            var resolved = LanguageCode.Resolve(language, _languages);
            if (resolved == null)
            {
                Debug.LogWarning($"[Abubu.Localization] 言語 \"{language}\" はテーブルにありません。使える言語: {string.Join(", ", _languages)}");
            }
            return resolved;
        }

        /// <summary>選択中 → 親 (テーブルにあるものだけ) → フォールバック → 先頭 の順の言語コード</summary>
        private string[] BuildChain(string language)
        {
            var chain = new List<string>();
            foreach (var candidate in LanguageCode.Chain(language)) Add(LanguageCode.FindExact(candidate, _languages));
            Add(_fallback);
            Add(_languages.FirstOrDefault());
            return chain.ToArray();

            void Add(string code)
            {
                if (!string.IsNullOrEmpty(code) && !chain.Contains(code)) chain.Add(code);
            }
        }

        public string Get(string key) => Decorate(GetRaw(key));

        public string Get(string key, params object[] args)
        {
            if (args == null || args.Length == 0) return Get(key);
            return Decorate(LocalizationFormatter.Format(GetRaw(key), _culture, args));
        }

        public string Get(string key, IReadOnlyDictionary<string, object> named) =>
            Decorate(LocalizationFormatter.Format(GetRaw(key), _culture, null, named));

        public string GetPlural(string key, long count, IReadOnlyDictionary<string, object> named = null)
        {
            var category = LocalizationFormatter.PluralCategory(_language.Value, count);
            string template = null;
            if (count == 0) TryGet(key + ".zero", out template);
            if (template == null && !TryGet(key + "." + category, out template) && !TryGet(key + ".other", out template))
            {
                template = GetRaw(key);
            }

            var values = new Dictionary<string, object>();
            if (named != null)
            {
                foreach (var pair in named) values[pair.Key] = pair.Value;
            }
            values["count"] = count;
            return Decorate(LocalizationFormatter.Format(template, _culture, new object[] { count }, values));
        }

        public bool TryGet(string key, out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var language in _chain)
            {
                if (_loaded.TryGetValue(language, out var values) && values.TryGetValue(key, out text)) return true;
            }
            return false;
        }

        /// <summary>キーの翻訳 (書式化前)。見つからなければ MissingKey の設定に従う</summary>
        private string GetRaw(string key)
        {
            if (TryGet(key, out var text)) return text;
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // 非同期の取得元で読み込み中のときは、まだ無いだけなので警告しない
            if (_settings.WarnMissingKeys && _ready && _warnedKeys.Add(key))
            {
                var keys = _chain.SelectMany(l => _loaded.TryGetValue(l, out var v) ? v.Keys : Enumerable.Empty<string>());
                Debug.LogWarning($"[Abubu.Localization] キー \"{key}\" の翻訳がありません ({_language.Value}).{KeySuggestion.Hint(key, keys)}");
            }

            return _settings.MissingKey switch
            {
                MissingKeyBehavior.Bracketed => "[" + key + "]",
                MissingKeyBehavior.Empty => string.Empty,
                _ => key,
            };
        }

        private string Decorate(string text) => _pseudo ? PseudoLocalizer.Apply(text) : text;

        public string GetLanguageName(string language)
        {
            if (string.IsNullOrEmpty(language)) return string.Empty;
            if (TryGet(LanguageNameKeyPrefix + language, out var name)) return name;
            return LanguageCode.Normalize(language) switch
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
                "pt-BR" => "Português (Brasil)",
                "ru" => "Русский",
                "uk" => "Українська",
                "pl" => "Polski",
                "tr" => "Türkçe",
                "th" => "ไทย",
                "vi" => "Tiếng Việt",
                "id" => "Bahasa Indonesia",
                _ => language,
            };
        }

        public LocalizationFont GetFont()
        {
            var fonts = _settings.Fonts;
            if (fonts == null || fonts.Count == 0) return null;

            // 選択中の言語と、その親 (zh-Hant-TW → zh-Hant → zh) だけを見る。フォールバック言語のフォントは使わない
            foreach (var candidate in LanguageCode.Chain(_language.Value))
            {
                foreach (var font in fonts)
                {
                    if (font != null && font.Language != "*" && LanguageCode.Normalize(font.Language) == candidate) return font;
                }
            }
            return fonts.FirstOrDefault(f => f != null && f.Language == "*");
        }

        public T GetAsset<T>(string key) where T : Object
        {
            if (string.IsNullOrEmpty(key) || _source is not ILocalizedAssetSource assets) return null;
            foreach (var language in _chain)
            {
                if (assets.TryGetAsset(language, key, out var asset) && asset is T typed) return typed;
            }
            return null;
        }

        public async UniTask<T> GetAssetAsync<T>(string key, CancellationToken ct = default) where T : Object
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_source is not IAsyncLocalizedAssetSource assets) return GetAsset<T>(key);

            foreach (var language in _chain)
            {
                if (await assets.LoadAssetAsync(language, key, ct) is T typed) return typed;
            }
            return null;
        }

        public void Dispose()
        {
            _disposeCts.Cancel();
            _disposeCts.Dispose();
            _language.Dispose();
            _isLoading.Dispose();
            _changed.Dispose();
        }

        /// <summary>端末の言語を言語コードに変換する。対応表に無ければ null</summary>
        public static string ToLanguageCode(SystemLanguage language) => LanguageCode.FromSystemLanguage(language);
    }
}

namespace Abubu
{
    using Abubu.Localization;

    /// <summary>
    /// ローカライズの static ショートカット。
    /// <code>
    /// label.text = Loc.Get("menu.start");
    /// label.text = Loc.Get("result.score", score);
    /// label.text = Loc.Plural("item.count", count);
    /// Loc.SetLanguage("en");
    /// </code>
    /// ProjectContext が無いときはキーをそのまま返す。
    /// </summary>
    public static class Loc
    {
        public static ILocalizationService Service => AbubuServices.TryResolve<ILocalizationService>();

        public static string Get(string key) => Service?.Get(key) ?? key;
        public static string Get(string key, params object[] args) => Service?.Get(key, args) ?? key;
        public static string Get(string key, IReadOnlyDictionary<string, object> named) => Service?.Get(key, named) ?? key;
        public static string Plural(string key, long count) => Service?.GetPlural(key, count) ?? key;
        public static T Asset<T>(string key) where T : UnityEngine.Object => Service?.GetAsset<T>(key);
        public static bool SetLanguage(string language) => Service?.SetLanguage(language) ?? false;
        public static string Language => Service?.Language.CurrentValue;
        public static Observable<string> OnLanguageChanged => Service?.Language ?? Observable.Empty<string>();
    }
}
