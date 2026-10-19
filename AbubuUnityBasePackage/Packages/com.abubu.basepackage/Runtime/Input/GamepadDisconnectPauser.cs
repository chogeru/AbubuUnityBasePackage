using System;
using Abubu.Pause;
using R3;
using Zenject;

namespace Abubu.Inputs
{
    /// <summary>
    /// 使用中のゲームパッドが切断されたらポーズし、ゲームパッドがつながったら再開する。
    /// <see cref="AbubuInputSettings.PauseOnGamepadDisconnect"/> (既定オン) のとき InputModuleInstaller が有効にする。
    /// <code>
    /// // 「コントローラーを接続してください」の表示は IsWaitingForReconnect を見て出す
    /// [Inject] GamepadDisconnectPauser _pauser;
    /// _pauser.IsWaitingForReconnect.Subscribe(waiting => reconnectPanel.SetActive(waiting)).AddTo(this);
    /// _pauser.Dismiss();   // 「キーボードで続ける」ボタンなどから。待つのをやめてポーズを解除する
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>ポーズするのは、切断されたゲームパッドが使用中だった (<see cref="InputDeviceConnection.WasInUse"/>) ときだけ。
    /// キーボードで遊んでいる間に、使っていないゲームパッドが抜けてもポーズしない</item>
    /// <item>IPauseService が無ければポーズはせず、<see cref="IsWaitingForReconnect"/> だけを切り替える</item>
    /// <item>ポーズは自分を owner にして行うので、他のポーズ (ポーズメニューなど) とは干渉しない</item>
    /// </list>
    /// </remarks>
    public sealed class GamepadDisconnectPauser : IInitializable, IDisposable
    {
        private readonly IInputDeviceService _devices;
        private readonly IPauseService _pause;
        private readonly bool _enabled;
        private readonly ReactiveProperty<bool> _waiting = new(false);
        private readonly CompositeDisposable _subscriptions = new();
        private bool _paused;
        private bool _initialized;
        private bool _disposed;

        /// <summary>使用中のゲームパッドが切断され、再接続を待っている間 true (「コントローラーを接続してください」の表示用)</summary>
        public ReadOnlyReactiveProperty<bool> IsWaitingForReconnect => _waiting;

        /// <param name="devices">デバイスの接続・切断の通知元</param>
        /// <param name="pause">ポーズ (任意。無ければ IsWaitingForReconnect だけを切り替える)</param>
        /// <param name="settings">無ければ Resources/AbubuInputSettings、それも無ければ既定値 (オン)</param>
        [Inject]
        public GamepadDisconnectPauser(IInputDeviceService devices, [InjectOptional] IPauseService pause = null,
            [InjectOptional] AbubuInputSettings settings = null)
            : this(devices, pause, ResolveEnabled(settings))
        {
        }

        /// <param name="devices">デバイスの接続・切断の通知元</param>
        /// <param name="pause">ポーズ (任意)</param>
        /// <param name="enabled">false なら何もしない</param>
        public GamepadDisconnectPauser(IInputDeviceService devices, IPauseService pause, bool enabled)
        {
            _devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _pause = pause;
            _enabled = enabled;
        }

        private static bool ResolveEnabled(AbubuInputSettings settings)
        {
            if (settings == null) settings = AbubuInputSettings.FindDefault();
            return settings == null || (settings.Enabled && settings.PauseOnGamepadDisconnect);
        }

        public void Initialize()
        {
            if (_initialized || _disposed) return;
            _initialized = true;
            if (!_enabled) return;
            _devices.DeviceLost.Subscribe(this, static (c, self) => self.OnLost(c)).AddTo(_subscriptions);
            _devices.DeviceRegained.Subscribe(this, static (c, self) => self.OnRegained(c)).AddTo(_subscriptions);
        }

        private void OnLost(InputDeviceConnection connection)
        {
            if (_waiting.Value || connection.Kind != InputDeviceKind.Gamepad || !connection.WasInUse) return;
            _waiting.Value = true;
            if (_pause != null && !_paused)
            {
                _pause.Pause(this);
                _paused = true;
            }
        }

        private void OnRegained(InputDeviceConnection connection)
        {
            if (connection.Kind == InputDeviceKind.Gamepad) Dismiss();
        }

        /// <summary>再接続を待つのをやめ、このクラスが掛けたポーズを解除する (「キーボードで続ける」など)</summary>
        public void Dismiss()
        {
            if (_disposed) return;
            if (_paused)
            {
                _paused = false;
                _pause.Resume(this);
            }
            _waiting.Value = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Dismiss();
            _disposed = true;
            _subscriptions.Dispose();
            _waiting.Dispose();
        }
    }
}
