using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

namespace Abubu.Inputs
{
    /// <summary>割り当て変更で、同じ ActionMap の別の割り当てと操作がかぶったときの扱い</summary>
    public enum RebindConflictPolicy
    {
        /// <summary>そのまま許可する (警告だけ表示する)</summary>
        Allow,
        /// <summary>かぶった側に、変更前の操作を割り当てる (入れ替え)</summary>
        Swap,
        /// <summary>変更を取り消す</summary>
        Reject,
    }

    /// <summary>アクションの割り当て 1 つを指す参照</summary>
    public readonly struct InputBindingRef : IEquatable<InputBindingRef>
    {
        public readonly InputAction Action;
        public readonly int BindingIndex;

        public InputBindingRef(InputAction action, int bindingIndex)
        {
            Action = action;
            BindingIndex = bindingIndex;
        }

        /// <summary>"Map/Action" 形式のパス (IInputService に渡せる)</summary>
        public string ActionPath => InputBindingUtility.GetActionPath(Action);

        /// <summary>割り当て (Action が null なら default)</summary>
        public InputBinding Binding => Action != null && BindingIndex >= 0 && BindingIndex < Action.bindings.Count ? Action.bindings[BindingIndex] : default;

        public bool Equals(InputBindingRef other) => ReferenceEquals(Action, other.Action) && BindingIndex == other.BindingIndex;
        public override bool Equals(object obj) => obj is InputBindingRef other && Equals(other);
        public override int GetHashCode() => ((Action != null ? Action.GetHashCode() : 0) * 397) ^ BindingIndex;
    }

    /// <summary>
    /// 割り当て (InputBinding) まわりの補助。UI に依存しないので、独自の割り当て変更画面やボタンアイコン表示からも使える。
    /// <code>
    /// var index = InputBindingUtility.FindBindingIndex(action, InputDeviceKind.Gamepad); // ゲームパッド用の割り当て
    /// var conflicts = new List&lt;InputBindingRef&gt;();
    /// InputBindingUtility.FindConflicts(action, index, conflicts);                        // 同じマップでかぶっている割り当て
    /// </code>
    /// </summary>
    public static class InputBindingUtility
    {
        /// <summary>"Map/Action" 形式のパス。マップに属さないアクションはアクション名だけ</summary>
        public static string GetActionPath(InputAction action)
        {
            if (action == null) return string.Empty;
            var map = action.actionMap;
            return map != null ? map.name + "/" + action.name : action.name;
        }

        /// <summary>Control Path ("&lt;Gamepad&gt;/buttonSouth" など) が対象とするデバイスの種類。判定できなければ Other</summary>
        public static InputDeviceKind GetDeviceKind(string controlPath)
        {
            if (string.IsNullOrEmpty(controlPath)) return InputDeviceKind.Other;
            string layout;
            try
            {
                layout = InputControlPath.TryGetDeviceLayout(controlPath);
            }
            catch (Exception)
            {
                return InputDeviceKind.Other;
            }
            if (string.IsNullOrEmpty(layout)) return InputDeviceKind.Other;

            if (IsBasedOn(layout, "Gamepad")) return InputDeviceKind.Gamepad;
            if (IsBasedOn(layout, "Joystick")) return InputDeviceKind.Joystick;
            // Touchscreen は Pointer の派生なので先に見る
            if (IsBasedOn(layout, "Touchscreen")) return InputDeviceKind.Touch;
            if (IsBasedOn(layout, "Keyboard") || IsBasedOn(layout, "Mouse") || IsBasedOn(layout, "Pointer")) return InputDeviceKind.KeyboardMouse;
            if (IsBasedOn(layout, "TrackedDevice")) return InputDeviceKind.XR;
            return InputDeviceKind.Other;
        }

        /// <summary>割り当てが対象とするデバイスの種類。複合バインディングの先頭なら最初のパートで判定する</summary>
        public static InputDeviceKind GetDeviceKind(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return InputDeviceKind.Other;
            var binding = action.bindings[bindingIndex];
            if (binding.isComposite)
            {
                var part = bindingIndex + 1;
                return part < action.bindings.Count && action.bindings[part].isPartOfComposite
                    ? GetDeviceKind(action.bindings[part].effectivePath)
                    : InputDeviceKind.Other;
            }
            return GetDeviceKind(binding.effectivePath);
        }

        /// <summary>
        /// 指定デバイス向けの最初の割り当てのインデックス (複合バインディングは先頭のインデックス)。見つからなければ -1
        /// </summary>
        public static int FindBindingIndex(InputAction action, InputDeviceKind kind)
        {
            if (action == null) return -1;
            var bindings = action.bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isPartOfComposite) continue;
                if (GetDeviceKind(action, i) == kind) return i;
            }
            return -1;
        }

        /// <summary>
        /// 変更の対象になる割り当てのインデックスを列挙する。複合バインディングの先頭なら各パート、それ以外はそれ自身
        /// </summary>
        public static void GetTargetIndices(InputAction action, int bindingIndex, List<int> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return;
            if (!action.bindings[bindingIndex].isComposite)
            {
                results.Add(bindingIndex);
                return;
            }
            for (var i = bindingIndex + 1; i < action.bindings.Count && action.bindings[i].isPartOfComposite; i++) results.Add(i);
        }

        /// <summary>2 つの Control Path が同じ操作を指すか (大文字小文字と、&lt;Gamepad&gt; と &lt;XInputController&gt; のような派生関係を無視する)</summary>
        public static bool IsSameControl(string pathA, string pathB)
        {
            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)) return false;
            if (string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase)) return true;

            if (!TrySplit(pathA, out var layoutA, out var controlA) || !TrySplit(pathB, out var layoutB, out var controlB)) return false;
            if (!string.Equals(controlA, controlB, StringComparison.OrdinalIgnoreCase)) return false;
            return IsBasedOn(layoutA, layoutB) || IsBasedOn(layoutB, layoutA);
        }

        /// <summary>
        /// 指定した割り当て (複合バインディングの先頭なら各パート) と同じ操作を使っている、同じ ActionMap 内の割り当てを集める。
        /// 自分自身は含まない。results はクリアしない
        /// </summary>
        public static void FindConflicts(InputAction action, int bindingIndex, List<InputBindingRef> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var targets = new List<int>();
            GetTargetIndices(action, bindingIndex, targets);
            foreach (var target in targets)
            {
                var path = action.bindings[target].effectivePath;
                if (string.IsNullOrEmpty(path)) continue;

                var map = action.actionMap;
                if (map != null)
                {
                    foreach (var other in map.actions) Collect(other, action, target, path, results);
                }
                else
                {
                    Collect(action, action, target, path, results);
                }
            }
        }

        private static void Collect(InputAction other, InputAction self, int selfIndex, string path, List<InputBindingRef> results)
        {
            var bindings = other.bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isComposite) continue;
                if (ReferenceEquals(other, self) && i == selfIndex) continue;
                if (!IsSameControl(bindings[i].effectivePath, path)) continue;

                var reference = new InputBindingRef(other, i);
                if (!results.Contains(reference)) results.Add(reference);
            }
        }

        private static bool TrySplit(string path, out string layout, out string control)
        {
            layout = null;
            control = null;
            if (path[0] != '<') return false;
            var end = path.IndexOf('>');
            if (end <= 1) return false;
            layout = path.Substring(1, end - 1);
            control = path.Substring(end + 1);
            return true;
        }

        private static bool IsBasedOn(string layout, string baseLayout)
        {
            if (string.IsNullOrEmpty(layout) || string.IsNullOrEmpty(baseLayout)) return false;
            if (string.Equals(layout, baseLayout, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                return InputSystem.IsFirstLayoutBasedOnSecond(layout, baseLayout);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
