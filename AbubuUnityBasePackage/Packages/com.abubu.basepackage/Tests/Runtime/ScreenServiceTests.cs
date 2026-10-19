using System.Collections;
using Abubu.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    public sealed class ScreenServiceTests
    {
        private sealed class Probe : MonoBehaviour, IScreen
        {
            public int Shown;
            public int Hidden;
            public void OnShown() => Shown++;
            public void OnHidden() => Hidden++;
        }

        private GameObject _prefab;
        private AbubuUiSettings _settings;
        private ScreenService _screens;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("Screen", typeof(RectTransform), typeof(Probe));
            _prefab.SetActive(false);
            _settings = ScriptableObject.CreateInstance<AbubuUiSettings>();
            _screens = new ScreenService(_settings);
        }

        [TearDown]
        public void TearDown()
        {
            _screens.Dispose();
            Object.Destroy(_prefab);
            Object.Destroy(_settings);
        }

        [Test]
        public void Push_HidesPrevious_AndBackRestoresIt()
        {
            var menu = _screens.Push(_prefab);
            var settings = _screens.Push(_prefab);

            Assert.That(_screens.Count.CurrentValue, Is.EqualTo(2));
            Assert.That(menu.activeSelf, Is.False);
            Assert.That(settings.activeSelf, Is.True);
            Assert.That(menu.GetComponent<Probe>().Hidden, Is.EqualTo(1));

            Assert.That(_screens.Back(), Is.True);
            Assert.That(_screens.Current, Is.SameAs(menu));
            Assert.That(menu.activeSelf, Is.True);
            Assert.That(menu.GetComponent<Probe>().Shown, Is.EqualTo(2));
            Assert.That(settings.GetComponent<Probe>().Hidden, Is.EqualTo(1));
        }

        [Test]
        public void Back_OnRoot_ReturnsFalse()
        {
            _screens.Push(_prefab);
            Assert.That(_screens.Back(), Is.False);
            Assert.That(_screens.Count.CurrentValue, Is.EqualTo(1));
        }

        [Test]
        public void KeepPreviousDisabled_RecreatesOnBack()
        {
            _settings.KeepPreviousScreens = false;
            var first = _screens.Push(_prefab);
            _screens.Push(_prefab);

            _screens.Back();
            Assert.That(_screens.Current, Is.Not.Null);
            Assert.That(_screens.Current, Is.Not.SameAs(first));
            Assert.That(_screens.Current.activeSelf, Is.True);
        }

        [Test]
        public void Replace_DoesNotGrowHistory()
        {
            var root = _screens.Push(_prefab);
            _screens.Push(_prefab);
            _screens.Replace(_prefab);

            Assert.That(_screens.Count.CurrentValue, Is.EqualTo(2));
            _screens.Back();
            Assert.That(_screens.Current, Is.SameAs(root));
        }

        [Test]
        public void BackToRoot_ClosesEverythingAbove()
        {
            var root = _screens.Push(_prefab);
            _screens.Push(_prefab);
            _screens.Push(_prefab);

            _screens.BackToRoot();
            Assert.That(_screens.Count.CurrentValue, Is.EqualTo(1));
            Assert.That(_screens.Current, Is.SameAs(root));
        }

        [UnityTest]
        public IEnumerator DestroyedExternally_ShowsPreviousScreen()
        {
            var root = _screens.Push(_prefab);
            var top = _screens.Push(_prefab);
            Object.Destroy(top);
            yield return null;

            Assert.That(_screens.Count.CurrentValue, Is.EqualTo(1));
            Assert.That(_screens.Current, Is.SameAs(root));
            Assert.That(root.activeSelf, Is.True);
        }

        [Test]
        public void UiNavigationBack_ClosesPopupBeforeScreen()
        {
            using var popups = new PopupService(settings: _settings);
            _screens.Push(_prefab);
            _screens.Push(_prefab);
            popups.Push(_prefab);

            Assert.That(UiNavigation.Back(popups, _screens), Is.True);
            Assert.That(popups.Count.CurrentValue, Is.Zero);
            Assert.That(_screens.Count.CurrentValue, Is.EqualTo(2));

            Assert.That(UiNavigation.Back(popups, _screens), Is.True);
            Assert.That(_screens.Count.CurrentValue, Is.EqualTo(1));
            Assert.That(UiNavigation.Back(popups, _screens), Is.False);
        }
    }
}
