using System.Collections.Generic;
using System.Text.RegularExpressions;
using Abubu.Localization;
using Abubu.Save;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    public sealed class LocalizationCsvTests
    {
        [Test]
        public void Parse_HandlesQuotesCommasAndNewlines()
        {
            var rows = LocalizationCsv.Parse("﻿key,ja\r\nhello,\"こんにちは, \"\"世界\"\"\"\r\nmulti,\"1行目\n2行目\"");
            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(rows[0], Is.EqualTo(new[] { "key", "ja" }));
            Assert.That(rows[1][1], Is.EqualTo("こんにちは, \"世界\""));
            Assert.That(rows[2][1], Is.EqualTo("1行目\n2行目"));
        }

        [Test]
        public void ReadInto_SkipsCommentsAndEmptyCells()
        {
            var output = new Dictionary<string, Dictionary<string, string>>();
            LocalizationCsv.ReadInto("key,ja,en\n# comment,x,y\nstart,スタート,\n\nquit,終了,Quit\n", output);

            Assert.That(output["ja"]["start"], Is.EqualTo("スタート"));
            Assert.That(output["ja"].ContainsKey("# comment"), Is.False);
            Assert.That(output["en"].ContainsKey("start"), Is.False);
            Assert.That(output["en"]["quit"], Is.EqualTo("Quit"));
        }
    }

    public sealed class LocalizationServiceTests
    {
        /// <summary>テスト用の取得元 (言語 → キー → 文字列)</summary>
        private sealed class DictionarySource : ILocalizationSource
        {
            private readonly Dictionary<string, Dictionary<string, string>> _values;
            public IReadOnlyList<string> Languages { get; }

            public DictionarySource(Dictionary<string, Dictionary<string, string>> values)
            {
                _values = values;
                Languages = new List<string>(values.Keys);
            }

            public IReadOnlyDictionary<string, string> Load(string language) =>
                _values.TryGetValue(language, out var v) ? v : new Dictionary<string, string>();
        }

        private static DictionarySource CreateSource() => new(new Dictionary<string, Dictionary<string, string>>
        {
            ["ja"] = new() { ["start"] = "スタート", ["score"] = "スコア: {0}", ["only.ja"] = "日本語だけ" },
            ["en"] = new() { ["start"] = "Start", ["score"] = "Score: {0}" },
            ["pt-BR"] = new() { ["start"] = "Iniciar" },
        });

        [Test]
        public void Get_ReturnsCurrentLanguage_AndFallsBack()
        {
            using var loc = new LocalizationService(CreateSource(), new LocalizationSettings { StartupLanguage = "en", RememberLanguage = false });

            Assert.That(loc.Language.CurrentValue, Is.EqualTo("en"));
            Assert.That(loc.Get("start"), Is.EqualTo("Start"));
            Assert.That(loc.Get("score", 10), Is.EqualTo("Score: 10"));
            // en に無いキーはフォールバック (先頭の ja) から取る
            Assert.That(loc.Get("only.ja"), Is.EqualTo("日本語だけ"));
        }

        [Test]
        public void MissingKey_ReturnsKey_AndWarnsOnce()
        {
            using var loc = new LocalizationService(CreateSource(), new LocalizationSettings { StartupLanguage = "ja", RememberLanguage = false });

            LogAssert.Expect(LogType.Warning, new Regex("キー \"strat\" の翻訳がありません.*\"start\""));
            Assert.That(loc.Get("strat"), Is.EqualTo("strat"));
            Assert.That(loc.Get("strat"), Is.EqualTo("strat"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SetLanguage_NotifiesAndMatchesRegion()
        {
            using var loc = new LocalizationService(CreateSource(), new LocalizationSettings { StartupLanguage = "ja", RememberLanguage = false });
            var changes = new List<string>();
            using var _ = loc.Language.Subscribe(changes.Add);

            Assert.That(loc.SetLanguage("pt"), Is.True);
            Assert.That(loc.Language.CurrentValue, Is.EqualTo("pt-BR"));
            Assert.That(loc.Get("start"), Is.EqualTo("Iniciar"));
            Assert.That(changes, Is.EqualTo(new[] { "ja", "pt-BR" }));

            LogAssert.Expect(LogType.Warning, new Regex("言語 \"xx\" はテーブルにありません"));
            Assert.That(loc.SetLanguage("xx"), Is.False);
        }

        [Test]
        public void SelectedLanguage_IsRemembered()
        {
            var save = new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer());
            var settings = new LocalizationSettings { StartupLanguage = "ja" };
            using (var loc = new LocalizationService(CreateSource(), settings, save)) loc.SetLanguage("en");

            using var reloaded = new LocalizationService(CreateSource(), settings, save);
            Assert.That(reloaded.Language.CurrentValue, Is.EqualTo("en"));
        }

        [Test]
        public void InvalidFormat_ReturnsRawText()
        {
            var source = new DictionarySource(new Dictionary<string, Dictionary<string, string>>
            {
                ["ja"] = new() { ["bad"] = "値: {1}" },
            });
            using var loc = new LocalizationService(source, new LocalizationSettings { RememberLanguage = false });

            LogAssert.Expect(LogType.Error, new Regex("\"bad\" の書式が不正です"));
            Assert.That(loc.Get("bad", 1), Is.EqualTo("値: {1}"));
        }

        [Test]
        public void Table_EntriesOverrideIncludes()
        {
            var common = ScriptableObject.CreateInstance<LocalizationTable>();
            common.Entries.Add(new LocalizationEntry { Key = "start", Values = new List<string> { "共通", "Common" } });
            common.Entries.Add(new LocalizationEntry { Key = "quit", Values = new List<string> { "終了", "" } });

            var table = ScriptableObject.CreateInstance<LocalizationTable>();
            table.Includes.Add(common);
            table.Entries.Add(new LocalizationEntry { Key = "start", Values = new List<string> { "はじめる", "Begin" } });

            try
            {
                var source = new TableLocalizationSource(table);
                Assert.That(source.Languages, Is.EqualTo(new[] { "ja", "en" }));
                Assert.That(source.Load("ja")["start"], Is.EqualTo("はじめる"));
                Assert.That(source.Load("ja")["quit"], Is.EqualTo("終了"));
                // 空文字は未翻訳として扱う
                Assert.That(source.Load("en").ContainsKey("quit"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(common);
            }
        }
    }
}
