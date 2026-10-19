using System.Collections;
using Abubu.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    /// <summary>閉じる処理の途中で別の操作が呼ばれたとき・外から破棄されたときに、スタックが崩れないこと</summary>
    public sealed class PopupServiceStackTests
    {
        private sealed class ClosingAction : MonoBehaviour, IPopup
        {
            public System.Action Action;
            public void OnOpened() { }
            public void OnClosing() => Action?.Invoke();
        }

        private GameObject _prefab;
        private AbubuUiSettings _settings;
        private PopupService _popups;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("Dialog", typeof(RectTransform), typeof(ClosingAction));
            _settings = ScriptableObject.CreateInstance<AbubuUiSettings>();
            _popups = new PopupService(settings: _settings);
        }

        [TearDown]
        public void TearDown()
        {
            _popups.Dispose();
            Object.Destroy(_prefab);
            Object.Destroy(_settings);
        }

        [Test]
        public void PopInsideOnClosing_ClosesNextOne_WithoutRecursion()
        {
            _popups.Push(_prefab);
            var top = _popups.Push(_prefab);
            // 子のポップアップが閉じるときに親も閉じる、というよくある書き方
            top.GetComponent<ClosingAction>().Action = () => _popups.Pop();

            _popups.Pop();
            Assert.That(_popups.Count.CurrentValue, Is.Zero);
        }

        [Test]
        public void ThrowingOnClosing_StillCloses()
        {
            var popup = _popups.Push(_prefab);
            popup.GetComponent<ClosingAction>().Action = () => throw new System.InvalidOperationException("boom");

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            Assert.That(_popups.Pop(), Is.True);
            Assert.That(_popups.Count.CurrentValue, Is.Zero);
        }

        [Test]
        public void Close_RemovesOnlyThatPopup()
        {
            var bottom = _popups.Push(_prefab);
            var top = _popups.Push(_prefab);

            Assert.That(_popups.Close(bottom), Is.True);
            Assert.That(_popups.Count.CurrentValue, Is.EqualTo(1));
            Assert.That(_popups.Close(bottom), Is.False);
            Assert.That(_popups.Close(top), Is.True);
        }

        [UnityTest]
        public IEnumerator DestroyedExternally_UpdatesCountImmediately()
        {
            var popup = _popups.Push(_prefab);
            Object.Destroy(popup);
            yield return null;

            // 次の操作を待たずに Count が更新される (アクティブだったオブジェクトは OnDestroy で通知される)
            Assert.That(_popups.Count.CurrentValue, Is.Zero);
        }

        [Test]
        public void PushGenericNull_ThrowsArgumentNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => _popups.Push<ClosingAction>(null));
        }
    }
}
