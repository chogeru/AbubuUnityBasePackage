using System;
using System.Collections.Generic;
using System.IO;
using Abubu.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    /// <summary>テスト用のメモリ上ストレージ</summary>
    internal sealed class MemorySaveStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Data = new();
        public bool ThrowOnWrite;

        public bool TryRead(string key, out string text) => Data.TryGetValue(key, out text);

        public void Write(string key, string text)
        {
            if (ThrowOnWrite) throw new IOException("disk full");
            Data[key] = text;
        }

        public bool Exists(string key) => Data.ContainsKey(key);
        public void Delete(string key) => Data.Remove(key);
    }

    [Serializable]
    public sealed class ProgressV1
    {
        public int gold;
    }

    [Serializable, SaveVersion(2)]
    public sealed class Progress
    {
        public int Coins;
    }

    public sealed class ProgressV1ToV2 : SaveMigration<Progress>
    {
        public override int FromVersion => 1;
        public override string Migrate(string json) => json.Replace("\"gold\"", "\"Coins\"");
    }

    public sealed class SaveServiceTests
    {
        private MemorySaveStorage _storage;
        private SaveService _save;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySaveStorage();
            _save = new SaveService(_storage, new JsonUtilitySaveSerializer());
        }

        [Test]
        public void Class_RoundTrip()
        {
            _save.Save("p", new Progress { Coins = 42 });
            Assert.That(_save.Load<Progress>("p").Coins, Is.EqualTo(42));
        }

        [Test]
        public void Primitive_RoundTrip()
        {
            _save.Save("int", 1200);
            _save.Save("str", "hello");
            _save.Save("list", new List<int> { 1, 2, 3 });

            Assert.That(_save.Load("int", 0), Is.EqualTo(1200));
            Assert.That(_save.Load("str", ""), Is.EqualTo("hello"));
            Assert.That(_save.Load<List<int>>("list"), Is.EqualTo(new List<int> { 1, 2, 3 }));
        }

        [Test]
        public void Missing_ReturnsDefault()
        {
            Assert.That(_save.TryLoad<Progress>("none", out _), Is.False);
            Assert.That(_save.Load("none", 7), Is.EqualTo(7));
        }

        [Test]
        public void Migration_UpgradesOldData_AndResaves()
        {
            // v1 形式で保存されたデータ (型は ProgressV1 だが、キーは同じ "p")
            new SaveService(_storage, new JsonUtilitySaveSerializer()).Save("p", new ProgressV1 { gold = 5 });
            var migrated = new SaveService(_storage, new JsonUtilitySaveSerializer(), new List<ISaveMigration> { new ProgressV1ToV2() });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ProgressV1"));
            Assert.That(migrated.Load<Progress>("p").Coins, Is.EqualTo(5));
            // 読み込み時に新形式で保存し直されている
            StringAssert.Contains("\"version\":2", _storage.Data["p"]);
        }

        [Test]
        public void Migration_Missing_FailsWithError()
        {
            new SaveService(_storage, new JsonUtilitySaveSerializer()).Save("p", new ProgressV1 { gold = 5 });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ProgressV1"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("マイグレーション"));
            Assert.That(_save.TryLoad<Progress>("p", out _), Is.False);
        }

        [Test]
        public void Corrupted_ReturnsFalse_WithError()
        {
            _storage.Data["p"] = "{ this is not json";
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("読み込みに失敗"));
            Assert.That(_save.TryLoad<Progress>("p", out _), Is.False);
        }

        [Test]
        public void WriteFailure_IsLogged_NotThrown()
        {
            _storage.ThrowOnWrite = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            Assert.DoesNotThrow(() => _save.Save("p", 1));
        }
    }

    public sealed class FileSaveStorageTests
    {
        private string _folder;
        private FileSaveStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _folder = "AbubuTests_" + Guid.NewGuid().ToString("N");
            _storage = new FileSaveStorage(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_storage.DirectoryPath)) Directory.Delete(_storage.DirectoryPath, true);
        }

        [Test]
        public void Write_Read_Overwrite()
        {
            _storage.Write("a", "1");
            _storage.Write("a", "2");
            Assert.That(_storage.TryRead("a", out var text), Is.True);
            Assert.That(text, Is.EqualTo("2"));
        }

        [Test]
        public void MainFileMissing_RecoversFromBackup()
        {
            _storage.Write("a", "old");
            _storage.Write("a", "new"); // ここで .bak に "old" が残る
            File.Delete(Path.Combine(_storage.DirectoryPath, "a.json"));

            Assert.That(_storage.TryRead("a", out var text), Is.True);
            Assert.That(text, Is.EqualTo("old"));
        }

        [Test]
        public void InvalidKeyCharacters_AreSanitized()
        {
            _storage.Write("slot/1:*", "x");
            Assert.That(_storage.Exists("slot/1:*"), Is.True);
        }

        [Test]
        public void Delete_RemovesBackupToo()
        {
            _storage.Write("a", "1");
            _storage.Write("a", "2");
            _storage.Delete("a");
            Assert.That(_storage.Exists("a"), Is.False);
        }
    }
}
