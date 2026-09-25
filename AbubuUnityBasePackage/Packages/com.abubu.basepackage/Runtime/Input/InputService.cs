using System;
using System.Collections.Generic;
using System.Threading;
using Abubu.Pause;
using Abubu.Save;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;
using Zenject;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.Input.InputModuleInstaller))]

namespace Abubu.Input
{
    public enum InputDeviceKind
    {
        KeyboardMouse,
        Gamepad,
        Touch,
    }

    /// <summary>
    /// 入力の窓口。Input System の型 (InputAction など) は外に出さず、Observable / ReactiveProperty だけを公開する。
    /// <code>
    /// _input.Move.Subscribe(v => velocity = v * speed).AddTo(this);
    /// _input.Jump.Subscribe(_ => Jump()).AddTo(this);
    /// _input.OnPerformed("Player/Sprint").Subscribe(...);   // 任意のアクションも名前で取れる
    /// </code>
    /// ポーズ中はゲーム操作用の ActionMap (既定 "Player") が無効になり、UI 操作だけ受け付ける。
    /// </summary>
    public interface IInputService
    {
        ReadOnlyReactiveProperty<Vector2> Move { get; }
        ReadOnlyReactiveProperty<Vector2> Look { get; }
        Observable<Unit> Jump { get; }
        Observable<Unit> Attack { get; }
        Observable<Unit> Interact { get; }
        Observable<Unit> Submit { get; }
        Observable<Unit> Cancel { get; }

        /// <summary>最後に操作されたデバイスの種類 (ボタン表示の切り替えなどに使う)</summary>
        ReadOnlyReactiveProperty<InputDeviceKind> CurrentDevice { get; }

        /// <summary>"Map/Action" 形式で任意のアクションの押下を購読する</summary>
        Observable<Unit> OnPerformed(string actionPath);
        /// <summary>"Map/Action" 形式で任意のボタンの押下状態を取得する</summary>
        ReadOnlyReactiveProperty<bool> IsPressed(string actionPath);

        /// <summary>ゲーム操作用の ActionMap を有効/無効にする (会話中・カットシーン中など)</summary>
        void SetGameplayEnabled(bool enabled);

        /// <summary>キー割り当てを対話的に変更し、保存する。Esc でキャンセル。成功したら true</summary>
        UniTask<bool> RebindAsync(string actionPath, int bindingIndex = 0, CancellationToken ct = default);
        /// <summary>割り当ての表示用文字列 ("Space" / "A" など)</summary>
        string GetBindingDisplayString(string actionPath, int bindingIndex = 0);
        /// <summary>割り当ての変更をすべて初期状態に戻す</summary>
        void ResetBindings();
    }

    /// <summary>
    /// 任意。Resources/AbubuInputSettings として置くと読み込まれる。無ければ既定値
    /// (プロジェクト共通の Input Actions = Unity 6 テンプレートの InputSystem_Actions) を使う。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/Input Settings", fileName = "AbubuInputSettings")]
    public sealed class AbubuInputSettings : ScriptableObject
    {
        public const string ResourcePath = "AbubuInputSettings";

        [Tooltip("未設定ならプロジェクト共通の Input Actions (Project Settings > Input System Package) を使う")]
        public InputActionAsset Actions;
        public string GameplayMap = "Player";

        [Header("Action パス (Map/Action)")]
        public string Move = "Player/Move";
        public string Look = "Player/Look";
        public string Jump = "Player/Jump";
        public string Attack = "Player/Attack";
        public string Interact = "Player/Interact";
        public string Submit = "UI/Submit";
        public string Cancel = "UI/Cancel";

        [Tooltip("ポーズ中はゲーム操作用の ActionMap を無効にする")]
        public bool DisableGameplayWhilePaused = true;
    }

    public sealed class InputService : IInputService, IInitializable, IDisposable
    {
        private const string SaveKey = "abubu.input-bindings";

        private readonly AbubuInputSettings _settings;
        private readonly ISaveService _save;
        private readonly IPauseService _pause;
        private readonly InputActionAsset _asset;
        private readonly InputActionMap _gameplayMap;
        private readonly CompositeDisposable _disposables = new();
        private readonly Dictionary<string, Subject<Unit>> _performed = new();
        private readonly Dictionary<string, ReactiveProperty<bool>> _pressed = new();
        private readonly List<(InputAction action, Action<InputAction.CallbackContext> handler, bool performedOnly)> _handlers = new();
        private readonly ReactiveProperty<Vector2> _move = new(Vector2.zero);
        private readonly ReactiveProperty<Vector2> _look = new(Vector2.zero);
        private readonly ReactiveProperty<InputDeviceKind> _device = new(InputDeviceKind.KeyboardMouse);
        private bool _gameplayRequested = true;

        public ReadOnlyReactiveProperty<Vector2> Move => _move;
        public ReadOnlyReactiveProperty<Vector2> Look => _look;
        public Observable<Unit> Jump => OnPerformed(_settings.Jump);
        public Observable<Unit> Attack => OnPerformed(_settings.Attack);
        public Observable<Unit> Interact => OnPerformed(_settings.Interact);
        public Observable<Unit> Submit => OnPerformed(_settings.Submit);
        public Observable<Unit> Cancel => OnPerformed(_settings.Cancel);
        public ReadOnlyReactiveProperty<InputDeviceKind> CurrentDevice => _device;

        public InputService(ISaveService save, IPauseService pause, AbubuInputSettings settings = null)
        {
            _save = save;
            _pause = pause;
            _settings = settings != null ? settings : ScriptableObject.CreateInstance<AbubuInputSettings>();
            _asset = _settings.Actions != null ? _settings.Actions : InputSystem.actions;
            _gameplayMap = _asset != null ? _asset.FindActionMap(_settings.GameplayMap) : null;
        }

        public void Initialize()
        {
            if (_asset == null)
            {
                Debug.LogWarning("[Abubu.Input] Input Actions が見つかりません。Project Settings > Input System Package で Project-wide Actions を設定するか、Resources/AbubuInputSettings を作成してください。");
                return;
            }

            var overrides = _save.Load(SaveKey, string.Empty);
            if (!string.IsNullOrEmpty(overrides)) _asset.LoadBindingOverridesFromJson(overrides);
            _asset.Enable();

            BindVector(_settings.Move, _move);
            BindVector(_settings.Look, _look);

            foreach (var map in _asset.actionMaps) map.actionTriggered += OnActionTriggered;

            if (_settings.DisableGameplayWhilePaused && _pause != null)
            {
                _pause.IsPaused.Subscribe(this, static (_, self) => self.ApplyGameplayEnabled()).AddTo(_disposables);
            }
        }

        public Observable<Unit> OnPerformed(string actionPath)
        {
            if (_performed.TryGetValue(actionPath, out var subject)) return subject;

            subject = new Subject<Unit>();
            _performed.Add(actionPath, subject);
            var action = FindAction(actionPath);
            if (action != null) AddHandler(action, _ => subject.OnNext(Unit.Default), performedOnly: true);
            return subject;
        }

        public ReadOnlyReactiveProperty<bool> IsPressed(string actionPath)
        {
            if (_pressed.TryGetValue(actionPath, out var property)) return property;

            property = new ReactiveProperty<bool>(false);
            _pressed.Add(actionPath, property);
            var action = FindAction(actionPath);
            if (action != null) AddHandler(action, ctx => property.Value = ctx.action.IsPressed(), performedOnly: false);
            return property;
        }

        public void SetGameplayEnabled(bool enabled)
        {
            _gameplayRequested = enabled;
            ApplyGameplayEnabled();
        }

        public async UniTask<bool> RebindAsync(string actionPath, int bindingIndex = 0, CancellationToken ct = default)
        {
            var action = FindAction(actionPath);
            if (action == null) return false;

            var wasEnabled = action.enabled;
            action.Disable();
            var completion = new UniTaskCompletionSource<bool>();
            using var operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(_ => completion.TrySetResult(true))
                .OnCancel(_ => completion.TrySetResult(false))
                .Start();

            bool result;
            using (ct.Register(() => operation.Cancel()))
            {
                result = await completion.Task;
            }

            if (wasEnabled) action.Enable();
            if (result) _save.Save(SaveKey, _asset.SaveBindingOverridesAsJson());
            return result;
        }

        public string GetBindingDisplayString(string actionPath, int bindingIndex = 0)
        {
            var action = FindAction(actionPath);
            return action != null && bindingIndex < action.bindings.Count ? action.GetBindingDisplayString(bindingIndex) : string.Empty;
        }

        public void ResetBindings()
        {
            if (_asset == null) return;
            _asset.RemoveAllBindingOverrides();
            _save.Delete(SaveKey);
        }

        private void ApplyGameplayEnabled()
        {
            if (_gameplayMap == null) return;
            var paused = _settings.DisableGameplayWhilePaused && _pause != null && _pause.IsPaused.CurrentValue;
            if (_gameplayRequested && !paused) _gameplayMap.Enable();
            else
            {
                _gameplayMap.Disable();
                _move.Value = Vector2.zero;
                _look.Value = Vector2.zero;
            }
        }

        private void BindVector(string actionPath, ReactiveProperty<Vector2> property)
        {
            var action = FindAction(actionPath);
            if (action == null) return;
            AddHandler(action, ctx => property.Value = ctx.canceled ? Vector2.zero : ctx.ReadValue<Vector2>(), performedOnly: false);
        }

        private void AddHandler(InputAction action, Action<InputAction.CallbackContext> handler, bool performedOnly)
        {
            action.performed += handler;
            if (!performedOnly) action.canceled += handler;
            _handlers.Add((action, handler, performedOnly));
        }

        private InputAction FindAction(string actionPath)
        {
            if (_asset == null || string.IsNullOrEmpty(actionPath)) return null;
            var action = _asset.FindAction(actionPath);
            if (action == null) Debug.LogWarning($"[Abubu.Input] アクション \"{actionPath}\" が見つかりません。");
            return action;
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            var device = context.control?.device;
            _device.Value = device switch
            {
                Gamepad => InputDeviceKind.Gamepad,
                Touchscreen => InputDeviceKind.Touch,
                null => _device.Value,
                _ => InputDeviceKind.KeyboardMouse,
            };
        }

        public void Dispose()
        {
            _disposables.Dispose();
            foreach (var (action, handler, performedOnly) in _handlers)
            {
                action.performed -= handler;
                if (!performedOnly) action.canceled -= handler;
            }
            _handlers.Clear();
            if (_asset != null)
            {
                foreach (var map in _asset.actionMaps) map.actionTriggered -= OnActionTriggered;
            }
            foreach (var subject in _performed.Values) subject.Dispose();
            foreach (var property in _pressed.Values) property.Dispose();
            _move.Dispose();
            _look.Dispose();
            _device.Dispose();
        }
    }

    /// <summary>Input System 導入時に AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class InputModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public InputModuleInstaller() { }

        public void Install(DiContainer container)
        {
            var settings = Resources.Load<AbubuInputSettings>(AbubuInputSettings.ResourcePath);
            container.BindInterfacesTo<InputService>().AsSingle().WithArguments(settings);
        }
    }
}

namespace Abubu
{
    using Abubu.Input;

    /// <summary>
    /// 入力の static ショートカット。
    /// <code>
    /// var move = GameInput.Move;             // 現在の移動入力
    /// GameInput.Jump.Subscribe(_ => ...);
    /// </code>
    /// </summary>
    public static class GameInput
    {
        public static IInputService Service => AbubuServices.TryResolve<IInputService>();

        public static Vector2 Move => Service?.Move.CurrentValue ?? Vector2.zero;
        public static Vector2 Look => Service?.Look.CurrentValue ?? Vector2.zero;
        public static Observable<Unit> Jump => Service?.Jump ?? Observable.Empty<Unit>();
        public static Observable<Unit> Attack => Service?.Attack ?? Observable.Empty<Unit>();
        public static Observable<Unit> Submit => Service?.Submit ?? Observable.Empty<Unit>();
        public static Observable<Unit> Cancel => Service?.Cancel ?? Observable.Empty<Unit>();
        public static Observable<Unit> OnPerformed(string actionPath) => Service?.OnPerformed(actionPath) ?? Observable.Empty<Unit>();
    }
}
