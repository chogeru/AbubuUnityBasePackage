using System;
using System.Collections.Generic;
using Abubu.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    public sealed class SaveSlotServiceTests
    {
        private MemorySaveStorage _storage;
        private SaveService _root;
        private SaveSlotService _slots;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySaveStorage();
            _root = new SaveService(_storage, new JsonUtilitySaveSerializer());
            _slots = new SaveSlotService(_root, _storage);
        }

        [Test]
        public void NoSlotSelected_ActiveWritesToSameKeysAsBefore()
        {
            Assert.That(_slots.ActiveSlotId, Is.Null);
            _slots.Active.Save("p", new Progress { Coins = 1 });

            // スロット未選択なら、これまでどおりのキーにそのまま保存される
            Assert.That(_storage.Data.ContainsKey("p"), Is.True);
            Assert.That(_root.Load<Progress>("p").Coins, Is.EqualTo(1));
            Assert.That(_slots.GetSlots(), Is.Empty);
        }

        [Test]
        public void Slots_AreIsolated()
        {
            _slots.Switch(1);
            _slots.Active.Save("p", new Progress { Coins = 1 });
            _slots.Switch(2);
            Assert.That(_slots.Active.Exists("p"), Is.False);
            _slots.Active.Save("p", new Progress { Coins = 2 });

            _slots.Switch(1);
            Assert.That(_slots.Active.Load<Progress>("p").Coins, Is.EqualTo(1));
            Assert.That(_slots.GetScope(2).Load<Progress>("p").Coins, Is.EqualTo(2));
            Assert.That(_root.Exists("p"), Is.False);
        }

        [Test]
        public void GlobalArea_IsUnaffectedBySlotSwitch()
        {
            _root.Save("volume", 0.5f);
            _slots.Switch("a");
            _slots.Active.Save("volume", 0.1f);

            Assert.That(_root.Load("volume", 0f), Is.EqualTo(0.5f));
            Assert.That(_slots.Global.Load("volume", 0f), Is.EqualTo(0.5f));
            Assert.That(_slots.Global.SlotId, Is.Null);
            Assert.That(_slots.Active.Load("volume", 0f), Is.EqualTo(0.1f));
        }

        [Test]
        public void ActiveScope_FollowsSwitch()
        {
            var active = _slots.Active;
            _slots.Switch(1);
            active.Save("p", 1);
            _slots.Switch(2);
            Assert.That(active.SlotId, Is.EqualTo("2"));
            Assert.That(active.Exists("p"), Is.False);
        }

        [Test]
        public void Metadata_IsUpdated_AndPersisted()
        {
            var before = DateTime.UtcNow.AddSeconds(-1);
            Assert.That(_slots.Switch(1), Is.True);
            Assert.That(_slots.TryGetSlot("1", out var created), Is.True);
            Assert.That(created.LastSavedAt, Is.Null);
            Assert.That(created.CreatedAt, Is.GreaterThanOrEqualTo(before));

            _slots.Active.Save("p", new Progress { Coins = 1 });
            _slots.SetSummary("1", "Stage 3");
            _slots.AddPlayTime("1", TimeSpan.FromMinutes(5));
            _slots.AddPlayTime("1", TimeSpan.FromMinutes(2));

            // 別のインスタンス (= 次回起動) からも読める
            var reloaded = new SaveSlotService(new SaveService(_storage, new JsonUtilitySaveSerializer()), _storage);
            Assert.That(reloaded.TryGetSlot("1", out var info), Is.True);
            Assert.That(info.LastSavedAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(info.Summary, Is.EqualTo("Stage 3"));
            Assert.That(info.PlayTime, Is.EqualTo(TimeSpan.FromMinutes(7)));
            Assert.That(info.GameVersion, Is.EqualTo(Application.version));
            Assert.That(info.Keys, Is.EqualTo(new List<string> { "p" }));
            Assert.That(reloaded.LastSelectedSlotId, Is.EqualTo("1"));
            Assert.That(reloaded.ActiveSlotId, Is.Null); // 起動しただけでは選択されない
        }

        [Test]
        public void GetSlots_ReturnsCopies()
        {
            _slots.Create("1", "original");
            _slots.GetSlots()[0].Summary = "changed";
            _slots.TryGetSlot("1", out var info);
            Assert.That(info.Summary, Is.EqualTo("original"));
        }

        [Test]
        public void Create_Existing_Fails()
        {
            Assert.That(_slots.Create(1), Is.True);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("すでにあります"));
            Assert.That(_slots.Create(1), Is.False);
        }

        [Test]
        public void Copy_DuplicatesData_Independently()
        {
            _slots.Switch(1);
            _slots.Active.Save("p", new Progress { Coins = 1 });
            _slots.SetSummary("1", "s");

            Assert.That(_slots.Copy(1, 2), Is.True);
            Assert.That(_slots.GetScope(2).Load<Progress>("p").Coins, Is.EqualTo(1));
            Assert.That(_slots.TryGetSlot("2", out var info) && info.Summary == "s", Is.True);

            _slots.GetScope(2).Save("p", new Progress { Coins = 9 });
            Assert.That(_slots.GetScope(1).Load<Progress>("p").Coins, Is.EqualTo(1));
        }

        [Test]
        public void Copy_ToExisting_RequiresOverwrite()
        {
            _slots.GetScope(1).Save("p", new Progress { Coins = 1 });
            _slots.GetScope(2).Save("p", new Progress { Coins = 2 });
            _slots.GetScope(2).Save("only2", 5);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("すでにあります"));
            Assert.That(_slots.Copy(1, 2), Is.False);
            Assert.That(_slots.GetScope(2).Load<Progress>("p").Coins, Is.EqualTo(2));

            Assert.That(_slots.Copy(1, 2, overwrite: true), Is.True);
            Assert.That(_slots.GetScope(2).Load<Progress>("p").Coins, Is.EqualTo(1));
            Assert.That(_slots.GetScope(2).Exists("only2"), Is.False);
        }

        [Test]
        public void Delete_RemovesDataAndMetadata_AndClearsActive()
        {
            string changedTo = "unset";
            _slots.Switch(1);
            _slots.ActiveSlotChanged += id => changedTo = id;
            _slots.Active.Save("p", 1);
            var dataKey = _slots.ToStorageKey("1", "p");
            Assert.That(_storage.Data.ContainsKey(dataKey), Is.True);

            Assert.That(_slots.Delete(1), Is.True);
            Assert.That(_storage.Data.ContainsKey(dataKey), Is.False);
            Assert.That(_slots.Exists(1), Is.False);
            Assert.That(_slots.ActiveSlotId, Is.Null);
            Assert.That(changedTo, Is.Null);
            Assert.That(_slots.LastSelectedSlotId, Is.Null);
        }

        [Test]
        public void Switch_WithoutCreate_FailsForMissingSlot()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ありません"));
            Assert.That(_slots.Switch("x", createIfMissing: false), Is.False);
            Assert.That(_slots.ActiveSlotId, Is.Null);
        }

        [Test]
        public void SlotId_IsValidated_AndNormalized()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("不正"));
            Assert.That(_slots.Switch("a/b"), Is.False);
            Assert.Throws<ArgumentException>(() => _slots.GetScope("a_b"));

            Assert.That(_slots.Switch("Slot-A"), Is.True);
            Assert.That(_slots.ActiveSlotId, Is.EqualTo("slot-a"));
            Assert.That(_slots.Exists("SLOT-A"), Is.True);
        }

        [Test]
        public void ActiveSaveAsync_UpdatesMetadata()
        {
            _slots.Switch(1);
            var result = _slots.Active.SaveAsync("p", 3);
            // MemorySaveStorage は別スレッド非対応なので同期で完了する
            Assert.That(result.IsCompleted && result.Result.Success, Is.True);
            Assert.That(_slots.TryGetSlot("1", out var info) && info.Keys.Contains("p"), Is.True);
            Assert.That(_slots.Active.LoadAsync<int>("p").Result.Value, Is.EqualTo(3));
        }

        [Test]
        public void DeleteKey_InSlot_RemovesItFromMetadata()
        {
            _slots.Switch(1);
            _slots.Active.Save("p", 1);
            _slots.Active.Delete("p");
            Assert.That(_slots.TryGetSlot("1", out var info) && info.Keys.Count == 0, Is.True);
        }

        [Test]
        public void SlotData_UsesRootFailureReporting()
        {
            var failures = new List<SaveFailure>();
            _root.SaveFailed += failures.Add;
            _slots.Switch(1);
            _storage.ThrowOnWrite = true;

            // スロットの一覧の保存 (Switch) は成功済み。データの保存だけが失敗する
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("保存に失敗"));
            Assert.That(_slots.Active.Save("p", 1), Is.False);
            Assert.That(failures[0].Key, Is.EqualTo(_slots.ToStorageKey("1", "p")));
            Assert.That(failures[0].Reason, Is.EqualTo(SaveFailureReason.DiskFull));
        }
    }
}
