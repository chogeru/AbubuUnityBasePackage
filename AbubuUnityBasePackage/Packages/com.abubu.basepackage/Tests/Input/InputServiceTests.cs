using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Abubu.Inputs;
using Abubu.Pause;
using Abubu.Save;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    public sealed class InputServiceTests
    {
        private const string SaveKey = AbubuInputSettings.DefaultSaveKey;

        /// <summary>メモリ上だけに保存する ISaveService</summary>
        private sealed class MemorySave : ISaveService
        {
            public readonly Dictionary<string, object> Data = new();

            public bool Save<T>(string key, T data)
            {
                Data[key] = data;
                return true;
            }

            public bool ForceSave<T>(string key, T data) => Save(key, data);

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
            foreach (var o in _created)
            {
                if (o is InputActionAsset asset) asset.Disable();
                Object.DestroyImmediate(o);
            }
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

        private AbubuInputSettings CreateSettings(InputActionAsset asset)
        {
            var settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
            settings.Actions = asset;
            _created.Add(settings);
            return settings;
        }

        /// <summary>
        /// プロジェクト共通の Input Actions は Input System が自動で有効にするので、それを模して全マップを有効にしてから作る
        /// </summary>
        private InputService Create(InputActionAsset asset, Action<AbubuInputSettings> configure = null)
        {
            asset.Enable();
            var settings = CreateSettings(asset);
            configure?.Invoke(settings);
            return new InputService(_save, _pause, settings);
        }

        // ---------------------------------------------------------------- ActionMap の有効/無効

        [Test]
        public void Initialize_DoesNotChangeMapStates_ByDefault()
        {
            var asset = CreateActions();
            using var input = new InputService(_save, _pause, CreateSettings(asset));

            input.Initialize();

            Assert.That(asset.FindActionMap("Player").enabled, Is.False);
            Assert.That(asset.FindActionMap("UI").enabled, Is.False);
        }

        [Test]
        public void Initialize_EnablesOnlyEnabledMaps()
        {
            var asset = CreateActions();
            var settings = CreateSettings(asset);
            settings.EnabledMaps.Add("Player");
            using var input = new InputService(_save, _pause, settings);

            input.Initialize();

            Assert.That(asset.FindActionMap("Player").enabled, Is.True);
            Assert.That(asset.FindActionMap("UI").enabled, Is.False);
        }

        [Test]
        public void Dispose_RestoresMapStates()
        {
            var asset = CreateActions();
            var settings = CreateSettings(asset);
            settings.EnabledMaps.Add("Player");
            var input = new InputService(_save, _pause, settings);
            input.Initialize();
            input.SetMapEnabled("UI", true);

            input.Dispose();

            Assert.That(asset.FindActionMap("Player").enabled, Is.False);
            Assert.That(asset.FindActionMap("UI").enabled, Is.False);
        }

        [Test]
        public void Dispose_DoesNotTouchMaps_NeverChanged()
        {
            var asset = CreateActions();
            var input = Create(asset);
            input.Initialize();
            input.SetMapEnabled("UI", true); // 元から有効 → 変化なし

            // サービスが状態を変えていないマップは、その後に他が変えてもそのまま
            asset.FindActionMap("UI").Disable();
            input.Dispose();

            Assert.That(asset.FindActionMap("UI").enabled, Is.False);
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

        // ---------------------------------------------------------------- ポーズ

        [Test]
        public void Pause_DoesNotDisableMaps_ByDefault()
        {
            var asset = CreateActions();
            using var input = Create(asset);
            input.Initialize();

            _pause.Pause(new object());

            Assert.That(asset.FindActionMap("Player").enabled, Is.True);
            Assert.That(asset.FindActionMap("UI").enabled, Is.True);
        }

        [Test]
        public void Pause_DisablesPauseDisabledMaps_UntilResumed()
        {
            var asset = CreateActions();
            using var input = Create(asset, s => s.PauseDisabledMaps.Add("Player"));
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
            using var input = Create(asset, s => s.PauseDisabledMaps.Add("Player"));
            input.Initialize();
            var owner = new object();

            input.SetGameplayEnabled(false);
            _pause.Pause(owner);
            _pause.Resume(owner);

            Assert.That(asset.FindActionMap("Player").enabled, Is.False);
        }

        [Test]
        public void SetGameplayEnabled_WhilePaused_EnablesOnResume()
        {
            var asset = CreateActions();
            using var input = Create(asset, s => s.PauseDisabledMaps.Add("Player"));
            input.Initialize();
            var owner = new object();

            input.SetGameplayEnabled(false);
            _pause.Pause(owner);
            input.SetGameplayEnabled(true);
            Assert.That(asset.FindActionMap("Player").enabled, Is.False);

            _pause.Resume(owner);
            Assert.That(asset.FindActionMap("Player").enabled, Is.True);
        }

        [Test]
        public void Dispose_WhilePaused_ReenablesPauseDisabledMaps()
        {
            var asset = CreateActions();
            var input = Create(asset, s => s.PauseDisabledMaps.Add("Player"));
            input.Initialize();

            _pause.Pause(new object());
            input.Dispose();

            Assert.That(asset.FindActionMap("Player").enabled, Is.True);
        }

        // ---------------------------------------------------------------- 割り当て

        [Test]
        public void Initialize_AppliesSavedBindingOverrides()
        {
            // 割り当ては binding の ID で照合されるため、同じアセットで「変更 → 保存 → 元に戻す」して前回の起動を再現する
            var asset = CreateActions();
            var jump = asset.FindAction("Player/Jump");
            jump.ApplyBindingOverride(0, "<Keyboard>/e");
            var expected = jump.GetBindingDisplayString(0);
            _save.Save(SaveKey, asset.SaveBindingOverridesAsJson());
            asset.RemoveAllBindingOverrides();

            using var input = Create(asset);
            input.Initialize();

            Assert.That(input.GetBindingDisplayString("Player/Jump"), Is.EqualTo(expected));
        }

        [Test]
        public void Initialize_UsesConfiguredSaveKey_AndCanSkipLoading()
        {
            var asset = CreateActions();
            var original = asset.FindAction("Player/Jump").GetBindingDisplayString(0);
            asset.FindAction("Player/Jump").ApplyBindingOverride(0, "<Keyboard>/e");
            _save.Save("custom-key", asset.SaveBindingOverridesAsJson());
            asset.RemoveAllBindingOverrides();
            using (var input = Create(asset, s =>
                   {
                       s.SaveKey = "custom-key";
                       s.LoadBindingOverridesOnStart = false;
                   }))
            {
                input.Initialize();
                Assert.That(input.GetBindingDisplayString("Player/Jump"), Is.EqualTo(original));
            }

            using (var input = Create(asset, s => s.SaveKey = "custom-key"))
            {
                input.Initialize();
                Assert.That(input.GetBindingDisplayString("Player/Jump"), Is.Not.EqualTo(original));
            }
        }

        [Test]
        public void Dispose_RevertsLoadedBindingOverrides()
        {
            // 割り当ては binding の ID で照合されるため、同じアセットで「変更 → 保存 → 元に戻す」して前回の起動を再現する
            var asset = CreateActions();
            var original = asset.FindAction("Player/Jump").GetBindingDisplayString(0);
            asset.FindAction("Player/Jump").ApplyBindingOverride(0, "<Keyboard>/e");
            _save.Save(SaveKey, asset.SaveBindingOverridesAsJson());
            asset.RemoveAllBindingOverrides();

            var input = Create(asset);
            input.Initialize();
            Assume.That(asset.FindAction("Player/Jump").GetBindingDisplayString(0), Is.Not.EqualTo(original));

            input.Dispose();

            Assert.That(asset.FindAction("Player/Jump").GetBindingDisplayString(0), Is.EqualTo(original));
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

        // ---------------------------------------------------------------- 見つからないアクション

        [Test]
        public void MissingAction_NoWarning_WhenNotStrict()
        {
            // Player マップを持たないアセットでも、便利メンバーや PauseDisabledMaps を使って警告が出ないこと
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(asset);
            asset.AddActionMap("Vehicle").AddAction("Throttle", InputActionType.Value, "<Gamepad>/rightTrigger");
            using var input = Create(asset, s => s.PauseDisabledMaps.Add("Player"));
            input.Initialize();

            Assert.That(input.GetBindingDisplayString("Player/Nothing"), Is.Empty);
            Assert.That(input.GetAction("Player/Nothing"), Is.Null);
            Assert.That(input.Move.CurrentValue, Is.EqualTo(Vector2.zero));
            using (input.Jump.Subscribe(_ => { }))
            {
                input.SetGameplayEnabled(false);
                _pause.Pause(new object());
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MissingAction_Warns_WhenStrict()
        {
            using var input = Create(CreateActions(), s => s.StrictActionLookup = true);
            input.Initialize();
            LogAssert.Expect(LogType.Warning, new Regex("Player/Nothing"));

            Assert.That(input.GetBindingDisplayString("Player/Nothing"), Is.Empty);
        }

        // ---------------------------------------------------------------- その他

        [Test]
        public void GetAction_ReturnsActionFromConfiguredAsset()
        {
            var asset = CreateActions();
            using var input = Create(asset);

            Assert.That(input.GetAction("Player/Jump"), Is.SameAs(asset.FindAction("Player/Jump")));
            Assert.That(input.Actions, Is.SameAs(asset));
        }

        [Test]
        public void DisabledSettings_MakesServiceInert()
        {
            var asset = CreateActions();
            using var input = Create(asset, s => s.Enabled = false);
            input.Initialize();

            Assert.That(input.Actions, Is.Null);
            Assert.That(input.GetAction("Player/Jump"), Is.Null);
        }

        [Test]
        public void GetBindingDisplayString_OutOfRangeIndex_ReturnsEmpty()
        {
            using var input = Create(CreateActions());
            input.Initialize();

            Assert.That(input.GetBindingDisplayString("Player/Jump", bindingIndex: 99), Is.Empty);
            Assert.That(input.GetBindingDisplayString("Player/Jump", bindingIndex: -1), Is.Empty);
        }

        [Test]
        public void SameAction_ReturnsSameObservable()
        {
            using var input = Create(CreateActions());
            input.Initialize();

            Assert.That(input.OnPerformed("Player/Jump"), Is.SameAs(input.OnPerformed("Player/Jump")));
            Assert.That(input.Performed("Player/Jump"), Is.SameAs(input.Performed("Player/Jump")));
            Assert.That(input.ReadValue<Vector2>("Player/Move"), Is.SameAs(input.ReadValue<Vector2>("Player/Move")));
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
