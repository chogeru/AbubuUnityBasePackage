using System.Collections;
using Abubu.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Abubu.Tests
{
    public sealed class DialogServiceTests
    {
        private AbubuUiSettings _settings;
        private PopupService _popups;
        private DialogService _dialogs;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<AbubuUiSettings>();
            _popups = new PopupService(settings: _settings);
            _dialogs = new DialogService(_popups, _settings);
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _popups.Dispose();
            Object.Destroy(_settings);
        }

        [UnityTest]
        public IEnumerator Confirm_ReturnsTrueOnOk_FalseOnPop() => UniTask.ToCoroutine(async () =>
        {
            var first = _dialogs.ConfirmAsync("Delete?");
            Assert.That(_popups.Count.CurrentValue, Is.EqualTo(1));
            ClickButton("OkButton");
            Assert.That(await first, Is.True);
            await UniTask.Yield();
            Assert.That(_popups.Count.CurrentValue, Is.Zero);

            // 戻る (Pop) で閉じたら false
            var second = _dialogs.ConfirmAsync("Delete?");
            _popups.Pop();
            Assert.That(await second, Is.False);
        });

        [UnityTest]
        public IEnumerator Alert_HidesCancelButton() => UniTask.ToCoroutine(async () =>
        {
            var alert = _dialogs.AlertAsync("Done", title: "Info");
            var dialog = _popups.Current;
            Assert.That(dialog.transform.Find("Buttons/CancelButton").gameObject.activeSelf, Is.False);
            Assert.That(dialog.transform.Find("Title").gameObject.activeSelf, Is.True);
            ClickButton("OkButton");
            await alert;
        });

        [UnityTest]
        public IEnumerator EnsureSelection_SelectsInsideTopPopup()
        {
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            try
            {
                yield return null;
                _dialogs.ConfirmAsync("Delete?").Forget();
                // 開いたときは組み込みのダイアログの初期選択 (OK) が選ばれている
                Assert.That(EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_popups.Current.transform), Is.True);

                // マウスなどで裏の UI が選ばれた状態から、ポップアップの中に戻す
                var outside = new GameObject("Outside", typeof(RectTransform), typeof(Image), typeof(Button));
                EventSystem.current.SetSelectedGameObject(outside);
                Assert.That(UiNavigation.EnsureSelection(_popups, null), Is.True);
                Assert.That(EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_popups.Current.transform), Is.True);
                // すでにポップアップの中が選ばれていれば何もしない
                Assert.That(UiNavigation.EnsureSelection(_popups, null), Is.False);

                _popups.PopAll();
                Object.Destroy(outside);
            }
            finally
            {
                Object.Destroy(eventSystem);
            }
        }

        private void ClickButton(string name)
        {
            var button = _popups.Current.transform.Find("Buttons/" + name).GetComponent<Button>();
            button.onClick.Invoke();
        }
    }
}
