using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Abubu.Debugging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    /// <summary>
    /// DebugOverlay の登録 API・ゲームパッド操作・ログ末尾のテスト。
    /// ゲームパッド操作は入力の読み取りと分けてあるため、デバイスを使わず内部の操作を直接呼んで確かめる。
    /// </summary>
    public sealed class DebugOverlayTests
    {
        private readonly List<IDisposable> _handles = new();
        private DebugOverlay _overlay;

        [SetUp]
        public void SetUp()
        {
            _overlay = new GameObject("TestDebugOverlay").AddComponent<DebugOverlay>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var handle in _handles) handle.Dispose();
            _handles.Clear();
            if (_overlay != null) Object.DestroyImmediate(_overlay.gameObject);
        }

        private T Keep<T>(T handle) where T : IDisposable
        {
            _handles.Add(handle);
            return handle;
        }

        /// <summary>このテストで登録したもの (カテゴリが "T." で始まるもの) だけを表示順で返す</summary>
        private static List<string> TestKeys() =>
            DebugOverlay.GetControlKeysForTest().FindAll(static k => k.StartsWith("T.", StringComparison.Ordinal));

        // ---- 登録 ----

        [Test]
        public void RegisterCommand_GroupsByCategory_KeepingRegistrationOrder()
        {
            Keep(DebugOverlay.RegisterCommand("A", () => { }, category: "T.Cheat"));
            Keep(DebugOverlay.RegisterCommand("B", () => { }, category: "T.Time"));
            Keep(DebugOverlay.RegisterCommand("C", () => { }, category: "T.Cheat"));
            Keep(DebugOverlay.RegisterToggle("D", () => true, _ => { }, category: "T.Time"));

            Assert.That(TestKeys(), Is.EqualTo(new[] { "T.Cheat/A", "T.Cheat/C", "T.Time/B", "T.Time/D" }));
        }

        [Test]
        public void RegisterCommand_WithoutCategory_GoesToDefaultCategory()
        {
            Keep(DebugOverlay.RegisterCommand("T.NoCategory", () => { }));
            Assert.That(DebugOverlay.GetControlKeysForTest(), Does.Contain("Commands/T.NoCategory"));
        }

        [Test]
        public void SameKey_Replaces_InPlace_AndOldHandleDoesNotRemoveReplacement()
        {
            var calls = new List<string>();
            var old = DebugOverlay.RegisterCommand("A", () => calls.Add("old"), category: "T.X");
            Keep(DebugOverlay.RegisterCommand("B", () => { }, category: "T.X"));
            Keep(DebugOverlay.RegisterCommand("A", () => calls.Add("new"), category: "T.X"));

            Assert.That(TestKeys(), Is.EqualTo(new[] { "T.X/A", "T.X/B" }), "置き換えても並び順は変わらない");

            old.Dispose(); // 置き換え済みの古い登録を消しても新しい登録は残る
            Assert.That(TestKeys(), Is.EqualTo(new[] { "T.X/A", "T.X/B" }));

            _overlay.ToggleFromGamepad();
            SelectKey("T.X/A");
            _overlay.ActivateSelected();
            Assert.That(calls, Is.EqualTo(new[] { "new" }));
        }

        [Test]
        public void Dispose_And_UnregisterCommand_Remove()
        {
            var handle = DebugOverlay.RegisterCommand("A", () => { }, category: "T.X");
            Keep(DebugOverlay.RegisterCommand("B", () => { }, category: "T.X"));

            handle.Dispose();
            handle.Dispose(); // 二重に呼んでも安全
            Assert.That(TestKeys(), Is.EqualTo(new[] { "T.X/B" }));

            DebugOverlay.UnregisterCommand("B", "T.X");
            Assert.That(TestKeys(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator DestroyedOwner_RemovesItsCommands()
        {
            var owner = new GameObject("Owner");
            Keep(DebugOverlay.RegisterCommand("A", () => { }, owner, "T.Owned"));
            Keep(DebugOverlay.RegisterSlider("S", () => 0f, _ => { }, 0f, 1f, owner, "T.Owned"));
            Assert.That(TestKeys(), Has.Count.EqualTo(2));

            Object.Destroy(owner);
            yield return null;

            Assert.That(TestKeys(), Is.Empty);
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentException>(() => DebugOverlay.RegisterCommand("", () => { }));
            Assert.Throws<ArgumentNullException>(() => DebugOverlay.RegisterCommand("A", null));
            Assert.Throws<ArgumentNullException>(() => DebugOverlay.RegisterToggle("A", null, _ => { }));
            Assert.Throws<ArgumentException>(() => DebugOverlay.RegisterSlider("A", () => 0f, _ => { }, 1f, 1f));
            Assert.That(TestKeys(), Is.Empty);
        }

        // ---- ゲームパッド操作 ----

        [Test]
        public void GamepadToggle_ShowsWithFocus_ThenHides()
        {
            _overlay.Visible = false;
            Assert.That(DebugOverlay.HasGamepadFocus, Is.False);

            _overlay.ToggleFromGamepad();
            Assert.That(_overlay.Visible, Is.True);
            Assert.That(DebugOverlay.HasGamepadFocus, Is.True);

            _overlay.ReleaseGamepadFocus(); // B ボタン: 表示はそのまま操作だけやめる
            Assert.That(_overlay.Visible, Is.True);
            Assert.That(DebugOverlay.HasGamepadFocus, Is.False);

            _overlay.ToggleFromGamepad(); // 表示中なら操作できる状態に戻る
            Assert.That(DebugOverlay.HasGamepadFocus, Is.True);

            _overlay.ToggleFromGamepad(); // 操作中なら閉じる
            Assert.That(_overlay.Visible, Is.False);
            Assert.That(DebugOverlay.HasGamepadFocus, Is.False);
        }

        [Test]
        public void GamepadNavigation_MovesAcrossCategories_AndWraps()
        {
            Keep(DebugOverlay.RegisterCommand("A", () => { }, category: "T.1"));
            Keep(DebugOverlay.RegisterCommand("B", () => { }, category: "T.2"));
            Keep(DebugOverlay.RegisterCommand("C", () => { }, category: "T.1"));
            _overlay.ToggleFromGamepad();

            // 他の登録が混ざっていても動くよう、A を基準に相対で確かめる
            var keys = DebugOverlay.GetControlKeysForTest();
            SelectKey("T.1/A");

            _overlay.MoveSelection(1);
            Assert.That(_overlay.SelectedKeyForTest, Is.EqualTo("T.1/C"), "カテゴリ見出しは飛ばし、表示順に動く");
            _overlay.MoveSelection(1);
            Assert.That(_overlay.SelectedKeyForTest, Is.EqualTo("T.2/B"));

            _overlay.MoveSelection(keys.Count); // 1 周すると元に戻る
            Assert.That(_overlay.SelectedKeyForTest, Is.EqualTo("T.2/B"));
        }

        [Test]
        public void ActivateSelected_InvokesCommand_ExceptionIsLogged()
        {
            Keep(DebugOverlay.RegisterCommand("Boom", () => throw new InvalidOperationException("command-boom"), category: "T.X"));
            var count = 0;
            Keep(DebugOverlay.RegisterCommand("Count", () => count++, category: "T.X"));
            _overlay.ToggleFromGamepad();
            SelectKey("T.X/Boom");

            LogAssert.Expect(LogType.Exception, new Regex("command-boom"));
            Assert.DoesNotThrow(_overlay.ActivateSelected);

            _overlay.MoveSelection(1);
            _overlay.ActivateSelected();
            _overlay.ActivateSelected();
            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void Toggle_FlipsWithA_AndSetsWithLeftRight()
        {
            var value = false;
            Keep(DebugOverlay.RegisterToggle("God", () => value, v => value = v, category: "T.X"));
            _overlay.ToggleFromGamepad();
            SelectKey("T.X/God");
            Assert.That(_overlay.SelectedTextForTest, Is.EqualTo("[ ] God"));

            _overlay.ActivateSelected();
            Assert.That(value, Is.True);
            Assert.That(_overlay.SelectedTextForTest, Is.EqualTo("[x] God"));

            _overlay.AdjustSelected(-1);
            Assert.That(value, Is.False);
            _overlay.AdjustSelected(1);
            Assert.That(value, Is.True);
        }

        [Test]
        public void Slider_AdjustsByStep_AndClamps()
        {
            var value = 0.5f;
            Keep(DebugOverlay.RegisterSlider("Speed", () => value, v => value = v, 0f, 1f, category: "T.X", step: 0.25f));
            _overlay.ToggleFromGamepad();
            SelectKey("T.X/Speed");

            _overlay.AdjustSelected(1);
            Assert.That(value, Is.EqualTo(0.75f));
            _overlay.AdjustSelected(1);
            _overlay.AdjustSelected(1);
            Assert.That(value, Is.EqualTo(1f), "max で止まる");
            Assert.That(_overlay.SelectedTextForTest, Is.EqualTo("Speed  1"));

            _overlay.ActivateSelected(); // スライダーでは A は何もしない
            Assert.That(value, Is.EqualTo(1f));

            // 外から値が変わったら次の更新で表示に反映する
            value = 0.1f;
            _overlay.RefreshForTest();
            Assert.That(_overlay.SelectedTextForTest, Is.EqualTo("Speed  0.1"));
        }

        [Test]
        public void SelectionFollowsControl_WhenOthersAreRemoved()
        {
            var a = DebugOverlay.RegisterCommand("A", () => { }, category: "T.X");
            Keep(DebugOverlay.RegisterCommand("B", () => { }, category: "T.X"));
            _overlay.ToggleFromGamepad();
            SelectKey("T.X/B");

            a.Dispose();

            Assert.That(_overlay.SelectedKeyForTest, Is.EqualTo("T.X/B"));
        }

        private void SelectKey(string key)
        {
            var keys = DebugOverlay.GetControlKeysForTest();
            var target = keys.IndexOf(key);
            Assert.That(target, Is.GreaterThanOrEqualTo(0), key + " が登録されていない");
            for (var i = 0; i < keys.Count && _overlay.SelectedKeyForTest != key; i++) _overlay.MoveSelection(1);
            Assert.That(_overlay.SelectedKeyForTest, Is.EqualTo(key));
        }

        // ---- 表示内容 ----

        [Test]
        public void RegisteredValues_AppearInText_GetterErrorIsShown()
        {
            Keep(DebugOverlay.Register("T.score", () => "123"));
            Keep(DebugOverlay.Register("T.broken", () => throw new InvalidOperationException("getter-boom")));

            _overlay.RefreshForTest();

            Assert.That(_overlay.TextForTest, Does.Contain("T.score 123"));
            Assert.That(_overlay.TextForTest, Does.Contain("T.broken <error: getter-boom>"));
        }

        // ---- ログ末尾 ----

        [Test]
        public void LogTail_IsOffByDefault()
        {
            Assert.That(_overlay.ShowLogTail, Is.False);
            Debug.Log("T.log-hidden");
            _overlay.RefreshForTest();
            Assert.That(_overlay.TextForTest, Does.Not.Contain("T.log-hidden"));
        }

        [Test]
        public void LogTail_KeepsLastN_FirstLineOnly()
        {
            _overlay.ShowLogTail = true;
            _overlay.LogTailCount = 2;

            Debug.Log("T.first");
            Debug.LogWarning("T.second");
            Debug.Log("T.third\nsecond line");
            _overlay.RefreshForTest();

            var text = _overlay.TextForTest;
            Assert.That(text, Does.Not.Contain("T.first"));
            Assert.That(text, Does.Contain("[W] T.second"));
            Assert.That(text, Does.Contain("[I] T.third"));
            Assert.That(text, Does.Not.Contain("second line"));
        }

        [Test]
        public void LogTail_FilterSkipsOtherTypes()
        {
            _overlay.ShowLogTail = true;
            _overlay.LogTailFilter = DebugOverlay.LogFilter.Warning;

            Debug.Log("T.info");
            Debug.LogWarning("T.warn");
            _overlay.RefreshForTest();

            Assert.That(_overlay.TextForTest, Does.Not.Contain("T.info"));
            Assert.That(_overlay.TextForTest, Does.Contain("[W] T.warn"));
        }

        [Test]
        public void LogTail_StopsCollecting_WhenTurnedOff()
        {
            _overlay.ShowLogTail = true;
            _overlay.ShowLogTail = false;
            Debug.Log("T.after-off");
            _overlay.ShowLogTail = true;
            _overlay.RefreshForTest();

            Assert.That(_overlay.TextForTest, Does.Not.Contain("T.after-off"));
        }
    }
}
