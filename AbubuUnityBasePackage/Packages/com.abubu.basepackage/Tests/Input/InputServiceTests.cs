using System.Collections.Generic;
using System.Text.RegularExpressions;
using Abubu.Input;
using Abubu.Pause;
using Abubu.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    public sealed class InputServiceTests
    {
        private const string SaveKey = "abubu.input-bindings";

        /// <summary>メモリ上だけに保存する ISaveService</summary>
        private sealed class MemorySave : ISaveService
        {
            public readonly Dictionary<string, object> Data = new();

            public void Save<T>(string key, T data) => Data[key] = data;

            public bool TryLoad<T>(string key, out T data)
            {
                if (Data.TryGetValue(key, out var value) && value is T typed)
                {
                    data = typed;
                    return true;
                }
                data = default;
                return false;
            }

            public T Load<T>(string key, T defaultValue = default) => TryLoad<T>(key, out var data) ? data : defaultValue;
            public bool Exists(string key) => Data.ContainsKey(key);
            public void Delete(string key) => Data.Remove(key);
        }

        private float _timeScale;
        private readonly List<Object> _created = new();
        private MemorySave _save;
        private PauseService _pause;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            _save = new MemorySave();
            _pause = new PauseService();
        }

        [TearDown]
        public void TearDown()
        {
            _pause.Dispose();
            Time.timeScale = _timeScale;
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        /// <summary>Player/{Move, Look, Jump, Attack, Interact} と UI/{Submit, Cancel} を持つ Input Actions を作る</summary>
        private InputActionAsset CreateActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(asset);

            var player = asset.AddActionMap("Player");
            player.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick").expectedControlType = "Vector2";
            player.AddAction("Look", InputActionType.Value, "<Gamepad>/rightStick").expectedControlType = "Vector2";
            player.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            player.AddAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            player.AddAction("Interact", InputActionType.Button, "<Keyboard>/f");

            var ui = asset.AddActionMap("UI");
            ui.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            ui.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
            return asset;
        }

        private InputService Create(InputActionAsset asset)
        {
            var settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
            settings.Actions = asset;
            _created.Add(settings);
            return new InputService(_save, _pause, settings);
        }

        [Test]
        public void Initialize_EnablesActions()
        {
            var asset = CreateActions();
            using var input = Create(asset);

            input.Initialize();

            Assert.That(asset.FindAction("Player/Jump").enabled, Is.True);
        }

        [Test]
        public void SetGameplayEnabled_TogglesGameplayMapOnly()
        {
            var asset = CreateActions();
            using var input = Create(asset);
            input.Initialize();

            input.SetGameplayEnabled(false);
            Assert.That(asset.FindActionMap("Player").enabled, Is.False);
            Assert.That(asset.FindActionMap("UI").enabled, Is.True);

            input.SetGameplayEnabled(true);
            Assert.That(asset.FindActionMap("Player").enabled, Is.True);
        }

        [Test]
        public void Pause_DisablesGameplayMap_UntilResumed()
        {
            var asset = CreateActions();
            using var input = Create(asset);
            input.Initialize();
            var owner = new object();

            _pause.Pause(owner);
            Assert.That(asset.FindActionMap("Player").enabled, Is.False);
            Assert.That(asset.FindActionMap("UI").enabled, Is.True);

            _pause.Resume(owner);
            Assert.That(asset.FindActionMap("Player").enabled, Is.True);
        }

        [Test]
        public void Resume_DoesNotReenableGameplay_WhenRequestedOff()
        {
            var asset = CreateActions();
            using var input = Create(asset);
            input.Initialize();
            var owner = new object();

            input.SetGameplayEnabled(false);
            _pause.Pause(owner);
            _pause.Resume(owner);

            Assert.That(asset.FindActionMap("Player").enabled, Is.False);
        }

        [Test]
        public void Initialize_AppliesSavedBindingOverrides()
        {
            // 別のアセットで割り当てを変更して JSON にし、保存済みデータとして渡す
            var source = CreateActions();
            source.FindAction("Player/Jump").ApplyBindingOverride(0, "<Keyboard>/e");
            _save.Save(SaveKey, source.SaveBindingOverridesAsJson());

            var asset = CreateActions();
            using var input = Create(asset);
            input.Initialize();

            var expected = source.FindAction("Player/Jump").GetBindingDisplayString(0);
            Assert.That(input.GetBindingDisplayString("Player/Jump"), Is.EqualTo(expected));
        }

        [Test]
        public void ResetBindings_RemovesOverrides_AndDeletesSave()
        {
            var asset = CreateActions();
            var original = asset.FindAction("Player/Jump").GetBindingDisplayString(0);
            using var input = Create(asset);
            input.Initialize();
            asset.FindAction("Player/Jump").ApplyBindingOverride(0, "<Keyboard>/e");
            _save.Save(SaveKey, "dummy");

            input.ResetBindings();

            Assert.That(input.GetBindingDisplayString("Player/Jump"), Is.EqualTo(original));
            Assert.That(_save.Exists(SaveKey), Is.False);
        }

        [Test]
        public void GetBindingDisplayString_UnknownAction_ReturnsEmptyAndWarns()
        {
            using var input = Create(CreateActions());
            input.Initialize();
            LogAssert.Expect(LogType.Warning, new Regex("Player/Nothing"));

            Assert.That(input.GetBindingDisplayString("Player/Nothing"), Is.Empty);
        }

        [Test]
        public void GetBindingDisplayString_OutOfRangeIndex_ReturnsEmpty()
        {
            using var input = Create(CreateActions());
            input.Initialize();

            Assert.That(input.GetBindingDisplayString("Player/Jump", bindingIndex: 99), Is.Empty);
        }

        [Test]
        public void OnPerformed_SameAction_ReturnsSameObservable()
        {
            using var input = Create(CreateActions());
            input.Initialize();

            Assert.That(input.OnPerformed("Player/Jump"), Is.SameAs(input.OnPerformed("Player/Jump")));
        }

        [Test]
        public void Move_StartsAtZero()
        {
            using var input = Create(CreateActions());
            input.Initialize();

            Assert.That(input.Move.CurrentValue, Is.EqualTo(Vector2.zero));
        }
    }
}
