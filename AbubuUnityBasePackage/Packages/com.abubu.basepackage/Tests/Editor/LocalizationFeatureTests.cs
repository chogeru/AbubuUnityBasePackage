using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Abubu.Editor;
using Abubu.Localization;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Tests
{
    public sealed class LanguageCodeTests
    {
        [Test]
        public void Normalize_FixesSeparatorsAndCase()
        {
            Assert.That(LanguageCode.Normalize("zh_hant_tw"), Is.EqualTo("zh-Hant-TW"));
            Assert.That(LanguageCode.Normalize("EN-us"), Is.EqualTo("en-US"));
            Assert.That(LanguageCode.Normalize("es-419"), Is.EqualTo("es-419"));
        }

        [Test]
        public void Chain_RemovesSubtagsOneByOne()
        {
            Assert.That(LanguageCode.Chain("zh-Hant-TW").ToArray(), Is.EqualTo(new[] { "zh-Hant-TW", "zh-Hant", "zh" }));
        }

        [Test]
        public void Resolve_PrefersExactThenParentThenSibling()
        {
            var available = new[] { "ja", "zh-Hant", "pt-BR" };
            Assert.That(LanguageCode.Resolve("ZH_hant_tw", available), Is.EqualTo("zh-Hant"));
            Assert.That(LanguageCode.Resolve("pt", available), Is.EqualTo("pt-BR"));
            Assert.That(LanguageCode.Resolve("ja-JP", available), Is.EqualTo("ja"));
            Assert.That(LanguageCode.Resolve("fr", available), Is.Null);
        }
    }

    public sealed class LocalizationFormatterTests
    {
        [Test]
        public void Format_UsesCulture_AndNamedPlaceholders()
        {
            var en = CultureInfo.GetCultureInfo("en");
            var de = CultureInfo.GetCultureInfo("de");
            Assert.That(LocalizationFormatter.Format("{0:N0}", en, new object[] { 1200 }), Is.EqualTo("1,200"));
            Assert.That(LocalizationFormatter.Format("{0:N0}", de, new object[] { 1200 }), Is.EqualTo("1.200"));

            var named = new Dictionary<string, object> { ["score"] = 1200, ["name"] = "Abu" };
            Assert.That(LocalizationFormatter.Format("{name}: {score:N0}", en, null, named), Is.EqualTo("Abu: 1,200"));
            Assert.That(LocalizationFormatter.Format("{{literal}} {name,5}", en, null, named), Is.EqualTo("{literal}   Abu"));
            Assert.That(LocalizationFormatter.Format("{unknown}", en, null, named), Is.EqualTo("{unknown}"));
        }

        [Test]
        public void PluralCategory_FollowsLanguageRules()
        {
            Assert.That(LocalizationFormatter.PluralCategory("en", 1), Is.EqualTo("one"));
            Assert.That(LocalizationFormatter.PluralCategory("en", 2), Is.EqualTo("other"));
            Assert.That(LocalizationFormatter.PluralCategory("ja", 1), Is.EqualTo("other"));
            Assert.That(LocalizationFormatter.PluralCategory("fr", 0), Is.EqualTo("one"));
            Assert.That(LocalizationFormatter.PluralCategory("ru", 21), Is.EqualTo("one"));
            Assert.That(LocalizationFormatter.PluralCategory("ru", 3), Is.EqualTo("few"));
            Assert.That(LocalizationFormatter.PluralCategory("ru", 11), Is.EqualTo("many"));
        }

        [Test]
        public void Pseudo_ReplacesLetters_KeepsTagsAndPlaceholders()
        {
            var result = PseudoLocalizer.Apply("<b>Start</b> {name}");
            Assert.That(result, Does.StartWith("[!!<b>"));
            Assert.That(result, Does.EndWith("!!]"));
            Assert.That(result, Does.Contain("</b>"));
            Assert.That(result, Does.Contain("{name}"));
            Assert.That(result, Does.Not.Contain("Start"));
            Assert.That(result, Does.Contain("Ŝţåŕţ"));
        }
    }

    public sealed class LocalizationServiceFeatureTests
    {
        private sealed class DictionarySource : ILocalizationSource
        {
            private readonly Dictionary<string, Dictionary<string, string>> _values;
            public IReadOnlyList<string> Languages { get; }

            public DictionarySource(Dictionary<string, Dictionary<string, string>> values)
            {
                _values = values;
                Languages = values.Keys.ToList();
            }

            public IReadOnlyDictionary<string, string> Load(string language) =>
                _values.TryGetValue(language, out var v) ? v : new Dictionary<string, string>();
        }

        /// <summary>読み込みの完了をテストから操作できる非同期の取得元</summary>
        private sealed class ManualAsyncSource : IAsyncLocalizationSource
        {
            public readonly Dictionary<string, UniTaskCompletionSource<IReadOnlyDictionary<string, string>>> Pending = new();
            public IReadOnlyList<string> Languages { get; } = new[] { "en", "ja" };
            public IReadOnlyDictionary<string, string> Load(string language) => new Dictionary<string, string>();

            public UniTask<IReadOnlyDictionary<string, string>> LoadAsync(string language, CancellationToken ct)
            {
                var source = new UniTaskCompletionSource<IReadOnlyDictionary<string, string>>();
                Pending[language] = source;
                return source.Task;
            }
        }

        private static LocalizationSettings Settings(string startup = "en") => new() { StartupLanguage = startup, RememberLanguage = false };

        [Test]
        public void Lookup_FallsBackThroughParentLanguages()
        {
            var source = new DictionarySource(new Dictionary<string, Dictionary<string, string>>
            {
                ["en"] = new() { ["a"] = "en-a", ["b"] = "en-b", ["c"] = "en-c" },
                ["zh-Hant"] = new() { ["a"] = "hant-a", ["b"] = "hant-b" },
                ["zh-Hant-TW"] = new() { ["a"] = "tw-a" },
            });
            using var loc = new LocalizationService(source, new LocalizationSettings { StartupLanguage = "zh-Hant-TW", FallbackLanguage = "en", RememberLanguage = false });

            Assert.That(loc.Get("a"), Is.EqualTo("tw-a"));
            Assert.That(loc.Get("b"), Is.EqualTo("hant-b"));
            Assert.That(loc.Get("c"), Is.EqualTo("en-c"));
        }

        [Test]
        public void GetPlural_ChoosesCategory_AndFillsCount()
        {
            var source = new DictionarySource(new Dictionary<string, Dictionary<string, string>>
            {
                ["en"] = new() { ["item.zero"] = "No items", ["item.one"] = "{count} item", ["item.other"] = "{count:N0} items" },
                ["ja"] = new() { ["item.other"] = "{0} 個" },
            });
            using var loc = new LocalizationService(source, Settings());

            Assert.That(loc.GetPlural("item", 0), Is.EqualTo("No items"));
            Assert.That(loc.GetPlural("item", 1), Is.EqualTo("1 item"));
            Assert.That(loc.GetPlural("item", 1200), Is.EqualTo("1,200 items"));

            loc.SetLanguage("ja");
            Assert.That(loc.GetPlural("item", 1), Is.EqualTo("1 個"));
        }

        [Test]
        public void MissingKeyBehavior_IsConfigurable()
        {
            var source = new DictionarySource(new Dictionary<string, Dictionary<string, string>> { ["en"] = new() });
            using var bracketed = new LocalizationService(source, new LocalizationSettings { MissingKey = MissingKeyBehavior.Bracketed, WarnMissingKeys = false });
            using var empty = new LocalizationService(source, new LocalizationSettings { MissingKey = MissingKeyBehavior.Empty, WarnMissingKeys = false });

            Assert.That(bracketed.Get("menu.start"), Is.EqualTo("[menu.start]"));
            Assert.That(empty.Get("menu.start"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void PseudoLocalization_NotifiesChanged()
        {
            var source = new DictionarySource(new Dictionary<string, Dictionary<string, string>> { ["en"] = new() { ["start"] = "Start" } });
            using var loc = new LocalizationService(source, Settings());
            var changed = 0;
            using var _ = R3.ObservableSubscribeExtensions.Subscribe(loc.Changed, _ => changed++);

            loc.PseudoLocalization = true;
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(loc.Get("start"), Does.StartWith("[!!"));
        }

        [Test]
        public void AsyncSource_SwitchesAfterLoad_AndLastRequestWins()
        {
            var source = new ManualAsyncSource();
            using var loc = new LocalizationService(source, Settings());
            Assert.That(loc.IsLoading.CurrentValue, Is.True);

            var ready = loc.WaitReadyAsync();
            source.Pending["en"].TrySetResult(new Dictionary<string, string> { ["start"] = "Start" });
            ready.GetAwaiter().GetResult();
            Assert.That(loc.IsLoading.CurrentValue, Is.False);
            Assert.That(loc.Get("start"), Is.EqualTo("Start"));

            // 読み込み中に別の言語が要求されたら、先の要求は反映しない
            var first = loc.SetLanguageAsync("ja");
            Assert.That(loc.Language.CurrentValue, Is.EqualTo("en"));
            Assert.That(loc.IsLoading.CurrentValue, Is.True);
            var second = loc.SetLanguageAsync("en");
            Assert.That(second.GetAwaiter().GetResult(), Is.True);

            source.Pending["ja"].TrySetResult(new Dictionary<string, string> { ["start"] = "スタート" });
            Assert.That(first.GetAwaiter().GetResult(), Is.False);
            Assert.That(loc.Language.CurrentValue, Is.EqualTo("en"));
        }

        [Test]
        public void Fonts_MatchLanguageChain_ThenWildcard()
        {
            var jaFont = new LocalizationFont { Language = "ja" };
            var hantFont = new LocalizationFont { Language = "zh-Hant" };
            var defaultFont = new LocalizationFont { Language = "*" };
            var source = new DictionarySource(new Dictionary<string, Dictionary<string, string>>
            {
                ["en"] = new(), ["ja"] = new(), ["zh-Hant-TW"] = new(),
            });
            using var loc = new LocalizationService(source, Settings());
            var fonts = new LocalizedFontService(loc, new List<LocalizationFont> { defaultFont, jaFont, hantFont });

            Assert.That(fonts.GetFont(), Is.SameAs(defaultFont));
            loc.SetLanguage("ja");
            Assert.That(fonts.GetFont(), Is.SameAs(jaFont));
            loc.SetLanguage("zh-Hant-TW");
            Assert.That(fonts.GetFont(), Is.SameAs(hantFont));
        }

        [Test]
        public void LocalizedText_SwitchesFont_UnlessKeepFont()
        {
            var original = new Font("Original");
            var japanese = new Font("Japanese");
            var source = new DictionarySource(new Dictionary<string, Dictionary<string, string>>
            {
                ["en"] = new() { ["start"] = "Start" },
                ["ja"] = new() { ["start"] = "スタート" },
            });
            using var loc = new LocalizationService(source, Settings());
            var fonts = new LocalizedFontService(loc, new List<LocalizationFont> { new() { Language = "ja", Font = japanese } });

            var go = new GameObject("Label", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<Text>();
                text.font = original;
                var localized = go.AddComponent<LocalizedText>();
                // 先にサービスを渡す (Key の設定で表示が更新されるとき、未設定だと Loc.Service = 本物の ProjectContext を使ってしまう)
                localized.SetService(loc);
                localized.SetFontService(fonts);
                localized.Key = "start";
                localized.Refresh();
                Assert.That(text.text, Is.EqualTo("Start"));
                Assert.That(text.font, Is.SameAs(original));

                loc.SetLanguage("ja");
                localized.Refresh();
                Assert.That(text.text, Is.EqualTo("スタート"));
                Assert.That(text.font, Is.SameAs(japanese));

                loc.SetLanguage("en");
                localized.Refresh();
                Assert.That(text.font, Is.SameAs(original));

                localized.KeepFont = true;
                loc.SetLanguage("ja");
                localized.Refresh();
                Assert.That(text.font, Is.SameAs(original));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(japanese);
            }
        }

        [Test]
        public void Assets_FallBackToOtherLanguages()
        {
            var table = ScriptableObject.CreateInstance<LocalizationTable>();
            var enClip = AudioClip.Create("en", 1, 1, 44100, false);
            try
            {
                table.Languages = new List<string> { "en", "ja" };
                table.Assets.Add(new LocalizedAssetEntry { Key = "voice", Values = new List<Object> { enClip, null } });
                using var loc = new LocalizationService(new TableLocalizationSource(table), Settings("ja"));

                Assert.That(loc.GetAsset<AudioClip>("voice"), Is.SameAs(enClip));
                Assert.That(loc.GetAsset<Sprite>("voice"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(enClip);
                Object.DestroyImmediate(table);
            }
        }
    }

    public sealed class LocalizationCsvRoundTripTests
    {
        [Test]
        public void WriteThenImport_KeepsValues()
        {
            var values = new Dictionary<string, Dictionary<string, string>>
            {
                ["ja"] = new() { ["greeting"] = "こんにちは, \"世界\"\n2 行目", ["tab"] = "a\tb" },
                ["en"] = new() { ["greeting"] = "Hello" },
            };

            foreach (var delimiter in new[] { ',', '\t' })
            {
                var text = LocalizationCsv.Write(new[] { "ja", "en" }, values, delimiter);
                var table = ScriptableObject.CreateInstance<LocalizationTable>();
                try
                {
                    table.Languages = new List<string> { "en" };
                    var (added, updated) = LocalizationTableIO.Import(table, text);
                    Assert.That(added, Is.EqualTo(2));
                    Assert.That(updated, Is.Zero);
                    Assert.That(table.Languages, Is.EqualTo(new[] { "en", "ja" }));

                    var built = table.Build();
                    Assert.That(built["ja"]["greeting"], Is.EqualTo("こんにちは, \"世界\"\n2 行目"));
                    Assert.That(built["ja"]["tab"], Is.EqualTo("a\tb"));
                    Assert.That(built["en"]["greeting"], Is.EqualTo("Hello"));
                    Assert.That(built["en"].ContainsKey("tab"), Is.False);
                }
                finally
                {
                    Object.DestroyImmediate(table);
                }
            }
        }
    }

    public sealed class KeyGeneratorTests
    {
        [Test]
        public void CollidingKeys_GetStableSuffix_UnaffectedByOtherKeys()
        {
            var first = AbubuKeyGenerator.AssignIdentifiers(new[] { "menu_start", "menu.start" }, "LocKeys");
            var second = AbubuKeyGenerator.AssignIdentifiers(new[] { "menu_start", "menu.start", "zzz", "aaa" }, "LocKeys");

            Assert.That(first.Select(x => x.Name).Distinct().Count(), Is.EqualTo(2));
            foreach (var (name, key) in first) Assert.That(second, Does.Contain((name, key)));
        }

        [Test]
        public void Escape_HandlesControlCharacters()
        {
            Assert.That(AbubuKeyGenerator.Escape("a\"b\\c\nd" + (char)0x2028), Is.EqualTo("a\\\"b\\\\c\\u000Ad\\u2028"));
        }
    }
}
