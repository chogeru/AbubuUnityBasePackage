using System.Collections.Generic;
using Abubu.Inputs;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    public sealed class InputBindingUtilityTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
            {
                if (o is InputActionAsset asset) asset.Disable();
                Object.DestroyImmediate(o);
            }
            _created.Clear();
        }

        /// <summary>Player/{Jump: Space|South, Interact: F|West, Move: WASD|LeftStick} と UI/{Submit: Space}</summary>
        private InputActionAsset CreateActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(asset);
            var player = asset.AddActionMap("Player");
            var jump = player.AddAction("Jump", InputActionType.Button);
            jump.AddBinding("<Keyboard>/space");
            jump.AddBinding("<Gamepad>/buttonSouth");
            var interact = player.AddAction("Interact", InputActionType.Button);
            interact.AddBinding("<Keyboard>/f");
            interact.AddBinding("<Gamepad>/buttonWest");
            var move = player.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");

            var ui = asset.AddActionMap("UI");
            ui.AddAction("Submit", InputActionType.Button, "<Keyboard>/space");
            return asset;
        }

        [TestCase("<Keyboard>/space", InputDeviceKind.KeyboardMouse)]
        [TestCase("<Mouse>/leftButton", InputDeviceKind.KeyboardMouse)]
        [TestCase("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad)]
        [TestCase("<DualShockGamepad>/buttonSouth", InputDeviceKind.Gamepad)]
        [TestCase("<Touchscreen>/primaryTouch/press", InputDeviceKind.Touch)]
        [TestCase("", InputDeviceKind.Other)]
        public void GetDeviceKind_FromPath(string path, InputDeviceKind expected)
        {
            Assert.AreEqual(expected, InputBindingUtility.GetDeviceKind(path));
        }

        [Test]
        public void FindBindingIndex_PicksBindingForDevice_AndCompositeRoot()
        {
            var asset = CreateActions();
            var jump = asset.FindAction("Player/Jump");
            var move = asset.FindAction("Player/Move");

            Assert.AreEqual(0, InputBindingUtility.FindBindingIndex(jump, InputDeviceKind.KeyboardMouse));
            Assert.AreEqual(1, InputBindingUtility.FindBindingIndex(jump, InputDeviceKind.Gamepad));
            Assert.AreEqual(-1, InputBindingUtility.FindBindingIndex(jump, InputDeviceKind.Touch));
            Assert.AreEqual(0, InputBindingUtility.FindBindingIndex(move, InputDeviceKind.KeyboardMouse));
            Assert.AreEqual(5, InputBindingUtility.FindBindingIndex(move, InputDeviceKind.Gamepad));
        }

        [Test]
        public void IsSameControl_IgnoresCaseAndDerivedLayouts()
        {
            Assert.IsTrue(InputBindingUtility.IsSameControl("<Gamepad>/buttonSouth", "<gamepad>/BUTTONSOUTH"));
            Assert.IsTrue(InputBindingUtility.IsSameControl("<Gamepad>/buttonSouth", "<DualShockGamepad>/buttonSouth"));
            Assert.IsFalse(InputBindingUtility.IsSameControl("<Gamepad>/buttonSouth", "<Gamepad>/buttonNorth"));
            Assert.IsFalse(InputBindingUtility.IsSameControl("<Keyboard>/space", "<Gamepad>/buttonSouth"));
            Assert.IsFalse(InputBindingUtility.IsSameControl("", ""));
        }

        [Test]
        public void FindConflicts_OnlyInSameMap_ExcludingSelf()
        {
            var asset = CreateActions();
            var jump = asset.FindAction("Player/Jump");
            var interact = asset.FindAction("Player/Interact");
            var conflicts = new List<InputBindingRef>();

            InputBindingUtility.FindConflicts(jump, 0, conflicts);
            Assert.IsEmpty(conflicts, "UI/Submit は別マップなのでかぶりにならない");

            jump.ApplyBindingOverride(0, "<Keyboard>/f");
            InputBindingUtility.FindConflicts(jump, 0, conflicts);
            Assert.AreEqual(1, conflicts.Count);
            Assert.AreSame(interact, conflicts[0].Action);
            Assert.AreEqual(0, conflicts[0].BindingIndex);
            Assert.AreEqual("Player/Interact", conflicts[0].ActionPath);
        }

        [Test]
        public void FindConflicts_ChecksCompositeParts()
        {
            var asset = CreateActions();
            var move = asset.FindAction("Player/Move");
            var conflicts = new List<InputBindingRef>();

            move.ApplyBindingOverride(1, "<Keyboard>/space");
            InputBindingUtility.FindConflicts(move, 0, conflicts);
            Assert.AreEqual(1, conflicts.Count);
            Assert.AreEqual("Player/Jump", conflicts[0].ActionPath);
        }

        [Test]
        public void GetTargetIndices_ExpandsComposite()
        {
            var asset = CreateActions();
            var targets = new List<int>();
            InputBindingUtility.GetTargetIndices(asset.FindAction("Player/Move"), 0, targets);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, targets);

            targets.Clear();
            InputBindingUtility.GetTargetIndices(asset.FindAction("Player/Move"), 5, targets);
            CollectionAssert.AreEqual(new[] { 5 }, targets);
        }

        // ---------------------------------------------------------------- InputService の割り当て API

        private InputService CreateService(InputActionAsset asset, MemorySaveService save)
        {
            var settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
            settings.Actions = asset;
            _created.Add(settings);
            var service = new InputService(save, null, settings);
            service.Initialize();
            return service;
        }

        [Test]
        public void SetBindingOverride_NotifiesWithoutSaving_SaveBindingsSaves()
        {
            var asset = CreateActions();
            var save = new MemorySaveService();
            using var input = CreateService(asset, save);
            var changes = 0;
            using var _ = input.BindingsChanged.Subscribe(_ => changes++);

            input.SetBindingOverride("Player/Jump", 0, "<Keyboard>/j");
            Assert.AreEqual(1, changes);
            Assert.AreEqual("<Keyboard>/j", asset.FindAction("Player/Jump").bindings[0].effectivePath);
            Assert.IsFalse(save.Exists(AbubuInputSettings.DefaultSaveKey));

            input.SaveBindings();
            Assert.IsTrue(save.Exists(AbubuInputSettings.DefaultSaveKey));

            input.SetBindingOverride("Player/Jump", 0, null);
            Assert.IsNull(asset.FindAction("Player/Jump").bindings[0].overridePath);
        }

        [Test]
        public void ResetBinding_RevertsCompositeParts_AndSaves()
        {
            var asset = CreateActions();
            var save = new MemorySaveService();
            using var input = CreateService(asset, save);
            var move = asset.FindAction("Player/Move");
            input.SetBindingOverride("Player/Move", 1, "<Keyboard>/upArrow");
            input.SetBindingOverride("Player/Move", 2, "<Keyboard>/downArrow");
            input.SetBindingOverride("Player/Jump", 0, "<Keyboard>/j");

            input.ResetBinding("Player/Move", 0);
            Assert.IsNull(move.bindings[1].overridePath);
            Assert.IsNull(move.bindings[2].overridePath);
            Assert.AreEqual("<Keyboard>/j", asset.FindAction("Player/Jump").bindings[0].effectivePath, "他のアクションは戻さない");
            Assert.IsTrue(save.Exists(AbubuInputSettings.DefaultSaveKey));
        }

        [Test]
        public void CurrentDevice_DelegatesToDeviceService()
        {
            var asset = CreateActions();
            var devices = new FakeDeviceService();
            var settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
            settings.Actions = asset;
            _created.Add(settings);
            using var input = new InputService(null, null, settings, devices);
            input.Initialize();

            devices.Set(InputDeviceKind.Gamepad, GamepadKind.Xbox);
            Assert.AreEqual(InputDeviceKind.Gamepad, input.CurrentDevice.CurrentValue);
        }
    }
}
