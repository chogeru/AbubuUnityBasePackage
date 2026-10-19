using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        /// <summary>設定すると Write でこの例外を投げる</summary>
        public Exception WriteException;

        public bool TryRead(string key, out string text) => Data.TryGetValue(key, out text);

        public void Write(string key, string text)
        {
            if (WriteException != null) throw WriteException;
            if (ThrowOnWrite) throw new IOException("disk full");
            Data[key] = text;
        }

        public bool Exists(string key) => Data.ContainsKey(key);
        public void Delete(string key) => Data.Remove(key);
    }

    /// <summary>テスト用の、常に例外を投げるシリアライザ</summary>
    internal sealed class ThrowingSerializer : ISaveSerializer
    {
        public string Serialize<T>(T data) => throw new InvalidOperationException("cannot serialize");
        public T Deserialize<T>(string text) => throw new InvalidOperationException("cannot deserialize");
    }

    /// <summary>新しいビルドで保存されたデータの代わり (Progress より新しい v3)</summary>
    [Serializable, SaveVersion(3)]
    public sealed class ProgressV3
    {
        public int Coins;
        public int Gems;
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
        public void Migration_Missing_KeepsOldDataAsBroken()
        {
            new SaveService(_storage, new JsonUtilitySaveSerializer()).Save("p", new ProgressV1 { gold = 5 });
            var original = _storage.Data["p"];

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ProgressV1"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("マイグレーション"));
            _save.TryLoad<Progress>("p", out _);
            // 読めなかったデータは、この後の Save で上書きされても残る
            _save.Save("p", new Progress { Coins = 1 });

            Assert.That(_storage.Data["p" + SaveService.BrokenSuffix], Is.EqualTo(original));
        }

        [Test]
        public void Corrupted_RecoversFromBackup()
        {
            var folder = "AbubuTests_" + Guid.NewGuid().ToString("N");
            var storage = new FileSaveStorage(folder);
            try
            {
                var save = new SaveService(storage, new JsonUtilitySaveSerializer());
                save.Save("p", new Progress { Coins = 1 });
                save.Save("p", new Progress { Coins = 2 }); // ここで .bak に Coins = 1 が残る
                var backupPath = Path.Combine(storage.DirectoryPath, "p.json.bak");
                var goodBackup = File.ReadAllText(backupPath);
                File.WriteAllText(Path.Combine(storage.DirectoryPath, "p.json"), "{ broken");

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("読み込みに失敗"));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("復旧"));
                Assert.That(save.Load<Progress>("p").Coins, Is.EqualTo(1));
                // 本体も書き直されているので、次回はエラーなしで読める
                Assert.That(save.Load<Progress>("p").Coins, Is.EqualTo(1));
                // 復旧で壊れた本体が .bak に回されず、正常なバックアップが残っている
                Assert.That(File.ReadAllText(backupPath), Is.EqualTo(goodBackup));
                // 壊れた本体は調査用に退避されている
                Assert.That(File.ReadAllText(Path.Combine(storage.DirectoryPath, "p" + SaveService.BrokenSuffix + ".json")), Does.Contain("{ broken"));
            }
            finally
            {
                if (Directory.Exists(storage.DirectoryPath)) Directory.Delete(storage.DirectoryPath, true);
            }
        }

        [Test]
        public void WriteFailure_IsLogged_NotThrown_ReturnsFalse()
        {
            _storage.ThrowOnWrite = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            var result = true;
            Assert.DoesNotThrow(() => result = _save.Save("p", 1));
            Assert.That(result, Is.False);
        }

        [Test]
        public void SerializerException_ReturnsFalse_NotThrown()
        {
            var save = new SaveService(_storage, new ThrowingSerializer());
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            var result = true;
            Assert.DoesNotThrow(() => result = save.Save("p", 1));
            Assert.That(result, Is.False);
            Assert.That(_storage.Data.ContainsKey("p"), Is.False);
        }

        [Test]
        public void Save_ReturnsTrue_AndWritesFormatMarker()
        {
            Assert.That(_save.Save("p", 1), Is.True);
            StringAssert.Contains(SaveService.FormatMarker, _storage.Data["p"]);
        }

        [Test]
        public void EmptyKey_IsRejected()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("キーが空"));
            Assert.That(_save.Save("", 1), Is.False);
        }

        [Test]
        public void ForeignJson_IsNotLoaded()
        {
            _storage.Data["p"] = "{\"gold\":5}";
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("形式ではありません"));
            Assert.That(_save.TryLoad<Progress>("p", out _), Is.False);
        }

        [Test]
        public void LegacyEnvelopeWithoutMarker_IsStillLoaded()
        {
            // 目印 (format) が無かった頃の形式
            _storage.Data["p"] = "{\"version\":2,\"type\":\"" + typeof(Progress).FullName + "\",\"savedAt\":\"\",\"data\":\"{\\\"Coins\\\":3}\"}";
            Assert.That(_save.Load<Progress>("p").Coins, Is.EqualTo(3));
        }

        [Test]
        public void NewerVersion_IsRefused_AndProtected()
        {
            new SaveService(_storage, new JsonUtilitySaveSerializer()).Save("p", new ProgressV3 { Coins = 9, Gems = 4 });
            var original = _storage.Data["p"];

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("新しいバージョン"));
            Assert.That(_save.TryLoad<Progress>("p", out _), Is.False);
            // 元データは .newer に退避される
            Assert.That(_storage.Data["p" + SaveService.NewerSuffix], Is.EqualTo(original));

            // 上書きは拒否され、元データは残る
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("上書きしませんでした"));
            Assert.That(_save.Save("p", new Progress { Coins = 1 }), Is.False);
            Assert.That(_storage.Data["p"], Is.EqualTo(original));

            // ForceSave なら上書きでき、保護も解除される
            Assert.That(_save.ForceSave("p", new Progress { Coins = 1 }), Is.True);
            Assert.That(_save.Save("p", new Progress { Coins = 2 }), Is.True);
            Assert.That(_save.Load<Progress>("p").Coins, Is.EqualTo(2));
        }

        [Test]
        public void NewerVersion_SaveWithoutLoad_IsAlsoRefused()
        {
            new SaveService(_storage, new JsonUtilitySaveSerializer()).Save("p", new ProgressV3 { Coins = 9 });
            var original = _storage.Data["p"];

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("新しいバージョン"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("上書きしませんでした"));
            Assert.That(_save.Save("p", new Progress { Coins = 1 }), Is.False);
            Assert.That(_storage.Data["p"], Is.EqualTo(original));
        }

        [Test]
        public void Delete_RemovesSideKeys_AndClearsProtection()
        {
            new SaveService(_storage, new JsonUtilitySaveSerializer()).Save("p", new ProgressV3 { Coins = 9 });
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("新しいバージョン"));
            _save.TryLoad<Progress>("p", out _);
            _storage.Data["p" + SaveService.BrokenSuffix] = "x";

            _save.Delete("p");
            Assert.That(_storage.Data.Count, Is.Zero);
            Assert.That(_save.Save("p", new Progress { Coins = 1 }), Is.True);
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
            File.WriteAllText(Path.Combine(_storage.DirectoryPath, "a.json.tmp"), "partial");
            _storage.Delete("a");
            Assert.That(_storage.Exists("a"), Is.False);
            Assert.That(Directory.GetFiles(_storage.DirectoryPath), Is.Empty);
        }

        [Test]
        public void RestoreFromBackup_KeepsBackup()
        {
            _storage.Write("a", "old");
            _storage.Write("a", "new"); // .bak = "old"
            _storage.RestoreFromBackup("a");

            Assert.That(_storage.TryRead("a", out var text), Is.True);
            Assert.That(text, Is.EqualTo("old"));
            Assert.That(_storage.TryReadBackup("a", out var backup), Is.True);
            Assert.That(backup, Is.EqualTo("old"));
        }

        [Test]
        public void DefaultFolderName_IsAbubuSpecific()
        {
            Assert.That(new SaveSettings().FolderName, Is.EqualTo(SaveSettings.DefaultFolderName));
            Assert.That(SaveSettings.DefaultFolderName, Is.Not.EqualTo("Save"));
        }
    }

    /// <summary>テスト用の、別スレッドから使えるメモリ上ストレージ。書き込みを遅らせ、同じキーへの同時書き込みを検出する</summary>
    internal sealed class SlowThreadSafeStorage : IThreadSafeSaveStorage
    {
        public readonly ConcurrentDictionary<string, string> Data = new();
        public int WriteDelayMs = 5;
        public Exception WriteException;
        /// <summary>同じキーへの書き込みが重なった回数</summary>
        public int OverlapCount;
        private readonly ConcurrentDictionary<string, int> _writing = new();

        public bool TryRead(string key, out string text) => Data.TryGetValue(key, out text);

        public void Write(string key, string text)
        {
            if (_writing.AddOrUpdate(key, 1, (_, n) => n + 1) > 1) Interlocked.Increment(ref OverlapCount);
            try
            {
                Thread.Sleep(WriteDelayMs);
                if (WriteException != null) throw WriteException;
                Data[key] = text;
            }
            finally
            {
                _writing.AddOrUpdate(key, 0, (_, n) => n - 1);
            }
        }

        public bool Exists(string key) => Data.ContainsKey(key);
        public void Delete(string key) => Data.TryRemove(key, out _);
    }

    internal static class TaskTestUtil
    {
        /// <summary>Task の完了をフレームを進めながら待つ (await の継続はメインスレッドで動くため Wait() は使えない)</summary>
        public static IEnumerator WaitFor(Task task, float timeoutSeconds = 10f)
        {
            var start = DateTime.UtcNow;
            while (!task.IsCompleted)
            {
                if ((DateTime.UtcNow - start).TotalSeconds > timeoutSeconds) Assert.Fail("Task がタイムアウトしました");
                yield return null;
            }
            if (task.IsFaulted) throw task.Exception;
        }
    }

    public sealed class SaveTransformTests
    {
        private MemorySaveStorage _storage;

        [SetUp]
        public void SetUp() => _storage = new MemorySaveStorage();

        private SaveService Create(params ISaveTransform[] transforms) =>
            new(_storage, new JsonUtilitySaveSerializer(), null, new List<ISaveTransform>(transforms));

        [Test]
        public void GZip_RoundTrip_AndRecordsTransformId()
        {
            var save = Create(new GZipSaveTransform());
            Assert.That(save.Save("p", new Progress { Coins = 42 }), Is.True);
            StringAssert.Contains(GZipSaveTransform.TransformId, _storage.Data["p"]);
            StringAssert.DoesNotContain("Coins", _storage.Data["p"]);
            Assert.That(save.Load<Progress>("p").Coins, Is.EqualTo(42));
        }

        [Test]
        public void GZipThenAes_RoundTrip()
        {
            var save = Create(new GZipSaveTransform(), new AesSaveTransform("pass", "salt"));
            save.Save("p", new Progress { Coins = 7 });
            StringAssert.DoesNotContain("Coins", _storage.Data["p"]);
            Assert.That(Create(new GZipSaveTransform(), new AesSaveTransform("pass", "salt")).Load<Progress>("p").Coins, Is.EqualTo(7));
        }

        [Test]
        public void DataWithoutTransforms_StillLoads_WhenTransformsAreAdded()
        {
            Create().Save("p", new Progress { Coins = 3 });
            Assert.That(Create(new GZipSaveTransform(), new AesSaveTransform("pass", "salt")).Load<Progress>("p").Coins, Is.EqualTo(3));
        }

        [Test]
        public void LegacyEnvelopeWithoutTransformsField_StillLoads()
        {
            _storage.Data["p"] = "{\"format\":\"abubu.save\",\"version\":2,\"type\":\"\",\"savedAt\":\"\",\"data\":\"{\\\"Coins\\\":5}\"}";
            Assert.That(Create(new GZipSaveTransform()).Load<Progress>("p").Coins, Is.EqualTo(5));
        }

        [Test]
        public void GZipData_StillLoads_WhenCompressIsTurnedOff()
        {
            // SaveSettings.Compress を後から無効にした場合 (GZip が登録されていない) でも、圧縮済みのデータは読める
            Create(new GZipSaveTransform()).Save("p", new Progress { Coins = 3 });
            var plain = Create();
            SaveFailure failure = null;
            plain.SaveFailed += f => failure = f;

            Assert.That(plain.TryLoad<Progress>("p", out var loaded), Is.True);
            Assert.That(loaded.Coins, Is.EqualTo(3));
            Assert.That(failure, Is.Null);
            Assert.That(_storage.Data.ContainsKey("p" + SaveService.BrokenSuffix), Is.False);

            // 保存は登録どおり (変換なし) で行われる
            Assert.That(plain.Save("p", new Progress { Coins = 4 }), Is.True);
            StringAssert.DoesNotContain(GZipSaveTransform.TransformId, _storage.Data["p"]);
            Assert.That(Create(new GZipSaveTransform()).Load<Progress>("p").Coins, Is.EqualTo(4));
        }

        [Test]
        public void DataWithTransforms_FailsToLoad_WhenTransformIsNotRegistered()
        {
            Create(new AesSaveTransform("pass", "salt")).Save("p", new Progress { Coins = 3 });
            var original = _storage.Data["p"];
            var plain = Create();
            SaveFailure failure = null;
            plain.SaveFailed += f => failure = f;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("登録されていない"));
            Assert.That(plain.TryLoad<Progress>("p", out _), Is.False);
            Assert.That(failure.Reason, Is.EqualTo(SaveFailureReason.TransformFailed));
            // 鍵が無いだけでデータは壊れていないので、退避せずそのまま残す
            Assert.That(_storage.Data["p"], Is.EqualTo(original));
            Assert.That(_storage.Data.ContainsKey("p" + SaveService.BrokenSuffix), Is.False);
        }

        [Test]
        public void Aes_WrongKey_FailsAsTransformFailed()
        {
            Create(new AesSaveTransform("right", "salt")).Save("p", new Progress { Coins = 1 });
            var wrong = Create(new AesSaveTransform("wrong", "salt"));
            SaveFailure failure = null;
            wrong.SaveFailed += f => failure = f;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("読み込みに失敗"));
            Assert.That(wrong.TryLoad<Progress>("p", out _), Is.False);
            Assert.That(failure.Reason, Is.EqualTo(SaveFailureReason.TransformFailed));
        }

        [Test]
        public void Aes_DetectsTampering()
        {
            var aes = new AesSaveTransform("pass", "salt");
            var data = aes.Apply(System.Text.Encoding.UTF8.GetBytes("hello"));
            data[data.Length / 2] ^= 0xFF;
            Assert.Throws<System.Security.Cryptography.CryptographicException>(() => aes.Revert(data));
        }

        [Test]
        public void DuplicateTransformIds_Throw()
        {
            Assert.Throws<ArgumentException>(() => Create(new GZipSaveTransform(), new GZipSaveTransform()));
        }
    }

    public sealed class SaveFailureTests
    {
        private MemorySaveStorage _storage;
        private SaveService _save;
        private List<SaveFailure> _failures;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySaveStorage();
            _save = new SaveService(_storage, new JsonUtilitySaveSerializer());
            _failures = new List<SaveFailure>();
            _save.SaveFailed += _failures.Add;
        }

        [Test]
        public void DiskFull_IsReported_WithReasonAndKey()
        {
            _storage.ThrowOnWrite = true; // IOException("disk full")
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            Assert.That(_save.Save("p", 1), Is.False);

            Assert.That(_failures, Has.Count.EqualTo(1));
            Assert.That(_failures[0].Key, Is.EqualTo("p"));
            Assert.That(_failures[0].Operation, Is.EqualTo(SaveOperation.Save));
            Assert.That(_failures[0].Reason, Is.EqualTo(SaveFailureReason.DiskFull));
            Assert.That(_failures[0].Exception, Is.InstanceOf<IOException>());
        }

        [Test]
        public void AccessDenied_IsReported()
        {
            _storage.WriteException = new UnauthorizedAccessException("denied");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            _save.Save("p", 1);
            Assert.That(_failures[0].Reason, Is.EqualTo(SaveFailureReason.AccessDenied));
        }

        [Test]
        public void Serialization_IsReported()
        {
            var save = new SaveService(_storage, new ThrowingSerializer());
            SaveFailure failure = null;
            save.SaveFailed += f => failure = f;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            save.Save("p", 1);
            Assert.That(failure.Reason, Is.EqualTo(SaveFailureReason.SerializationFailed));
        }

        [Test]
        public void Corrupted_Load_IsReported_ButMissingIsNot()
        {
            Assert.That(_save.TryLoad<Progress>("none", out _), Is.False);
            Assert.That(_failures, Is.Empty);

            _storage.Data["p"] = "{ broken";
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("読み込みに失敗"));
            _save.TryLoad<Progress>("p", out _);
            Assert.That(_failures, Has.Count.EqualTo(1));
            Assert.That(_failures[0].Operation, Is.EqualTo(SaveOperation.Load));
            Assert.That(_failures[0].Reason, Is.EqualTo(SaveFailureReason.Corrupted));
        }

        [Test]
        public void NewerVersion_IsReported()
        {
            new SaveService(_storage, new JsonUtilitySaveSerializer()).Save("p", new ProgressV3 { Coins = 1 });
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("新しいバージョン"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("上書きしませんでした"));
            _save.Save("p", new Progress { Coins = 2 });
            Assert.That(_failures[0].Reason, Is.EqualTo(SaveFailureReason.NewerVersion));
        }

        [Test]
        public void HandlerException_DoesNotBreakSave()
        {
            _save.SaveFailed += _ => throw new InvalidOperationException("handler");
            _storage.ThrowOnWrite = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("handler"));
            Assert.DoesNotThrow(() => _save.Save("p", 1));
        }

        [Test]
        public void Classify_RecognizesErrorCodesAndMessages()
        {
            Assert.That(SaveErrors.Classify(new IOException("x", unchecked((int)0x80070070))), Is.EqualTo(SaveFailureReason.DiskFull));
            Assert.That(SaveErrors.Classify(new IOException("x", unchecked((int)0x80070027))), Is.EqualTo(SaveFailureReason.DiskFull));
            Assert.That(SaveErrors.Classify(new IOException("No space left on device")), Is.EqualTo(SaveFailureReason.DiskFull));
            Assert.That(SaveErrors.Classify(new IOException("locked")), Is.EqualTo(SaveFailureReason.IoError));
            Assert.That(SaveErrors.Classify(new UnauthorizedAccessException()), Is.EqualTo(SaveFailureReason.AccessDenied));
            Assert.That(SaveErrors.Classify(new InvalidOperationException()), Is.EqualTo(SaveFailureReason.Unknown));
        }
    }

    public sealed class SaveAsyncTests
    {
        [UnityTest]
        public IEnumerator FileStorage_SaveAsync_LoadAsync_RoundTrip()
        {
            var storage = new FileSaveStorage("AbubuTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var save = new SaveService(storage, new JsonUtilitySaveSerializer(), null, new List<ISaveTransform> { new GZipSaveTransform() });
                var saveTask = save.SaveAsync("p", new Progress { Coins = 11 });
                yield return TaskTestUtil.WaitFor(saveTask);
                Assert.That(saveTask.Result.Success, Is.True);

                var loadTask = save.LoadAsync<Progress>("p");
                yield return TaskTestUtil.WaitFor(loadTask);
                Assert.That(loadTask.Result.Success, Is.True);
                Assert.That(loadTask.Result.Value.Coins, Is.EqualTo(11));

                var missing = save.LoadAsync<Progress>("none");
                yield return TaskTestUtil.WaitFor(missing);
                Assert.That(missing.Result.NotFound, Is.True);
            }
            finally
            {
                if (Directory.Exists(storage.DirectoryPath)) Directory.Delete(storage.DirectoryPath, true);
            }
        }

        [UnityTest]
        public IEnumerator SameKey_WritesAreSerialized_InCallOrder()
        {
            var storage = new SlowThreadSafeStorage { WriteDelayMs = 3 };
            var save = new SaveService(storage, new JsonUtilitySaveSerializer());
            var tasks = new List<Task<SaveResult>>();
            for (var i = 1; i <= 20; i++)
            {
                tasks.Add(save.SaveAsync("p", new Progress { Coins = i }));
                tasks.Add(save.SaveAsync("q" + (i % 3), new Progress { Coins = i })); // 別のキーは並行してよい
            }
            Assert.That(save.HasPendingWrites, Is.True);
            yield return TaskTestUtil.WaitFor(Task.WhenAll(tasks));

            Assert.That(tasks.TrueForAll(t => t.Result.Success), Is.True);
            Assert.That(storage.OverlapCount, Is.Zero);
            Assert.That(save.Load<Progress>("p").Coins, Is.EqualTo(20));
        }

        [Test]
        public void SyncLoad_AfterSaveAsync_SeesLatestData()
        {
            var storage = new SlowThreadSafeStorage { WriteDelayMs = 20 };
            var save = new SaveService(storage, new JsonUtilitySaveSerializer());
            _ = save.SaveAsync("p", new Progress { Coins = 1 });
            _ = save.SaveAsync("p", new Progress { Coins = 2 });
            // 同期版は書き込み途中のキーを待ってから読む
            Assert.That(save.Load<Progress>("p").Coins, Is.EqualTo(2));
        }

        [Test]
        public void Flush_WaitsForAllWrites()
        {
            var storage = new SlowThreadSafeStorage { WriteDelayMs = 20 };
            var save = new SaveService(storage, new JsonUtilitySaveSerializer());
            _ = save.SaveAsync("a", 1);
            _ = save.SaveAsync("b", 2);
            save.Flush();
            Assert.That(save.HasPendingWrites, Is.False);
            Assert.That(storage.Data.ContainsKey("a") && storage.Data.ContainsKey("b"), Is.True);
        }

        [Test]
        public void NonThreadSafeStorage_CompletesSynchronously()
        {
            var save = new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer());
            var task = save.SaveAsync("p", 5);
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.Success, Is.True);
            var load = save.LoadAsync<int>("p");
            Assert.That(load.IsCompleted, Is.True);
            Assert.That(load.Result.Value, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator SaveAsync_Failure_ReturnsResult_AndRaisesEvent()
        {
            var storage = new SlowThreadSafeStorage { WriteException = new IOException("disk full") };
            var save = new SaveService(storage, new JsonUtilitySaveSerializer());
            SaveFailure failure = null;
            save.SaveFailed += f => failure = f;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            var task = save.SaveAsync("p", 1);
            yield return TaskTestUtil.WaitFor(task);

            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.Reason, Is.EqualTo(SaveFailureReason.DiskFull));
            Assert.That(failure, Is.SameAs(task.Result.Failure));
        }

        [UnityTest]
        public IEnumerator SaveAsync_RespectsNewerVersionProtection()
        {
            var storage = new SlowThreadSafeStorage();
            new SaveService(storage, new JsonUtilitySaveSerializer()).Save("p", new ProgressV3 { Coins = 9 });
            var original = storage.Data["p"];
            var save = new SaveService(storage, new JsonUtilitySaveSerializer());

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("新しいバージョン"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("上書きしませんでした"));
            var task = save.SaveAsync("p", new Progress { Coins = 1 });
            yield return TaskTestUtil.WaitFor(task);

            Assert.That(task.Result.Reason, Is.EqualTo(SaveFailureReason.NewerVersion));
            Assert.That(storage.Data["p"], Is.EqualTo(original));
        }

        [Test]
        public void Extension_FallsBackToSync_ForImplementationsWithoutAsync()
        {
            ISaveService save = new SyncOnlySave();
            Assert.That(save.SaveAsync("p", 3).Result.Success, Is.True);
            Assert.That(save.LoadAsync<int>("p").Result.Value, Is.EqualTo(3));
        }

        /// <summary>IAsyncSaveService を実装していない ISaveService</summary>
        private sealed class SyncOnlySave : ISaveService
        {
            private readonly SaveService _inner = new(new MemorySaveStorage(), new JsonUtilitySaveSerializer());
            public bool Save<T>(string key, T data) => _inner.Save(key, data);
            public bool ForceSave<T>(string key, T data) => _inner.ForceSave(key, data);
            public bool TryLoad<T>(string key, out T data) => _inner.TryLoad(key, out data);
            public T Load<T>(string key, T defaultValue = default) => _inner.Load(key, defaultValue);
            public bool Exists(string key) => _inner.Exists(key);
            public void Delete(string key) => _inner.Delete(key);
        }
    }
}
