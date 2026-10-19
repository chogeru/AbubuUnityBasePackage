using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

namespace Abubu.Inputs
{
    /// <summary>
    /// デバイスを <see cref="InputDeviceKind"/> / <see cref="GamepadKind"/> に分類する。
    /// <code>
    /// var kind = InputDeviceClassifier.GetKind(device);              // Gamepad / KeyboardMouse / ...
    /// var pad = InputDeviceClassifier.GetGamepadKind(Gamepad.current); // Xbox / PlayStation / Switch / Generic
    /// </code>
    /// </summary>
    /// <remarks>
    /// ゲームパッドの種類は、デバイスのクラス名・レイアウト名 (基底レイアウトを含む) と、
    /// デバイス情報 (製品名・メーカー名・インターフェース名) から判定する。
    /// XInputController / DualShockGamepad / SwitchProControllerHID などはプラットフォームによってはコンパイルされないため、
    /// 型を直接参照せず名前で判定している。
    /// </remarks>
    public static class InputDeviceClassifier
    {
        /// <summary>
        /// デバイスの種類を返す。null なら <see cref="InputDeviceKind.Other"/>。
        /// ペン・タッチ以外のポインター (ペンタブレットなど) はマウスの仲間としてキーボード&amp;マウスに分類する
        /// </summary>
        public static InputDeviceKind GetKind(InputDevice device) => device switch
        {
            null => InputDeviceKind.Other,
            Gamepad => InputDeviceKind.Gamepad,
            Joystick => InputDeviceKind.Joystick,
            // Touchscreen は Pointer の派生なので Pen / Pointer より先に見る
            Touchscreen => InputDeviceKind.Touch,
            TrackedDevice => InputDeviceKind.XR,
            Keyboard or Mouse or Pen or Pointer => InputDeviceKind.KeyboardMouse,
            _ => InputDeviceKind.Other,
        };

        /// <summary>
        /// デバイスの種類を、割り当ての系統 (キーボード&amp;マウス / ゲームパッド) に丸める。それ以外は null
        /// </summary>
        public static InputDeviceKind? ToScheme(InputDeviceKind kind) => kind switch
        {
            InputDeviceKind.KeyboardMouse => InputDeviceKind.KeyboardMouse,
            InputDeviceKind.Gamepad => InputDeviceKind.Gamepad,
            _ => null,
        };

        /// <summary>ゲームパッドの種類を返す。ゲームパッド以外・null なら <see cref="GamepadKind.Generic"/></summary>
        public static GamepadKind GetGamepadKind(InputDevice device)
        {
            if (device is not Gamepad) return GamepadKind.Generic;

            var names = new List<string>();
            for (var type = device.GetType(); type != null && type != typeof(Gamepad); type = type.BaseType) names.Add(type.Name);
            for (var layout = device.layout; !string.IsNullOrEmpty(layout) && names.Count < 32; layout = InputSystem.GetNameOfBaseLayout(layout))
            {
                names.Add(layout);
            }
            return ClassifyGamepad(names, device.description);
        }

        /// <summary>
        /// クラス名・レイアウト名と、デバイス情報からゲームパッドの種類を判定する (実機が無くてもテストできるよう分けてある)。
        /// 名前で分からなければデバイス情報で判定する。
        /// </summary>
        /// <param name="typeOrLayoutNames">デバイスのクラス名・レイアウト名 (派生側から順に)</param>
        /// <param name="description">デバイス情報</param>
        public static GamepadKind ClassifyGamepad(IEnumerable<string> typeOrLayoutNames, InputDeviceDescription description)
        {
            if (typeOrLayoutNames != null)
            {
                foreach (var name in typeOrLayoutNames)
                {
                    var kind = ClassifyName(name);
                    if (kind != GamepadKind.Generic) return kind;
                }
            }

            if (Contains(description.interfaceName, "XInput")) return GamepadKind.Xbox;
            var kindFromProduct = ClassifyName(description.product);
            if (kindFromProduct != GamepadKind.Generic) return kindFromProduct;
            return ClassifyManufacturer(description.manufacturer);
        }

        private static GamepadKind ClassifyName(string name)
        {
            if (string.IsNullOrEmpty(name)) return GamepadKind.Generic;
            if (Contains(name, "XInput") || Contains(name, "Xbox")) return GamepadKind.Xbox;
            if (Contains(name, "DualShock") || Contains(name, "DualSense") || Contains(name, "PlayStation")) return GamepadKind.PlayStation;
            if (Contains(name, "Switch") || Contains(name, "JoyCon") || Contains(name, "Joy-Con") || Contains(name, "NPad")) return GamepadKind.Switch;
            return GamepadKind.Generic;
        }

        private static GamepadKind ClassifyManufacturer(string manufacturer)
        {
            if (string.IsNullOrEmpty(manufacturer)) return GamepadKind.Generic;
            if (Contains(manufacturer, "Microsoft")) return GamepadKind.Xbox;
            if (Contains(manufacturer, "Sony")) return GamepadKind.PlayStation;
            if (Contains(manufacturer, "Nintendo")) return GamepadKind.Switch;
            return GamepadKind.Generic;
        }

        private static bool Contains(string text, string value) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
