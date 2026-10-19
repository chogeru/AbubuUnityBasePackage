using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Abubu.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    /// <summary>圧縮の切り替え・鍵の無い暗号化データの扱い</summary>
    public sealed class SaveTransformCompatibilityTests
    {
        private FileSaveStorage _storage;

        [SetUp]
        public void SetUp() => _storage = new FileSaveStorage("AbubuTests_" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_storage.DirectoryPath)) Directory.Delete(_storage.DirectoryPath, true);
        }

        private SaveService Create(params ISaveTransform[] transforms) =>
            new(_storage, new JsonUtilitySaveSerializer(), null, new List<ISaveTransform>(transforms));

        [UnityTest]
        public IEnumerator GZipData_LoadsAsync_WhenCompressIsTurnedOff()
        {
            Assert.That(Create(new GZipSaveTransform()).Save("p", new Progress { Coins = 8 }), Is.True);

            var task = Create().LoadAsync<Progress>("p");
            yield return TaskTestUtil.WaitFor(task);
            Assert.That(task.Result.Success, Is.True);
            Assert.That(task.Result.Value.Coins, Is.EqualTo(8));
        }

        [Test]
        public void AesData_WithoutKey_IsReportedAsTransformFailed_AndLeftUntouched()
        {
            // .bak に暗号化前の古いデータが残っている状態を作る
            Assert.That(Create().Save("p", new Progress { Coins = 1 }), Is.True);
            Assert.That(Create(new AesSaveTransform("pass", "salt")).Save("p", new Progress { Coins = 2 }), Is.True);
            var mainPath = Path.Combine(_storage.DirectoryPath, "p.json");
            var main = File.ReadAllText(mainPath);
            var backup = File.ReadAllText(mainPath + ".bak");

            var noKey = Create();
            var failures = new List<SaveFailure>();
            noKey.SaveFailed += failures.Add;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("登録されていない"));
            Assert.That(noKey.TryLoad<Progress>("p", out _), Is.False);

            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0].Reason, Is.EqualTo(SaveFailureReason.TransformFailed));
            // 古いバックアップで本体を戻したり、.broken に退避したりしない
            Assert.That(File.ReadAllText(mainPath), Is.EqualTo(main));
            Assert.That(File.ReadAllText(mainPath + ".bak"), Is.EqualTo(backup));
            Assert.That(File.Exists(Path.Combine(_storage.DirectoryPath, "p" + SaveService.BrokenSuffix + ".json")), Is.False);

            // 鍵を登録すれば読める
            Assert.That(Create(new AesSaveTransform("pass", "salt")).Load<Progress>("p").Coins, Is.EqualTo(2));
        }
    }

    /// <summary>読み込みに必要な変換 (暗号化の鍵) が登録されていないデータを、Save による上書きから守る</summary>
    public sealed class SaveMissingTransformProtectionTests
    {
        private static readonly System.Text.RegularExpressions.Regex MissingError = new("登録されていない");
        private static readonly System.Text.RegularExpressions.Regex RefusedError = new("上書きしませんでした");

        private static SaveService Plain(ISaveStorage storage) => new(storage, new JsonUtilitySaveSerializer());

        private static SaveService Encrypted(ISaveStorage storage) =>
            new(storage, new JsonUtilitySaveSerializer(), null, new List<ISaveTransform> { new AesSaveTransform("pass", "salt") });

        [Test]
        public void SaveWithoutLoad_IsRefused_AndDataIsKept()
        {
            var storage = new MemorySaveStorage();
            Encrypted(storage).Save("p", new Progress { Coins = 7 });
            var original = storage.Data["p"];
            var save = Plain(storage);
            var failures = new List<SaveFailure>();
            save.SaveFailed += failures.Add;

            LogAssert.Expect(LogType.Error, MissingError);
            LogAssert.Expect(LogType.Error, RefusedError);
            Assert.That(save.Save("p", new Progress { Coins = 1 }), Is.False);
            Assert.That(failures[0].Reason, Is.EqualTo(SaveFailureReason.TransformFailed));
            Assert.That(storage.Data["p"], Is.EqualTo(original));
            Assert.That(Encrypted(storage).Load<Progress>("p").Coins, Is.EqualTo(7));
        }

        [Test]
        public void SaveAfterFailedLoad_IsRefused_UntilForceSave()
        {
            var storage = new MemorySaveStorage();
            Encrypted(storage).Save("p", new Progress { Coins = 7 });
            var original = storage.Data["p"];
            var save = Plain(storage);

            LogAssert.Expect(LogType.Error, MissingError);
            Assert.That(save.TryLoad<Progress>("p", out _), Is.False);

            LogAssert.Expect(LogType.Error, RefusedError);
            Assert.That(save.Save("p", new Progress { Coins = 1 }), Is.False);
            Assert.That(storage.Data["p"], Is.EqualTo(original));

            // ForceSave なら上書きでき、保護も解除される
            Assert.That(save.ForceSave("p", new Progress { Coins = 2 }), Is.True);
            Assert.That(save.Save("p", new Progress { Coins = 3 }), Is.True);
            Assert.That(save.Load<Progress>("p").Coins, Is.EqualTo(3));
        }

        [Test]
        public void Delete_ClearsProtection()
        {
            var storage = new MemorySaveStorage();
            Encrypted(storage).Save("p", new Progress { Coins = 7 });
            var save = Plain(storage);

            LogAssert.Expect(LogType.Error, MissingError);
            save.TryLoad<Progress>("p", out _);
            save.Delete("p");
            Assert.That(save.Save("p", new Progress { Coins = 1 }), Is.True);
        }

        [Test]
        public void WrongKey_IsNotProtected()
        {
            // 鍵の違い (登録はされている) は従来どおり壊れたデータとして扱い、上書きは止めない
            var storage = new MemorySaveStorage();
            Encrypted(storage).Save("p", new Progress { Coins = 7 });
            var wrong = new SaveService(storage, new JsonUtilitySaveSerializer(), null,
                new List<ISaveTransform> { new AesSaveTransform("wrong", "salt") });
            Assert.That(wrong.Save("p", new Progress { Coins = 1 }), Is.True);
        }

        [UnityTest]
        public IEnumerator SaveAsync_IsRefused_AndDataIsKept()
        {
            var storage = new SlowThreadSafeStorage();
            Encrypted(storage).Save("p", new Progress { Coins = 7 });
            var original = storage.Data["p"];
            var save = Plain(storage);

            LogAssert.Expect(LogType.Error, MissingError);
            LogAssert.Expect(LogType.Error, RefusedError);
            var task = save.SaveAsync("p", new Progress { Coins = 1 });
            yield return TaskTestUtil.WaitFor(task);

            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.Reason, Is.EqualTo(SaveFailureReason.TransformFailed));
            Assert.That(storage.Data["p"], Is.EqualTo(original));

            // ForceSaveAsync なら上書きできる
            var force = save.ForceSaveAsync("p", new Progress { Coins = 2 });
            yield return TaskTestUtil.WaitFor(force);
            Assert.That(force.Result.Success, Is.True);
            Assert.That(save.Load<Progress>("p").Coins, Is.EqualTo(2));
        }

        [Test]
        public void SlotScope_SaveIsRefused()
        {
            var storage = new MemorySaveStorage();
            var root = Plain(storage);
            var slots = new SaveSlotService(root, storage);
            slots.Switch(1);
            // スロットのデータだけが暗号化されている (鍵を登録しなくなったビルドで開いた状態)
            var storageKey = slots.ToStorageKey("1", "p");
            Encrypted(storage).Save(storageKey, new Progress { Coins = 7 });
            var original = storage.Data[storageKey];
            var failures = new List<SaveFailure>();
            root.SaveFailed += failures.Add;

            LogAssert.Expect(LogType.Error, MissingError);
            LogAssert.Expect(LogType.Error, RefusedError);
            Assert.That(slots.Active.Save("p", new Progress { Coins = 1 }), Is.False);
            Assert.That(failures[0].Key, Is.EqualTo(storageKey));
            Assert.That(failures[0].Reason, Is.EqualTo(SaveFailureReason.TransformFailed));
            Assert.That(storage.Data[storageKey], Is.EqualTo(original));
        }
    }

    /// <summary>FileSaveStorage のファイル名の衝突と、以前の形式のファイル名の互換</summary>
    public sealed class FileSaveStorageFileNameTests
    {
        private FileSaveStorage _storage;

        [SetUp]
        public void SetUp() => _storage = new FileSaveStorage("AbubuTests_" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_storage.DirectoryPath)) Directory.Delete(_storage.DirectoryPath, true);
        }

        [Test]
        public void SlotKey_AndSimilarUserKey_UseDifferentFiles()
        {
            _storage.Write("slot/1/x", "slot");
            _storage.Write("slot_1_x", "user");
            _storage.Write("slot%2F1%2Fx", "escaped");

            Assert.That(_storage.TryRead("slot/1/x", out var slot) && slot == "slot", Is.True);
            Assert.That(_storage.TryRead("slot_1_x", out var user) && user == "user", Is.True);
            Assert.That(_storage.TryRead("slot%2F1%2Fx", out var escaped) && escaped == "escaped", Is.True);
        }

        [Test]
        public void LegacyFileName_IsStillRead_UntilRewritten()
        {
            // 以前のバージョンは "/" を "_" にしたファイル名で保存していた
            Directory.CreateDirectory(_storage.DirectoryPath);
            var legacyPath = Path.Combine(_storage.DirectoryPath, "slot_1_x.json");
            File.WriteAllText(legacyPath, "legacy");

            Assert.That(_storage.Exists("slot/1/x"), Is.True);
            Assert.That(_storage.TryRead("slot/1/x", out var text) && text == "legacy", Is.True);

            // 書き込みは新しい形式のファイル名に行い、以前のファイルには触らない
            _storage.Write("slot/1/x", "new");
            Assert.That(_storage.TryRead("slot/1/x", out text) && text == "new", Is.True);
            Assert.That(File.ReadAllText(legacyPath), Is.EqualTo("legacy"));

            // Delete は両方消す (消したデータが以前のファイルから復活しない)
            _storage.Delete("slot/1/x");
            Assert.That(_storage.Exists("slot/1/x"), Is.False);
            Assert.That(File.Exists(legacyPath), Is.False);
        }

        [Test]
        public void LegacySlotData_IsLoadedThroughSaveService()
        {
            var save = new SaveService(_storage, new JsonUtilitySaveSerializer());
            var slots = new SaveSlotService(save, _storage);
            slots.GetScope(1).Save("p", new Progress { Coins = 5 });
            var storageKey = slots.ToStorageKey("1", "p");

            // 以前のバージョンで保存されたファイルの状態に戻す
            var newPath = Path.Combine(_storage.DirectoryPath, "slot%2F1%2Fp.json");
            Assert.That(File.Exists(newPath), Is.True);
            File.Move(newPath, Path.Combine(_storage.DirectoryPath, storageKey.Replace('/', '_') + ".json"));

            var reloaded = new SaveSlotService(new SaveService(_storage, new JsonUtilitySaveSerializer()), _storage);
            Assert.That(reloaded.GetScope(1).Load<Progress>("p").Coins, Is.EqualTo(5));
        }
    }

    /// <summary>プレイ時間の加算 (毎フレーム呼ばれても毎回は書き込まない)</summary>
    public sealed class SaveSlotPlayTimeTests
    {
        [Test]
        public void SmallDeltas_AreBuffered_UntilInterval()
        {
            var storage = new MemorySaveStorage();
            var options = new SaveSlotOptions { PlayTimeSaveInterval = 10f };
            var slots = new SaveSlotService(new SaveService(storage, new JsonUtilitySaveSerializer()), storage, options);
            slots.Switch(1);

            for (var i = 0; i < 3; i++) Assert.That(slots.AddPlayTime("1", TimeSpan.FromSeconds(1)), Is.True);
            // メモリ上はすぐ反映される
            Assert.That(slots.TryGetSlot("1", out var info) && info.PlayTime == TimeSpan.FromSeconds(3), Is.True);
            // まだ保存されていない
            Assert.That(Reload(storage, options).TryGetSlot("1", out var saved) && saved.PlayTimeSeconds == 0, Is.True);

            // 間隔分たまったら保存される
            slots.AddPlayTime("1", TimeSpan.FromSeconds(8));
            Assert.That(Reload(storage, options).TryGetSlot("1", out saved) && saved.PlayTime == TimeSpan.FromSeconds(11), Is.True);

            // ほかの理由で一覧を保存した時にも一緒に保存される
            slots.AddPlayTime("1", TimeSpan.FromSeconds(1));
            slots.Active.Save("p", 1);
            Assert.That(Reload(storage, options).TryGetSlot("1", out saved) && saved.PlayTime == TimeSpan.FromSeconds(12), Is.True);
        }

        [Test]
        public void ZeroInterval_SavesEveryTime()
        {
            var storage = new MemorySaveStorage();
            var options = new SaveSlotOptions { PlayTimeSaveInterval = 0f };
            var slots = new SaveSlotService(new SaveService(storage, new JsonUtilitySaveSerializer()), storage, options);
            slots.Switch(1);
            slots.AddPlayTime("1", TimeSpan.FromSeconds(0.5));
            Assert.That(Reload(storage, options).TryGetSlot("1", out var saved) && saved.PlayTimeSeconds == 0.5, Is.True);
        }

        private static SaveSlotService Reload(MemorySaveStorage storage, SaveSlotOptions options) =>
            new(new SaveService(storage, new JsonUtilitySaveSerializer()), storage, options);
    }
}
