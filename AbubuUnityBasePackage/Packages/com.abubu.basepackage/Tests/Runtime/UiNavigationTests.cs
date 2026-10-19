using System.Collections;
using System.Collections.Generic;
using Abubu.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Abubu.Tests
{
    /// <summary>ゲームパッド操作まわり (フォーカスの閉じ込め・開いたときの選択・EventSystem・操作の案内)</summary>
    public sealed class UiNavigationTests
    {
        private sealed class FakeInputMode : IUiInputModeSource
        {
            public bool IsNavigationMode { get; set; }
        }

        private sealed class PromptProvider : MonoBehaviour, IUiPromptProvider
        {
            public IReadOnlyList<UiPrompt> GetPrompts() => new[] { new UiPrompt("UI/Submit", "OK") };
        }

        private sealed class CountingFactory : IUiEventSystemFactory
        {
            public int Created;

            public EventSystem Create()
            {
                Created++;
                return new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            }
        }

        private AbubuUiSettings _settings;
        private FakeInputMode _input;
        private PopupService _popups;
        private GameObject _prefab;
        private GameObject _eventSystem;
        private GameObject _outside;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<AbubuUiSettings>();
            _input = new FakeInputMode();
            _popups = new PopupService(settings: _settings, input: _input);
            _prefab = CreatePopupPrefab();
            _eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            _outside = new GameObject("Outside", typeof(RectTransform), typeof(Image), typeof(Button));
        }

        [TearDown]
        public void TearDown()
        {
            _popups.Dispose();
            Object.Destroy(_prefab);
            Object.Destroy(_eventSystem);
            Object.Destroy(_outside);
            Object.Destroy(_settings);
        }

        private static GameObject CreatePopupPrefab()
        {
            var root = new GameObject("Popup", typeof(RectTransform));
            for (var i = 0; i < 2; i++)
            {
                var button = new GameObject("Button" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(root.transform, false);
            }
            root.AddComponent<PromptProvider>();
            return root;
        }

        [UnityTest]
        public IEnumerator FocusScope_ReturnsSelectionInsideTopPopup()
        {
            _input.IsNavigationMode = true;
            var popup = _popups.Push(_prefab);
            yield return null;
            Assert.That(EventSystem.current.currentSelectedGameObject.transform.IsChildOf(popup.transform), Is.True);

            // 自動ナビゲーションなどで裏の UI に選択が移っても、フレームの最後に中へ戻される
            EventSystem.current.SetSelectedGameObject(_outside);
            yield return null;
            Assert.That(EventSystem.current.currentSelectedGameObject.transform.IsChildOf(popup.transform), Is.True);
        }

        [Test]
        public void LowerPopup_IsNotInteractable_WhileCovered()
        {
            var lower = _popups.Push(_prefab);
            _popups.Push(_prefab);
            Assert.That(lower.GetComponent<CanvasGroup>().interactable, Is.False);

            _popups.Pop();
            Assert.That(lower.GetComponent<CanvasGroup>().interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator SelectOnOpen_Auto_DependsOnInputMode()
        {
            yield return null;
            // マウス操作中: 裏の UI の選択を外す (決定ボタンで裏のボタンが押されないように)
            EventSystem.current.SetSelectedGameObject(_outside);
            _input.IsNavigationMode = false;
            var first = _popups.Push(_prefab);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
            _popups.Pop();

            // ゲームパッド操作中: 最初のボタンを選ぶ
            _input.IsNavigationMode = true;
            var second = _popups.Push(_prefab);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(second.transform.GetChild(0).gameObject));
            Assert.That(first == null || first != second, Is.True);
        }

        [UnityTest]
        public IEnumerator EventSystemProvider_CreatesOnlyWhenMissing()
        {
            var factory = new CountingFactory();
            var provider = new UiEventSystemProvider(_settings, factory);
            try
            {
                provider.Ensure();
                Assert.That(factory.Created, Is.Zero, "シーンに EventSystem があれば作らない");

                Object.Destroy(_eventSystem);
                yield return null;
                provider.Ensure();
                provider.Ensure();
                Assert.That(factory.Created, Is.EqualTo(1));
            }
            finally
            {
                provider.Dispose();
            }
        }

        [Test]
        public void PromptService_FollowsTopPopup()
        {
            using var prompts = new UiPromptService(_popups);
            Assert.That(prompts.Top.CurrentValue, Is.Null);

            var popup = _popups.Push(_prefab);
            Assert.That(prompts.Top.CurrentValue, Is.SameAs(popup.GetComponent<PromptProvider>()));

            _popups.Pop();
            Assert.That(prompts.Top.CurrentValue, Is.Null);
        }

        [UnityTest]
        public IEnumerator ConfirmDialog_SelectsCancelByDefault()
        {
            yield return null;
            _input.IsNavigationMode = true;
            var dialogs = new DialogService(_popups, _settings);
            try
            {
                _ = dialogs.ConfirmAsync("Delete?");
                var selected = EventSystem.current.currentSelectedGameObject;
                Assert.That(selected.name, Is.EqualTo("CancelButton"));

                _popups.PopAll();
                _settings.ConfirmDefaultButton = ConfirmDefaultButton.Ok;
                _ = dialogs.ConfirmAsync("Delete?");
                Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("OkButton"));
                _popups.PopAll();
            }
            finally
            {
                dialogs.Dispose();
            }
        }
    }
}
