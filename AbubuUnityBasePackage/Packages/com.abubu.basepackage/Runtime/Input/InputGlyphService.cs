using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

namespace Abubu.Inputs
{
    /// <summary>アクションの、現在のデバイスでの表示 (アイコン + 文字列)</summary>
    public readonly struct InputGlyph
    {
        /// <summary>ボタンアイコン (<see cref="InputGlyphs"/> に無ければ null)</summary>
        public readonly Sprite Sprite;
        /// <summary>表示用文字列 ("Space" / "A" など)。アイコンが無いときの代わりに使う</summary>
        public readonly string DisplayString;
        /// <summary>割り当ての Control Path (複合バインディングなら空)</summary>
        public readonly string ControlPath;
        /// <summary>使った割り当てのインデックス (アクションが無ければ -1)</summary>
        public readonly int BindingIndex;
        /// <summary>複合バインディングのパート名 ("up" / "down" など。パートでなければ空)</summary>
        public readonly string PartName;

        public InputGlyph(Sprite sprite, string displayString, string controlPath, int bindingIndex)
            : this(sprite, displayString, controlPath, bindingIndex, null)
        {
        }

        public InputGlyph(Sprite sprite, string displayString, string controlPath, int bindingIndex, string partName)
        {
            Sprite = sprite;
            DisplayString = displayString ?? string.Empty;
            ControlPath = controlPath ?? string.Empty;
            BindingIndex = bindingIndex;
            PartName = partName ?? string.Empty;
        }

        /// <summary>アイコンがあるか</summary>
        public bool HasSprite => Sprite != null;

        /// <summary>アクション・割り当てが見つかったか</summary>
        public bool IsValid => BindingIndex >= 0;

        /// <summary>
        /// 表示するものが無いか (アクション・割り当てが無い、または割り当てが空 (入れ替えで未割り当てになった など))。
        /// 「未割り当て」の表示に切り替えるのに使う
        /// </summary>
        public bool IsUnbound => !IsValid || (Sprite == null && DisplayString.Length == 0);

        /// <summary>見つからなかったときの値</summary>
        public static InputGlyph None => new(null, string.Empty, string.Empty, -1);
    }

    /// <summary>
    /// アクションのボタンアイコン・表示文字列を、現在のデバイスと割り当てに合わせて返す。
    /// <code>
    /// var glyph = _glyphs.GetGlyph("Player/Jump");
    /// if (glyph.HasSprite) image.sprite = glyph.Sprite; else label.text = glyph.DisplayString;
    /// _glyphs.Changed.Subscribe(_ => Refresh()).AddTo(this);   // デバイス・割り当て・アイコン表が変わったら作り直す
    /// GameInput.GetGlyph("Player/Jump");                       // static でも取れる
    /// foreach (var part in _glyphs.GetCompositeGlyphs("Player/Move")) Add(part.Sprite, part.PartName); // WASD を 1 キーずつ
    /// </code>
    /// </summary>
    public interface IInputGlyphService
    {
        /// <summary>
        /// アクションのアイコン・表示文字列。bindingIndex が負なら、現在のデバイス向けの割り当てを自動で選ぶ
        /// (無ければ最初の割り当て)
        /// </summary>
        InputGlyph GetGlyph(string actionPath, int bindingIndex = -1);

        /// <summary>
        /// 複合バインディング (WASD・十字キーなど) のパートごとのアイコン・表示文字列。
        /// bindingIndex が負なら現在のデバイス向けの割り当てを自動で選ぶ。複合でなければその割り当て 1 つだけ、見つからなければ空
        /// </summary>
        IReadOnlyList<InputGlyph> GetCompositeGlyphs(string actionPath, int bindingIndex = -1);

        /// <summary>表示が変わりうるとき (デバイスの切り替え・割り当て変更・アイコン表の差し替え) に流れる</summary>
        Observable<Unit> Changed { get; }

        /// <summary>使用中のアイコン表 (null なら文字列だけ)</summary>
        InputGlyphs Glyphs { get; }

        /// <summary>アイコン表を差し替える (テーマ切り替えなど)</summary>
        void SetGlyphs(InputGlyphs glyphs);
    }

    /// <summary>
    /// <see cref="IInputGlyphService"/> の標準実装。InputModuleInstaller が自動でバインドする。
    /// 割り当ての選択は <see cref="IInputDeviceService.CurrentScheme"/> (キーボード&amp;マウス / ゲームパッド) で行うので、
    /// タッチ・ペンを触っても表示が消えない
    /// </summary>
    public sealed class InputGlyphService : IInputGlyphService, IDisposable
    {
        private readonly IInputService _input;
        private readonly IInputDeviceService _devices;
        private readonly Subject<Unit> _glyphsChanged = new();
        private readonly Observable<Unit> _changed;
        private InputGlyphs _glyphs;

        public Observable<Unit> Changed => _changed;
        public InputGlyphs Glyphs => _glyphs;

        /// <param name="input">入力</param>
        /// <param name="devices">現在のデバイス (無ければ input.CurrentDevice を使い、ゲームパッドの種類は Generic 扱い)</param>
        /// <param name="settings">アイコン表 (<see cref="AbubuInputSettings.Glyphs"/>) の取得元。無ければ Resources/AbubuInputSettings</param>
        public InputGlyphService(IInputService input, [InjectOptional] IInputDeviceService devices = null,
            [InjectOptional] AbubuInputSettings settings = null)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _devices = devices;
            if (settings == null) settings = AbubuInputSettings.FindDefault();
            _glyphs = settings != null ? settings.Glyphs : null;

            var deviceChanged = devices != null
                ? devices.Current.Skip(1).AsUnitObservable()
                : input.CurrentScheme.Skip(1).AsUnitObservable();
            _changed = Observable.Merge(deviceChanged, input.BindingsChanged, _glyphsChanged).Share();
        }

        /// <summary>現在の割り当ての系統 (KeyboardMouse / Gamepad) と、最後に使われたゲームパッドの種類</summary>
        private InputDeviceState CurrentState => _devices != null
            ? new InputDeviceState(_devices.CurrentScheme.CurrentValue, _devices.CurrentGamepadKind.CurrentValue)
            : new InputDeviceState(_input.CurrentScheme.CurrentValue, GamepadKind.Generic);

        public InputGlyph GetGlyph(string actionPath, int bindingIndex = -1)
        {
            var action = _input.GetAction(actionPath);
            if (action == null) return InputGlyph.None;

            var state = CurrentState;
            var index = ResolveIndex(action, bindingIndex, state);
            if (index < 0) return InputGlyph.None;

            var display = _input.GetBindingDisplayString(actionPath, index);
            if (action.bindings[index].isComposite) return new InputGlyph(null, display, string.Empty, index);
            return CreateGlyph(action, index, state, display);
        }

        public IReadOnlyList<InputGlyph> GetCompositeGlyphs(string actionPath, int bindingIndex = -1)
        {
            var results = new List<InputGlyph>();
            var action = _input.GetAction(actionPath);
            if (action == null) return results;

            var state = CurrentState;
            var index = ResolveIndex(action, bindingIndex, state);
            if (index < 0) return results;

            var bindings = action.bindings;
            if (!bindings[index].isComposite)
            {
                results.Add(CreateGlyph(action, index, state, _input.GetBindingDisplayString(actionPath, index)));
                return results;
            }
            for (var i = index + 1; i < bindings.Count && bindings[i].isPartOfComposite; i++)
            {
                results.Add(CreateGlyph(action, i, state, _input.GetBindingDisplayString(actionPath, i)));
            }
            return results;
        }

        private static int ResolveIndex(InputAction action, int bindingIndex, InputDeviceState state)
        {
            var index = bindingIndex >= 0 ? bindingIndex : InputBindingUtility.FindBindingIndex(action, state.Kind);
            if (index < 0) index = FirstBinding(action);
            return index >= 0 && index < action.bindings.Count ? index : -1;
        }

        private InputGlyph CreateGlyph(InputAction action, int index, InputDeviceState state, string display)
        {
            var binding = action.bindings[index];
            var path = binding.effectivePath;
            // アイコンは割り当て自体のデバイスで探す (ゲームパッドの種類だけ現在の状態から取る)
            var pathKind = InputBindingUtility.GetDeviceKind(path);
            var sprite = _glyphs != null && !string.IsNullOrEmpty(path)
                ? _glyphs.GetSprite(path, pathKind != InputDeviceKind.Other ? pathKind : state.Kind, state.Gamepad)
                : null;
            return new InputGlyph(sprite, display, path, index, binding.isPartOfComposite ? binding.name : null);
        }

        public void SetGlyphs(InputGlyphs glyphs)
        {
            if (ReferenceEquals(_glyphs, glyphs)) return;
            _glyphs = glyphs;
            _glyphsChanged.OnNext(Unit.Default);
        }

        private static int FirstBinding(InputAction action)
        {
            var bindings = action.bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                if (!bindings[i].isPartOfComposite) return i;
            }
            return -1;
        }

        public void Dispose() => _glyphsChanged.Dispose();
    }
}
