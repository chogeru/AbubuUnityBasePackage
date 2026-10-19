using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace Abubu.Inputs
{
    /// <summary>
    /// ゲームパッドが Input System で振動できるか (SetMotorSpeeds が効くか) を判定する。
    /// オプション画面で、振動の設定を出すかどうかを決めるのに使う。
    /// <code>
    /// vibrationToggle.gameObject.SetActive(GamepadRumbleSupport.IsSupported(Gamepad.current));
    /// vibrationToggle.gameObject.SetActive(GameInput.SupportsRumble);   // 使用中のゲームパッドで判定
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>WebGL では振動できない</item>
    /// <item>クラス名・レイアウト名で判定する: XInput (Windows) / DualShock 4 / DualSense は振動できる。
    /// Switch Pro コントローラー (HID)・DualShock 3・WebGL / iOS / macOS のゲームパッド・汎用 HID は振動できない</item>
    /// <item>名前で分からなければ、止める命令 (DualMotorRumbleCommand 0, 0) を送り、デバイスが受け付けるかで判定する</item>
    /// </list>
    /// 判定結果はデバイスごとに覚えておく。
    /// </remarks>
    public static class GamepadRumbleSupport
    {
        private static readonly Dictionary<int, bool> Cache = new();

        /// <summary>振動できるか。null・ゲームパッド以外は false</summary>
        public static bool IsSupported(InputDevice device)
        {
            if (device is not Gamepad || !device.added) return false;
            if (Cache.TryGetValue(device.deviceId, out var cached)) return cached;

            var names = new List<string>();
            for (var type = device.GetType(); type != null && type != typeof(Gamepad); type = type.BaseType) names.Add(type.Name);
            for (var layout = device.layout; !string.IsNullOrEmpty(layout) && names.Count < 32; layout = InputSystem.GetNameOfBaseLayout(layout))
            {
                names.Add(layout);
            }

            var result = Classify(names, Application.platform == RuntimePlatform.WebGLPlayer) ?? Probe(device);
            Cache[device.deviceId] = result;
            return result;
        }

        /// <summary>
        /// クラス名・レイアウト名から振動できるかを判定する (実機が無くてもテストできるよう分けてある)。分からなければ null
        /// </summary>
        /// <param name="typeOrLayoutNames">デバイスのクラス名・レイアウト名 (派生側から順に)</param>
        /// <param name="isWebGL">WebGL で動いているか</param>
        public static bool? Classify(IEnumerable<string> typeOrLayoutNames, bool isWebGL = false)
        {
            if (isWebGL) return false;
            if (typeOrLayoutNames == null) return null;
            foreach (var name in typeOrLayoutNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                // 振動できないもの (派生側の名前で先に判定する)
                if (Contains(name, "WebGL") || Contains(name, "iOS") || Contains(name, "MacOS") || Contains(name, "OSX") ||
                    Contains(name, "SwitchPro") || Contains(name, "DualShock3") || Contains(name, "Nimbus") ||
                    name.StartsWith("HID::", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                // 振動できるもの
                if (Contains(name, "XInput") || Contains(name, "DualShock4") || Contains(name, "DualSense")) return true;
            }
            return null;
        }

        /// <summary>止める命令を送り、デバイスが受け付けたら振動できるとみなす</summary>
        private static bool Probe(InputDevice device)
        {
            try
            {
                var command = RumbleCommand.Create(0f, 0f);
                return device.ExecuteCommand(ref command) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Input System の DualMotorRumbleCommand ('RMBL') と同じ並びの命令 (本家は internal のため自前で定義する)</summary>
        [StructLayout(LayoutKind.Explicit, Size = Size)]
        private struct RumbleCommand : IInputDeviceCommandInfo
        {
            private const int Size = InputDeviceCommand.BaseCommandSize + sizeof(float) * 2;
            private static readonly FourCC Type = new('R', 'M', 'B', 'L');

            [FieldOffset(0)] public InputDeviceCommand BaseCommand;
            [FieldOffset(InputDeviceCommand.BaseCommandSize)] public float LowFrequency;
            [FieldOffset(InputDeviceCommand.BaseCommandSize + 4)] public float HighFrequency;

            public FourCC typeStatic => Type;

            public static RumbleCommand Create(float low, float high) => new()
            {
                BaseCommand = new InputDeviceCommand(Type, Size),
                LowFrequency = low,
                HighFrequency = high,
            };
        }

        private static bool Contains(string text, string value) => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
