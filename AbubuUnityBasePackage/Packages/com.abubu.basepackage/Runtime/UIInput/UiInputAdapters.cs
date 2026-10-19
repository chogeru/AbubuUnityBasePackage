using Abubu.Debugging;
using Abubu.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Zenject;

namespace Abubu.UI
{
    /// <summary>IInputService の操作デバイスから、ゲームパッド操作中かどうかを UI に伝える</summary>
    public sealed class InputServiceUiInputMode : IUiInputModeSource
    {
        private readonly IInputService _input;

        /// <param name="input">Input モジュールが無効なら null (常にマウス操作として扱う)</param>
        public InputServiceUiInputMode([InjectOptional] IInputService input = null) => _input = input;

        public bool IsNavigationMode => _input != null && _input.CurrentDevice.CurrentValue is InputDeviceKind.Gamepad or InputDeviceKind.Joystick;
    }

    /// <summary>決定 (UI/Submit) が押されたままか (<see cref="UiHoldButton"/> の長押し用)</summary>
    public sealed class InputServiceSubmitHold : IUiSubmitHoldSource
    {
        private readonly IInputService _input;
        private readonly string _submitPath;

        public InputServiceSubmitHold([InjectOptional] IInputService input = null, [InjectOptional] AbubuUiSettings settings = null)
        {
            _input = input;
            _submitPath = (settings != null ? settings : AbubuUiSettings.Default).SubmitActionPath;
        }

        public bool IsSubmitHeld => _input != null && !string.IsNullOrEmpty(_submitPath) && _input.IsPressed(_submitPath).CurrentValue;
    }

    /// <summary>Input System 用の EventSystem (InputSystemUIInputModule。アクションはプロジェクト共通の Input Actions) を作る</summary>
    public sealed class InputSystemEventSystemFactory : IUiEventSystemFactory
    {
        private readonly IInputService _input;

        public InputSystemEventSystemFactory([InjectOptional] IInputService input = null) => _input = input;

        public EventSystem Create()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            // IInputService と同じアクションを使う (無ければプロジェクト共通の Input Actions)。未設定だとゲームパッドで UI を操作できない
            var actions = _input?.Actions != null ? _input.Actions : InputSystem.actions;
            if (actions != null) module.actionsAsset = actions;
            return go.GetComponent<EventSystem>();
        }
    }

    /// <summary>
    /// 「戻る」を止めるべき状態: 選択中の UI が自分で Cancel を処理する (ICancelHandler)、Dropdown のリストが開いている、
    /// デバッグメニューをゲームパッドで操作中
    /// </summary>
    public sealed class BuiltInUiBackBlocker : IUiBackBlocker
    {
        public bool BlocksBack
        {
            get
            {
                if (DebugOverlay.HasGamepadFocus) return true;

                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == null) return false;

                // 文字を入力中 (Cancel は入力の取り消しに使われる)
                if (selected.TryGetComponent<UnityEngine.UI.InputField>(out var input) && input.isFocused) return true;
                if (selected.TryGetComponent<TMPro.TMP_InputField>(out var tmpInput) && tmpInput.isFocused) return true;

                // Dropdown / TMP_Dropdown が開いたリストの中 (Cancel でリストが閉じる)
                for (var t = selected.transform; t != null; t = t.parent)
                {
                    if (t.name == "Dropdown List") return true;
                }

                // 自前で Cancel を処理するコンポーネント (閉じている Dropdown は Cancel で何もしないので除く)
                if (selected.GetComponent<UnityEngine.UI.Dropdown>() != null || selected.GetComponent<TMPro.TMP_Dropdown>() != null) return false;
                return selected.GetComponent<ICancelHandler>() != null;
            }
        }
    }
}
