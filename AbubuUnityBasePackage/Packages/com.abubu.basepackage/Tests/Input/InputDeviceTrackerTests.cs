using Abubu.Inputs;
using NUnit.Framework;
using R3;
using UnityEngine.InputSystem.Layouts;

namespace Abubu.Tests
{
    public sealed class InputDeviceTrackerTests
    {
        private FakeActivitySource _source;
        private InputDeviceSwitchSettings _switch;

        [SetUp]
        public void SetUp()
        {
            _source = new FakeActivitySource();
            _switch = new InputDeviceSwitchSettings { ActuationThreshold = 0.5f, PointerMoveThreshold = 4f, HoldTime = 0.25f };
        }

        private InputDeviceTracker Create()
        {
            var tracker = new InputDeviceTracker(_source, _switch);
            tracker.Initialize();
            return tracker;
        }

        // ---------------------------------------------------------------- ゲームパッドの種類

        [TestCase("XInputControllerWindows", GamepadKind.Xbox)]
        [TestCase("XboxOneGampadMacOSWireless", GamepadKind.Xbox)]
        [TestCase("DualShock4GamepadHID", GamepadKind.PlayStation)]
        [TestCase("DualSenseGamepadHID", GamepadKind.PlayStation)]
        [TestCase("SwitchProControllerHID", GamepadKind.Switch)]
        [TestCase("Gamepad", GamepadKind.Generic)]
        public void ClassifyGamepad_ByLayoutName(string layout, GamepadKind expected)
        {
            Assert.AreEqual(expected, InputDeviceClassifier.ClassifyGamepad(new[] { layout }, default));
        }

        [Test]
        public void ClassifyGamepad_UsesBaseLayouts_WhenDerivedIsUnknown()
        {
            Assert.AreEqual(GamepadKind.PlayStation,
                InputDeviceClassifier.ClassifyGamepad(new[] { "MyCustomPad", "DualShockGamepad", "Gamepad" }, default));
        }

        [Test]
        public void ClassifyGamepad_FallsBackToDescription()
        {
            Assert.AreEqual(GamepadKind.Xbox,
                InputDeviceClassifier.ClassifyGamepad(new[] { "Gamepad" }, new InputDeviceDescription { interfaceName = "XInput" }));
            Assert.AreEqual(GamepadKind.PlayStation, InputDeviceClassifier.ClassifyGamepad(new[] { "Gamepad" },
                new InputDeviceDescription { manufacturer = "Sony Interactive Entertainment", product = "Wireless Controller" }));
            Assert.AreEqual(GamepadKind.Switch, InputDeviceClassifier.ClassifyGamepad(new[] { "Gamepad" },
                new InputDeviceDescription { product = "Pro Controller", manufacturer = "Nintendo Co., Ltd." }));
            Assert.AreEqual(GamepadKind.Generic, InputDeviceClassifier.ClassifyGamepad(new[] { "Gamepad" },
                new InputDeviceDescription { product = "Generic USB Pad", manufacturer = "ACME" }));
        }

        [Test]
        public void GetKind_Null_IsOther()
        {
            Assert.AreEqual(InputDeviceKind.Other, InputDeviceClassifier.GetKind(null));
            Assert.AreEqual(GamepadKind.Generic, InputDeviceClassifier.GetGamepadKind(null));
        }

        // ---------------------------------------------------------------- しきい値

        [Test]
        public void StartsWithInitialState()
        {
            _source.Initial = new InputDeviceState(InputDeviceKind.Gamepad, GamepadKind.Switch);
            using var tracker = Create();
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);
            Assert.AreEqual(GamepadKind.Switch, tracker.CurrentGamepadKind.CurrentValue);
        }

        [Test]
        public void SmallStickMovement_DoesNotSwitch()
        {
            using var tracker = Create();
            _source.Emit(InputDeviceKind.Gamepad, 0.2f, 1.0, GamepadKind.Xbox);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentDevice.CurrentValue);

            _source.Emit(InputDeviceKind.Gamepad, 0.8f, 1.1, GamepadKind.Xbox);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);
            Assert.AreEqual(GamepadKind.Xbox, tracker.CurrentGamepadKind.CurrentValue);
        }

        [Test]
        public void SmallMouseMovement_DoesNotSwitch()
        {
            _source.Initial = new InputDeviceState(InputDeviceKind.Gamepad, GamepadKind.Generic);
            using var tracker = Create();

            _source.Emit(InputDeviceKind.KeyboardMouse, 2f, 1.0, pointer: true);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);

            _source.Emit(InputDeviceKind.KeyboardMouse, 20f, 1.1, pointer: true);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentDevice.CurrentValue);
        }

        // ---------------------------------------------------------------- チラつき防止

        [Test]
        public void OtherDevice_WithinHoldTime_IsIgnored()
        {
            using var tracker = Create();
            _source.Emit(InputDeviceKind.KeyboardMouse, 1f, 1.0);
            _source.Emit(InputDeviceKind.Gamepad, 1f, 1.1);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentDevice.CurrentValue);

            _source.Emit(InputDeviceKind.Gamepad, 1f, 1.3);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);
        }

        [Test]
        public void SimultaneousInput_DoesNotFlicker()
        {
            using var tracker = Create();
            _source.Emit(InputDeviceKind.Gamepad, 1f, 1.0);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);

            var changes = 0;
            using var _ = tracker.CurrentDevice.Skip(1).Subscribe(_ => changes++);

            // スティックを倒し続けながらキーボードも叩く
            for (var i = 1; i <= 20; i++)
            {
                _source.Emit(InputDeviceKind.Gamepad, 1f, 1.0 + i * 0.05);
                _source.Emit(InputDeviceKind.KeyboardMouse, 1f, 1.0 + i * 0.05 + 0.01);
            }
            Assert.AreEqual(0, changes);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);
        }

        [Test]
        public void HoldTimeZero_SwitchesImmediately()
        {
            _switch.HoldTime = 0f;
            using var tracker = Create();
            _source.Emit(InputDeviceKind.KeyboardMouse, 1f, 1.0);
            _source.Emit(InputDeviceKind.Gamepad, 1f, 1.0);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);
        }

        [Test]
        public void GamepadKind_ChangesBetweenGamepads_WithoutWaiting()
        {
            using var tracker = Create();
            _source.Emit(InputDeviceKind.Gamepad, 1f, 1.0, GamepadKind.Xbox);
            _source.Emit(InputDeviceKind.Gamepad, 1f, 1.05, GamepadKind.PlayStation);
            Assert.AreEqual(GamepadKind.PlayStation, tracker.CurrentGamepadKind.CurrentValue);
            Assert.AreEqual(new InputDeviceState(InputDeviceKind.Gamepad, GamepadKind.PlayStation), tracker.Current.CurrentValue);
        }

        [Test]
        public void GamepadKind_IsKept_WhenSwitchingToKeyboard()
        {
            using var tracker = Create();
            _source.Emit(InputDeviceKind.Gamepad, 1f, 1.0, GamepadKind.Switch);
            _source.Emit(InputDeviceKind.KeyboardMouse, 1f, 2.0);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentDevice.CurrentValue);
            Assert.AreEqual(GamepadKind.Switch, tracker.CurrentGamepadKind.CurrentValue);
        }

        [Test]
        public void Disabled_IgnoresActivity()
        {
            using var tracker = new InputDeviceTracker(_source, _switch, enabled: false);
            tracker.Initialize();
            _source.Emit(InputDeviceKind.Gamepad, 1f, 1.0);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentDevice.CurrentValue);
        }

        // ---------------------------------------------------------------- カーソル

        [Test]
        public void CursorAutoHide_HidesOnGamepad_ShowsOnMouse()
        {
            var devices = new FakeDeviceService();
            var visible = true;
            using var hide = new InputCursorAutoHide(devices, true, () => visible, v => visible = v);
            hide.Initialize();

            devices.Set(InputDeviceKind.Gamepad);
            Assert.IsFalse(visible);
            devices.Set(InputDeviceKind.KeyboardMouse);
            Assert.IsTrue(visible);
        }

        [Test]
        public void CursorAutoHide_DoesNotShowCursorHiddenByGame()
        {
            var devices = new FakeDeviceService();
            var visible = false;
            using var hide = new InputCursorAutoHide(devices, true, () => visible, v => visible = v);
            hide.Initialize();

            devices.Set(InputDeviceKind.Gamepad);
            devices.Set(InputDeviceKind.KeyboardMouse);
            Assert.IsFalse(visible);
        }

        [Test]
        public void CursorAutoHide_Disabled_DoesNothing_AndDisposeRestores()
        {
            var devices = new FakeDeviceService();
            var visible = true;
            using (var off = new InputCursorAutoHide(devices, false, () => visible, v => visible = v))
            {
                off.Initialize();
                devices.Set(InputDeviceKind.Gamepad);
                Assert.IsTrue(visible);
            }

            devices.Set(InputDeviceKind.KeyboardMouse);
            var on = new InputCursorAutoHide(devices, true, () => visible, v => visible = v);
            on.Initialize();
            devices.Set(InputDeviceKind.Gamepad);
            Assert.IsFalse(visible);
            on.Dispose();
            Assert.IsTrue(visible);
        }
    }
}
