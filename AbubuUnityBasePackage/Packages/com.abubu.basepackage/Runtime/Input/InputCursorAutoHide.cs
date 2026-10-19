using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Zenject;

namespace Abubu.Inputs
{
    /// <summary>
    /// ゲームパッドを使っている間はマウスカーソルを隠し、キーボード&amp;マウスに戻ったら (マウスを動かしたら) 表示する。
    /// 既定はオフ。<see cref="AbubuInputSettings.AutoHideCursor"/> をオンにすると InputModuleInstaller が有効にする。
    /// <code>
    /// // Resources/AbubuInputSettings の Auto Hide Cursor をオンにするだけ
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>このクラスが隠したカーソルだけを表示に戻す (ゲーム側が意図して隠しているカーソルを勝手に表示しない)。
    /// Dispose 時、隠したままなら表示に戻す。</item>
    /// <item>隠すとき、カーソルの下にある uGUI の部品に PointerExit を送る (ボタンのハイライトが残り、
    /// ゲームパッドで選んでいる項目と 2 つ光って見えるのを防ぐ)。表示に戻すときは PointerEnter を送り直す。</item>
    /// <item>マウスを少し動かしただけで表示に戻らないよう、移動量のしきい値は
    /// <see cref="InputDeviceSwitchSettings.PointerMoveThreshold"/> / PointerMoveWindow (短時間の移動量の合計) で判定する。</item>
    /// </list>
    /// </remarks>
    public sealed class InputCursorAutoHide : IInitializable, IDisposable
    {
        private readonly IInputDeviceService _devices;
        private readonly bool _enabled;
        private readonly Func<bool> _getVisible;
        private readonly Action<bool> _setVisible;
        private readonly Action<bool> _setPointerInside;
        private IDisposable _subscription;
        private bool _hidden;
        private bool _disposed;

        /// <param name="devices">現在のデバイス</param>
        /// <param name="settings">無ければ Resources/AbubuInputSettings、それも無ければ既定値 (オフ)</param>
        [Inject]
        public InputCursorAutoHide(IInputDeviceService devices, [InjectOptional] AbubuInputSettings settings = null)
            : this(devices, ResolveEnabled(settings), static () => Cursor.visible, static v => Cursor.visible = v, UiPointerHover.Set)
        {
        }

        /// <param name="devices">現在のデバイス</param>
        /// <param name="enabled">false なら何もしない</param>
        /// <param name="getVisible">カーソルの表示状態の取得 (テスト用)</param>
        /// <param name="setVisible">カーソルの表示状態の設定 (テスト用)</param>
        /// <param name="setPointerInside">
        /// カーソルの下の UI に PointerEnter (true) / PointerExit (false) を送る処理 (テスト用。null なら何もしない)
        /// </param>
        public InputCursorAutoHide(IInputDeviceService devices, bool enabled, Func<bool> getVisible, Action<bool> setVisible,
            Action<bool> setPointerInside = null)
        {
            _devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _enabled = enabled;
            _getVisible = getVisible ?? throw new ArgumentNullException(nameof(getVisible));
            _setVisible = setVisible ?? throw new ArgumentNullException(nameof(setVisible));
            _setPointerInside = setPointerInside;
        }

        private static bool ResolveEnabled(AbubuInputSettings settings)
        {
            if (settings == null) settings = AbubuInputSettings.FindDefault();
            return settings != null && settings.Enabled && settings.AutoHideCursor;
        }

        public void Initialize()
        {
            if (!_enabled || _disposed || _subscription != null) return;
            _subscription = _devices.CurrentDevice.Subscribe(this, static (kind, self) => self.OnDeviceChanged(kind));
        }

        private void OnDeviceChanged(InputDeviceKind kind)
        {
            if (kind == InputDeviceKind.Gamepad || kind == InputDeviceKind.Joystick)
            {
                if (_hidden || !_getVisible()) return;
                _setVisible(false);
                _hidden = true;
                _setPointerInside?.Invoke(false);
            }
            else if (_hidden)
            {
                _hidden = false;
                _setVisible(true);
                _setPointerInside?.Invoke(true);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();
            if (_hidden)
            {
                _hidden = false;
                _setVisible(true);
            }
        }
    }

    /// <summary>マウスカーソルの下にある uGUI の部品に PointerEnter / PointerExit を送る (<see cref="InputCursorAutoHide"/> 用)</summary>
    internal static class UiPointerHover
    {
        private static readonly List<RaycastResult> Hits = new();
        private static readonly List<GameObject> Targets = new();

        /// <summary>inside = false で PointerExit、true で PointerEnter を、カーソルの下の部品とその親に送る</summary>
        public static void Set(bool inside)
        {
            var eventSystem = EventSystem.current;
            var mouse = Mouse.current;
            if (eventSystem == null || mouse == null) return;

            var data = new PointerEventData(eventSystem) { position = mouse.position.ReadValue() };
            Hits.Clear();
            eventSystem.RaycastAll(data, Hits);
            if (Hits.Count == 0) return;

            // 一番手前の部品から親へたどる (uGUI のホバーと同じ範囲)
            Targets.Clear();
            for (var t = Hits[0].gameObject.transform; t != null; t = t.parent) Targets.Add(t.gameObject);
            Hits.Clear();

            data.pointerEnter = inside ? Targets[0] : null;
            foreach (var target in Targets)
            {
                if (inside) ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler);
                else ExecuteEvents.Execute(target, data, ExecuteEvents.pointerExitHandler);
            }
            Targets.Clear();
        }
    }
}
