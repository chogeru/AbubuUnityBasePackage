using System.Collections.Generic;
using Abubu.Inputs;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Abubu.Tests
{
    public sealed class InputGlyphTests
    {
        private readonly List<Object> _created = new();
        private Sprite _south;
        private Sprite _cross;
        private Sprite _space;
        private InputGlyphs _glyphs;

        [SetUp]
        public void SetUp()
        {
            _south = CreateSprite("South");
            _cross = CreateSprite("Cross");
            _space = CreateSprite("Space");

            _glyphs = ScriptableObject.CreateInstance<InputGlyphs>();
            _created.Add(_glyphs);
            _glyphs.Sets.Add(new InputGlyphs.GlyphSet
            {
                Device = InputDeviceKind.Gamepad,
                Gamepad = GamepadKind.Generic,
                Entries = { new InputGlyphs.Entry("<Gamepad>/buttonSouth", _south) },
            });
            _glyphs.Sets.Add(new InputGlyphs.GlyphSet
            {
                Device = InputDeviceKind.Gamepad,
                Gamepad = GamepadKind.PlayStation,
                Entries = { new InputGlyphs.Entry("<Gamepad>/buttonSouth", _cross) },
            });
            _glyphs.Sets.Add(new InputGlyphs.GlyphSet
            {
                Device = InputDeviceKind.KeyboardMouse,
                Entries = { new InputGlyphs.Entry("<Keyboard>/space", _space) },
            });
        }

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

        private Sprite CreateSprite(string name)
        {
            var texture = new Texture2D(2, 2);
            _created.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            sprite.name = name;
            _created.Add(sprite);
            return sprite;
        }

        // ---------------------------------------------------------------- InputGlyphs

        [Test]
        public void GetSprite_PrefersGamepadKindSet()
        {
            Assert.AreEqual(_cross, _glyphs.GetSprite("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad, GamepadKind.PlayStation));
        }

        [Test]
        public void GetSprite_FallsBackToGenericSet()
        {
            Assert.AreEqual(_south, _glyphs.GetSprite("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad, GamepadKind.Xbox));
            Assert.AreEqual(_south, _glyphs.GetSprite("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad, GamepadKind.Switch));
        }

        [Test]
        public void GetSprite_MatchesDerivedLayouts()
        {
            Assert.AreEqual(_cross, _glyphs.GetSprite("<DualShockGamepad>/buttonSouth", InputDeviceKind.Gamepad, GamepadKind.PlayStation));
            Assert.AreEqual(_south, _glyphs.GetSprite("<gamepad>/BUTTONSOUTH", InputDeviceKind.Gamepad, GamepadKind.Generic));
        }

        [Test]
        public void GetSprite_ReturnsNull_WhenMissing()
        {
            Assert.IsNull(_glyphs.GetSprite("<Gamepad>/buttonNorth", InputDeviceKind.Gamepad, GamepadKind.Generic));
            Assert.IsNull(_glyphs.GetSprite("", InputDeviceKind.Gamepad, GamepadKind.Generic));
            Assert.IsNull(_glyphs.GetSprite(null, InputDeviceKind.KeyboardMouse));
        }

        [Test]
        public void GetSprite_FindsOtherDeviceSet_AsLastResort()
        {
            // キーボード使用中でもゲームパッド専用の割り当てのアイコンは出せる
            Assert.AreEqual(_south, _glyphs.GetSprite("<Gamepad>/buttonSouth", InputDeviceKind.KeyboardMouse));
            Assert.AreEqual(_space, _glyphs.GetSprite("<Keyboard>/space", InputDeviceKind.Gamepad, GamepadKind.PlayStation));
        }

        // ---------------------------------------------------------------- InputGlyphService

        private (InputService input, FakeDeviceService devices, InputGlyphService glyphs) CreateService()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(asset);
            var player = asset.AddActionMap("Player");
            var jump = player.AddAction("Jump", InputActionType.Button);
            jump.AddBinding("<Keyboard>/space");
            jump.AddBinding("<Gamepad>/buttonSouth");
            var attack = player.AddAction("Attack", InputActionType.Button);
            attack.AddBinding("<Gamepad>/buttonWest");

            var settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
            settings.Actions = asset;
            settings.Glyphs = _glyphs;
            settings.SaveKey = string.Empty;
            _created.Add(settings);

            var devices = new FakeDeviceService();
            var input = new InputService(null, null, settings, devices);
            input.Initialize();
            return (input, devices, new InputGlyphService(input, devices, settings));
        }

        [Test]
        public void GetGlyph_FollowsCurrentDevice()
        {
            var (input, devices, glyphs) = CreateService();
            using (input)
            using (glyphs)
            {
                var keyboard = glyphs.GetGlyph("Player/Jump");
                Assert.AreEqual(_space, keyboard.Sprite);
                Assert.AreEqual(0, keyboard.BindingIndex);
                Assert.IsNotEmpty(keyboard.DisplayString);

                devices.Set(InputDeviceKind.Gamepad, GamepadKind.PlayStation);
                var pad = glyphs.GetGlyph("Player/Jump");
                Assert.AreEqual(_cross, pad.Sprite);
                Assert.AreEqual(1, pad.BindingIndex);

                devices.Set(InputDeviceKind.Gamepad, GamepadKind.Xbox);
                Assert.AreEqual(_south, glyphs.GetGlyph("Player/Jump").Sprite);
            }
        }

        [Test]
        public void GetGlyph_FallsBackToDisplayString_AndFirstBinding()
        {
            var (input, _, glyphs) = CreateService();
            using (input)
            using (glyphs)
            {
                // キーボード用の割り当てが無いので最初の割り当て (ゲームパッド) を使う。アイコンは無い
                var glyph = glyphs.GetGlyph("Player/Attack");
                Assert.IsTrue(glyph.IsValid);
                Assert.IsFalse(glyph.HasSprite);
                Assert.IsNotEmpty(glyph.DisplayString);

                Assert.IsFalse(glyphs.GetGlyph("Player/Missing").IsValid);
            }
        }

        [Test]
        public void Changed_FiresOnDeviceBindingAndGlyphChanges()
        {
            var (input, devices, glyphs) = CreateService();
            using (input)
            using (glyphs)
            {
                var count = 0;
                using var _ = glyphs.Changed.Subscribe(_ => count++);

                devices.Set(InputDeviceKind.Gamepad);
                Assert.AreEqual(1, count);

                input.SetBindingOverride("Player/Jump", 1, "<Gamepad>/buttonEast");
                Assert.AreEqual(2, count);
                Assert.IsNull(glyphs.GetGlyph("Player/Jump").Sprite);

                glyphs.SetGlyphs(null);
                Assert.AreEqual(3, count);
            }
        }
    }
}
