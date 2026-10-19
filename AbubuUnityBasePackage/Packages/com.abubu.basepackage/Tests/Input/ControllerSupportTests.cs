using System.Collections.Generic;
using Abubu.Inputs;
using Abubu.Inputs.UI;
using Abubu.Pause;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    /// <summary>コントローラー対応 (割り当て変更のキャンセル・系統・抜き差し・振動対応・複合アイコン) のテスト</summary>
    public sealed class ControllerSupportTests
    {
        private sealed class FakeTimeScale : ITimeScaleController
        {
            public float TimeScale { get; set; } = 1f;
        }

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

        // ---------------------------------------------------------------- 割り当て変更のキャンセル (RebindInputGuard)

        private static readonly string[] CancelPaths = { "<Keyboard>/escape", "<Gamepad>/start" };

        [Test]
        public void RebindGuard_CancelPath_FromAnotherDevice_Cancels()
        {
            // キーボードの割り当てを変更中に、ゲームパッドの Start でキャンセルできる
            var keyboard = new RebindInputGuard(CancelPaths, InputDeviceKind.KeyboardMouse, true);
            Assert.AreEqual(RebindPressAction.Cancel, keyboard.Decide(InputDeviceKind.Gamepad, false, false, "<Gamepad>/start"));
            Assert.AreEqual(RebindPressAction.Cancel, keyboard.Decide(InputDeviceKind.KeyboardMouse, false, false, "<Keyboard>/escape"));

            // ゲームパッドの割り当てを変更中に、キーボードの Esc でキャンセルできる
            var gamepad = new RebindInputGuard(CancelPaths, InputDeviceKind.Gamepad, true);
            Assert.AreEqual(RebindPressAction.Cancel, gamepad.Decide(InputDeviceKind.KeyboardMouse, false, false, "<Keyboard>/escape"));
            Assert.AreEqual(RebindPressAction.Cancel, gamepad.Decide(InputDeviceKind.Gamepad, false, false, "<Gamepad>/start"));
        }

        [Test]
        public void RebindGuard_RejectedDeviceButtons_AreSuppressed()
        {
            var keyboard = new RebindInputGuard(CancelPaths, InputDeviceKind.KeyboardMouse, true);
            // B で画面が閉じない / 割り当てにも入らない
            Assert.AreEqual(RebindPressAction.Suppress, keyboard.Decide(InputDeviceKind.Gamepad, false, false, "<Gamepad>/buttonEast"));
            Assert.AreEqual(RebindPressAction.PassThrough, keyboard.Decide(InputDeviceKind.KeyboardMouse, false, false, "<Keyboard>/space"));

            var gamepad = new RebindInputGuard(CancelPaths, InputDeviceKind.Gamepad, true);
            Assert.AreEqual(RebindPressAction.Suppress, gamepad.Decide(InputDeviceKind.KeyboardMouse, false, false, "<Keyboard>/enter"));
            Assert.AreEqual(RebindPressAction.PassThrough, gamepad.Decide(InputDeviceKind.Gamepad, false, false, "<Gamepad>/buttonSouth"));
        }

        [Test]
        public void RebindGuard_MouseButtons_AreDeferred_AndNeverSuppressed()
        {
            // マウスのボタンは UI (キャンセルボタン) を押せるよう捨てない。キーボード&マウスの割り当てなら離した後に割り当てる候補
            var keyboard = new RebindInputGuard(CancelPaths, InputDeviceKind.KeyboardMouse, true);
            Assert.AreEqual(RebindPressAction.PointerCandidate, keyboard.Decide(InputDeviceKind.KeyboardMouse, true, true, "<Mouse>/leftButton"));

            var gamepad = new RebindInputGuard(CancelPaths, InputDeviceKind.Gamepad, true);
            Assert.AreEqual(RebindPressAction.PassThrough, gamepad.Decide(InputDeviceKind.KeyboardMouse, true, true, "<Mouse>/leftButton"));

            var noMouse = new RebindInputGuard(CancelPaths, InputDeviceKind.KeyboardMouse, false);
            Assert.AreEqual(RebindPressAction.PassThrough, noMouse.Decide(InputDeviceKind.KeyboardMouse, true, true, "<Mouse>/leftButton"));

            var excluded = new RebindInputGuard(CancelPaths, null, true, new[] { "<Mouse>/rightButton" });
            Assert.AreEqual(RebindPressAction.PassThrough, excluded.Decide(InputDeviceKind.KeyboardMouse, true, true, "<Mouse>/rightButton"));
            Assert.AreEqual(RebindPressAction.PointerCandidate, excluded.Decide(InputDeviceKind.KeyboardMouse, true, true, "<Mouse>/leftButton"));

            // タッチ・ペンは割り当て候補にしない (UI 操作のため)
            Assert.AreEqual(RebindPressAction.PassThrough, keyboard.Decide(InputDeviceKind.Touch, true, false, "<Touchscreen>/press"));
        }

        [Test]
        public void RebindGuard_WithoutRestriction_PassesEverythingButCancel()
        {
            var any = new RebindInputGuard(CancelPaths, null, true);
            Assert.AreEqual(RebindPressAction.PassThrough, any.Decide(InputDeviceKind.Gamepad, false, false, "<Gamepad>/buttonEast"));
            Assert.AreEqual(RebindPressAction.Cancel, any.Decide(InputDeviceKind.Gamepad, false, false, "<Gamepad>/start"));
        }

        [Test]
        public void InputService_IsRebinding_StartsFalse_AndMissingActionFails()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(asset);
            asset.AddActionMap("Player").AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            var settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
            settings.Actions = asset;
            settings.SaveKey = string.Empty;
            _created.Add(settings);

            using var input = new InputService(null, null, settings);
            input.Initialize();
            Assert.IsFalse(input.IsRebinding.CurrentValue);
            Assert.IsFalse(input.RebindAsync("Player/Missing").GetAwaiter().GetResult());
            Assert.IsFalse(input.IsRebinding.CurrentValue);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, input.CurrentScheme.CurrentValue);
        }

        // ---------------------------------------------------------------- 系統 (CurrentScheme)

        private static InputDeviceTracker CreateTracker(FakeActivitySource source, FakeConnectionSource connections = null)
        {
            var tracker = new InputDeviceTracker(source,
                new InputDeviceSwitchSettings { ActuationThreshold = 0.5f, PointerMoveThreshold = 4f, PointerMoveWindow = 0.15f, HoldTime = 0f },
                true, connections);
            tracker.Initialize();
            return tracker;
        }

        [Test]
        public void Scheme_DoesNotSwitch_OnTouchPenOrOther()
        {
            var source = new FakeActivitySource();
            using var tracker = CreateTracker(source);

            source.Emit(InputDeviceKind.Gamepad, 1f, 1.0);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentScheme.CurrentValue);

            source.Emit(InputDeviceKind.Touch, 1f, 2.0);
            Assert.AreEqual(InputDeviceKind.Touch, tracker.CurrentDevice.CurrentValue);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentScheme.CurrentValue, "タッチでは系統は変わらない");

            source.Emit(InputDeviceKind.XR, 1f, 3.0);
            source.Emit(InputDeviceKind.Joystick, 1f, 4.0);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentScheme.CurrentValue);

            source.Emit(InputDeviceKind.Other, 1f, 5.0);
            Assert.AreEqual(InputDeviceKind.Joystick, tracker.CurrentDevice.CurrentValue, "Other には切り替わらない");

            // ペンはキーボード&マウスに分類される
            source.Emit(InputDeviceKind.KeyboardMouse, 1f, 6.0);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentScheme.CurrentValue);
        }

        [Test]
        public void ToScheme_MapsOnlyKeyboardAndGamepad()
        {
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, InputDeviceClassifier.ToScheme(InputDeviceKind.KeyboardMouse));
            Assert.AreEqual(InputDeviceKind.Gamepad, InputDeviceClassifier.ToScheme(InputDeviceKind.Gamepad));
            Assert.IsNull(InputDeviceClassifier.ToScheme(InputDeviceKind.Touch));
            Assert.IsNull(InputDeviceClassifier.ToScheme(InputDeviceKind.Other));
        }

        [Test]
        public void PointerMotion_IsAccumulatedWithinWindow()
        {
            var source = new FakeActivitySource { Initial = new InputDeviceState(InputDeviceKind.Gamepad, GamepadKind.Generic) };
            using var tracker = CreateTracker(source);

            // ゆっくり動かす (1 イベントはしきい値未満) と、短時間の合計で切り替わる
            source.Emit(InputDeviceKind.KeyboardMouse, 2f, 1.00, pointer: true);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);
            source.Emit(InputDeviceKind.KeyboardMouse, 2.5f, 1.05, pointer: true);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentDevice.CurrentValue);
        }

        [Test]
        public void PointerMotion_SpreadOverTime_DoesNotSwitch()
        {
            var source = new FakeActivitySource { Initial = new InputDeviceState(InputDeviceKind.Gamepad, GamepadKind.Generic) };
            using var tracker = CreateTracker(source);

            // 机の振動のような小さな動きが間をあけて続いても切り替わらない
            for (var i = 0; i < 10; i++) source.Emit(InputDeviceKind.KeyboardMouse, 1f, 1.0 + i * 0.5, pointer: true);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);
        }

        // ---------------------------------------------------------------- 抜き差し

        [Test]
        public void LastGamepadLost_SwitchesToKeyboard_AndNotifies()
        {
            var source = new FakeActivitySource();
            var connections = new FakeConnectionSource();
            using var tracker = CreateTracker(source, connections);
            var lost = new List<InputDeviceConnection>();
            var regained = new List<InputDeviceConnection>();
            using var a = tracker.DeviceLost.Subscribe(lost.Add);
            using var b = tracker.DeviceRegained.Subscribe(regained.Add);

            connections.ConnectGamepad();
            Assert.AreEqual(1, regained.Count);
            source.Emit(InputDeviceKind.Gamepad, 1f, 1.0, GamepadKind.PlayStation);
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);

            connections.DisconnectGamepad();
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentDevice.CurrentValue);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.CurrentScheme.CurrentValue);
            Assert.AreEqual(GamepadKind.PlayStation, tracker.CurrentGamepadKind.CurrentValue, "ゲームパッドの種類は覚えておく");
            Assert.AreEqual(1, lost.Count);
            Assert.IsTrue(lost[0].WasInUse);
            Assert.IsNull(tracker.CurrentGamepad);
        }

        [Test]
        public void GamepadLost_WithAnotherConnected_StaysOnGamepad()
        {
            var source = new FakeActivitySource();
            var connections = new FakeConnectionSource();
            using var tracker = CreateTracker(source, connections);

            connections.ConnectGamepad();
            connections.ConnectGamepad();
            source.Emit(InputDeviceKind.Gamepad, 1f, 1.0);
            connections.DisconnectGamepad();
            Assert.AreEqual(InputDeviceKind.Gamepad, tracker.CurrentDevice.CurrentValue);
        }

        [Test]
        public void GamepadLost_WhileUsingKeyboard_IsNotInUse()
        {
            var source = new FakeActivitySource();
            var connections = new FakeConnectionSource();
            using var tracker = CreateTracker(source, connections);
            var lost = new List<InputDeviceConnection>();
            using var _ = tracker.DeviceLost.Subscribe(lost.Add);

            connections.ConnectGamepad();
            source.Emit(InputDeviceKind.KeyboardMouse, 1f, 1.0);
            connections.DisconnectGamepad();
            Assert.AreEqual(1, lost.Count);
            Assert.IsFalse(lost[0].WasInUse);
        }

        [Test]
        public void Pauser_PausesWhileDisconnected_ResumesOnRegain()
        {
            var source = new FakeActivitySource();
            var connections = new FakeConnectionSource();
            using var pause = new PauseService(null, new FakeTimeScale());
            using var tracker = CreateTracker(source, connections);
            using var pauser = new GamepadDisconnectPauser(tracker, pause, true);
            pauser.Initialize();

            connections.ConnectGamepad();
            source.Emit(InputDeviceKind.Gamepad, 1f, 1.0);
            Assert.IsFalse(pause.IsPaused.CurrentValue);

            connections.DisconnectGamepad();
            Assert.IsTrue(pause.IsPaused.CurrentValue);
            Assert.IsTrue(pauser.IsWaitingForReconnect.CurrentValue);

            connections.ConnectGamepad();
            Assert.IsFalse(pause.IsPaused.CurrentValue);
            Assert.IsFalse(pauser.IsWaitingForReconnect.CurrentValue);
        }

        [Test]
        public void Pauser_IgnoresUnusedGamepad_AndRespectsOtherPauses()
        {
            var devices = new FakeDeviceService();
            using var pause = new PauseService(null, new FakeTimeScale());
            using var pauser = new GamepadDisconnectPauser(devices, pause, true);
            pauser.Initialize();

            devices.Lost.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, false, wasInUse: false));
            Assert.IsFalse(pause.IsPaused.CurrentValue, "使っていないゲームパッドの切断ではポーズしない");

            var menu = new object();
            pause.Pause(menu);
            devices.Lost.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, false, wasInUse: true));
            devices.Regained.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, true));
            Assert.IsTrue(pause.IsPaused.CurrentValue, "ポーズメニューのポーズは解除しない");
            pause.Resume(menu);
            Assert.IsFalse(pause.IsPaused.CurrentValue);
        }

        [Test]
        public void Pauser_Dismiss_AndDisabled()
        {
            var devices = new FakeDeviceService();
            using var pause = new PauseService(null, new FakeTimeScale());
            using (var pauser = new GamepadDisconnectPauser(devices, pause, true))
            {
                pauser.Initialize();
                devices.Lost.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, false, wasInUse: true));
                Assert.IsTrue(pause.IsPaused.CurrentValue);
                pauser.Dismiss();
                Assert.IsFalse(pause.IsPaused.CurrentValue);
                Assert.IsFalse(pauser.IsWaitingForReconnect.CurrentValue);
            }

            using var off = new GamepadDisconnectPauser(devices, pause, false);
            off.Initialize();
            devices.Lost.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, false, wasInUse: true));
            Assert.IsFalse(pause.IsPaused.CurrentValue);
        }

        [Test]
        public void Pauser_WithoutPauseService_OnlyFlags()
        {
            var devices = new FakeDeviceService();
            using var pauser = new GamepadDisconnectPauser(devices, null, true);
            pauser.Initialize();
            devices.Lost.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, false, wasInUse: true));
            Assert.IsTrue(pauser.IsWaitingForReconnect.CurrentValue);
            devices.Regained.OnNext(new InputDeviceConnection(null, InputDeviceKind.Gamepad, true));
            Assert.IsFalse(pauser.IsWaitingForReconnect.CurrentValue);
        }

        // ---------------------------------------------------------------- 振動対応

        [TestCase("XInputControllerWindows", true)]
        [TestCase("DualShock4GamepadHID", true)]
        [TestCase("DualSenseGamepadHID", true)]
        [TestCase("SwitchProControllerHID", false)]
        [TestCase("WebGLGamepad", false)]
        [TestCase("XboxGamepadMacOS", false)]
        [TestCase("DualShock3GamepadHID", false)]
        [TestCase("HID::Generic USB Joystick", false)]
        public void RumbleSupport_ByLayoutName(string layout, bool expected)
        {
            Assert.AreEqual(expected, GamepadRumbleSupport.Classify(new[] { layout, "Gamepad" }));
        }

        [Test]
        public void RumbleSupport_UnknownOrWebGL()
        {
            Assert.IsNull(GamepadRumbleSupport.Classify(new[] { "MyConsolePad", "Gamepad" }), "名前で分からなければ実機に問い合わせる");
            Assert.AreEqual(false, GamepadRumbleSupport.Classify(new[] { "XInputControllerWindows" }, isWebGL: true));
            Assert.IsFalse(GamepadRumbleSupport.IsSupported(null));
        }

        [Test]
        public void Haptics_SupportsRumble_FollowsBackend()
        {
            var backend = new FakeRumbleBackend();
            using var haptics = new HapticsService(backend, new HapticsSettings { SaveKey = string.Empty }, deltaTime: new Subject<float>());
            haptics.Initialize();
            Assert.IsTrue(haptics.SupportsRumble);
            backend.Supports = false;
            Assert.IsFalse(haptics.SupportsRumble);
        }

        // ---------------------------------------------------------------- 複合バインディングのアイコン・文字列

        private (InputService input, FakeDeviceService devices, InputGlyphService glyphs, Sprite w) CreateGlyphService()
        {
            var texture = new Texture2D(2, 2);
            _created.Add(texture);
            var w = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            w.name = "key_w";
            _created.Add(w);

            var table = ScriptableObject.CreateInstance<InputGlyphs>();
            _created.Add(table);
            table.Sets.Add(new InputGlyphs.GlyphSet
            {
                Device = InputDeviceKind.KeyboardMouse,
                Entries = { new InputGlyphs.Entry("<Keyboard>/w", w) },
            });

            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(asset);
            var player = asset.AddActionMap("Player");
            var move = player.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            var jump = player.AddAction("Jump", InputActionType.Button);
            jump.AddBinding("<Keyboard>/space");
            jump.AddBinding("<Gamepad>/buttonSouth");

            var settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
            settings.Actions = asset;
            settings.Glyphs = table;
            settings.SaveKey = string.Empty;
            _created.Add(settings);

            var devices = new FakeDeviceService();
            var input = new InputService(null, null, settings, devices);
            input.Initialize();
            return (input, devices, new InputGlyphService(input, devices, settings), w);
        }

        [Test]
        public void CompositeGlyphs_ReturnsEachPart()
        {
            var (input, devices, glyphs, w) = CreateGlyphService();
            using (input)
            using (glyphs)
            {
                var parts = glyphs.GetCompositeGlyphs("Player/Move");
                Assert.AreEqual(4, parts.Count);
                Assert.AreEqual("Up", parts[0].PartName, "パート名は Input Actions の名前");
                Assert.AreEqual("<Keyboard>/w", parts[0].ControlPath);
                Assert.AreEqual(w, parts[0].Sprite);
                Assert.IsNull(parts[1].Sprite);
                Assert.IsNotEmpty(parts[1].DisplayString);

                // ゲームパッドでは複合ではないので 1 つだけ
                devices.Set(InputDeviceKind.Gamepad);
                var pad = glyphs.GetCompositeGlyphs("Player/Move");
                Assert.AreEqual(1, pad.Count);
                Assert.AreEqual("<Gamepad>/leftStick", pad[0].ControlPath);

                Assert.AreEqual(0, glyphs.GetCompositeGlyphs("Player/Missing").Count);
            }
        }

        [Test]
        public void Glyph_IsUnbound_WhenOverrideIsEmpty_OrActionMissing()
        {
            var (input, _, glyphs, _) = CreateGlyphService();
            using (input)
            using (glyphs)
            {
                Assert.IsFalse(glyphs.GetGlyph("Player/Jump").IsUnbound);
                input.SetBindingOverride("Player/Jump", 0, string.Empty);
                Assert.IsTrue(glyphs.GetGlyph("Player/Jump").IsUnbound);
                Assert.IsTrue(glyphs.GetGlyph("Player/Missing").IsUnbound);
            }
        }

        [Test]
        public void Glyphs_UseScheme_NotTouch()
        {
            var (input, devices, glyphs, _) = CreateGlyphService();
            using (input)
            using (glyphs)
            {
                devices.Set(InputDeviceKind.Gamepad);
                devices.Set(InputDeviceKind.Touch);
                Assert.AreEqual(1, glyphs.GetGlyph("Player/Jump").BindingIndex, "タッチを触ってもゲームパッドの割り当てのまま");
            }
        }

        [Test]
        public void GlyphText_ReplacesActionsWithDisplayStringOrSpriteTags()
        {
            var (input, _, glyphs, _) = CreateGlyphService();
            using (input)
            using (glyphs)
            {
                var plain = new InputGlyphTextOptions();
                var space = input.GetBindingDisplayString("Player/Jump", 0);
                Assert.AreEqual("Press " + space + " to jump", InputGlyphText.Format("Press {Player/Jump} to jump", glyphs, GamepadKind.Generic, plain));
                Assert.AreEqual("{x} -", InputGlyphText.Format("{{x}} {Player/Missing}", glyphs, GamepadKind.Generic, plain));
                Assert.AreEqual("{open", InputGlyphText.Format("{open", glyphs, GamepadKind.Generic, plain));

                var tmp = new InputGlyphTextOptions { UseSpriteTags = true, SpriteAsset = "Icons" };
                tmp.SpriteNames.Add(new InputGlyphTextOptions.SpriteName("<Keyboard>/space", "key_space"));
                Assert.AreEqual("Press <sprite=\"Icons\" name=\"key_space\">",
                    InputGlyphText.Format("Press {Player/Jump}", glyphs, GamepadKind.Generic, tmp));

                // 複合: 1 パートでもスプライト名が無ければ表示文字列
                var composite = InputGlyphText.Format("{Player/Move}", glyphs, GamepadKind.Generic, tmp);
                Assert.AreEqual(input.GetBindingDisplayString("Player/Move", 0), composite);

                // パートすべてに名前があればスプライトを並べる (W は InputGlyphs のアイコン名、他は対応表)
                tmp.SpriteNames.Add(new InputGlyphTextOptions.SpriteName("<Keyboard>/s", "key_s"));
                tmp.SpriteNames.Add(new InputGlyphTextOptions.SpriteName("<Keyboard>/a", "key_a"));
                tmp.SpriteNames.Add(new InputGlyphTextOptions.SpriteName("<Keyboard>/d", "key_d"));
                tmp.SpriteAsset = string.Empty;
                StringAssert.StartsWith("<sprite name=\"key_w\"><sprite name=\"key_s\">",
                    InputGlyphText.Format("{Player/Move}", glyphs, GamepadKind.Generic, tmp));

                Assert.AreEqual("-", InputGlyphText.Format("{Player/Jump}", null, GamepadKind.Generic, plain), "サービスが無ければ未割り当て表示");
            }
        }
    }
}
