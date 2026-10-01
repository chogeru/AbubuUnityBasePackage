using System.Collections;
using Abubu.Pause;
using Abubu.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    public sealed class PopupServiceTests
    {
        private sealed class Probe : MonoBehaviour, IPopup
        {
            public int Opened;
            public int Closing;
            public void OnOpened() => Opened++;
            public void OnClosing() => Closing++;
        }

        private float _timeScale;
        private GameObject _prefab;
        private PauseService _pause;
        private PopupService _popups;
        private AbubuUiSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            _prefab = new GameObject("Dialog", typeof(RectTransform), typeof(Probe));
            _prefab.SetActive(false);
            _pause = new PauseService();
            _settings = ScriptableObject.CreateInstance<AbubuUiSettings>();
            _popups = new PopupService(_pause, _settings);
        }

        [TearDown]
        public void TearDown()
        {
            _popups.Dispose();
            _pause.Dispose();
            Time.timeScale = _timeScale;
            Object.Destroy(_prefab);
            Object.Destroy(_settings);
        }

        [Test]
        public void Push_IncrementsCount_AndNotifiesOpened()
        {
            var popup = _popups.Push(_prefab);

            Assert.That(_popups.Count.CurrentValue, Is.EqualTo(1));
            Assert.That(popup.GetComponent<Probe>().Opened, Is.EqualTo(1));
        }

        [Test]
        public void Pop_ClosesTopFirst_AndNotifiesClosing()
        {
            var first = _popups.Push(_prefab);
            var second = _popups.Push(_prefab);
            var secondProbe = second.GetComponent<Probe>();

            Assert.That(_popups.Pop(), Is.True);

            Assert.That(secondProbe.Closing, Is.EqualTo(1));
            Assert.That(first.GetComponent<Probe>().Closing, Is.EqualTo(0));
            Assert.That(_popups.Count.CurrentValue, Is.EqualTo(1));
        }

        [Test]
        public void Pop_WhenEmpty_ReturnsFalse()
        {
            Assert.That(_popups.Pop(), Is.False);
        }

        [Test]
        public void PopAll_ClosesEverything()
        {
            _popups.Push(_prefab);
            _popups.Push(_prefab);
            _popups.Push(_prefab);

            _popups.PopAll();

            Assert.That(_popups.Count.CurrentValue, Is.Zero);
        }

        [Test]
        public void PopTo_ClosesTargetAndEverythingAboveIt()
        {
            _popups.Push(_prefab);
            var middle = _popups.Push(_prefab);
            _popups.Push(_prefab);

            _popups.PopTo(middle);

            Assert.That(_popups.Count.CurrentValue, Is.EqualTo(1));
        }

        [Test]
        public void Push_NullPrefab_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _popups.Push((GameObject)null));
        }

        [Test]
        public void PauseWhileOpen_PausesUntilLastPopupClosed()
        {
            _settings.PauseWhileOpen = true;
            _popups.Push(_prefab);
            _popups.Push(_prefab);
            Assert.That(_pause.IsPaused.CurrentValue, Is.True);

            _popups.Pop();
            Assert.That(_pause.IsPaused.CurrentValue, Is.True);

            _popups.Pop();
            Assert.That(_pause.IsPaused.CurrentValue, Is.False);
        }

        [Test]
        public void PauseWhileOpen_Disabled_DoesNotPause()
        {
            _popups.Push(_prefab);

            Assert.That(_pause.IsPaused.CurrentValue, Is.False);
        }

        [UnityTest]
        public IEnumerator DestroyedExternally_IsRemovedOnNextOperation()
        {
            var popup = _popups.Push(_prefab);
            Object.Destroy(popup);
            yield return null;

            Assert.That(_popups.Pop(), Is.False);
            Assert.That(_popups.Count.CurrentValue, Is.Zero);
        }
    }
}
