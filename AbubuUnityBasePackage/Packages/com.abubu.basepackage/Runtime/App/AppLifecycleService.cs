using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.App
{
    /// <summary>「中断」(<see cref="IAppLifecycle.OnSuspend"/> と自動保存) として扱うきっかけ</summary>
    [Flags]
    public enum SuspendTrigger
    {
        None = 0,
        /// <summary>バックグラウンドに回ったとき (OnApplicationPause)。モバイルではここが最後の保存の機会になることが多い</summary>
        Background = 1 << 0,
        /// <summary>ウィンドウ・タブのフォーカスが外れたとき</summary>
        FocusLost = 1 << 1,
        /// <summary>アプリの終了時 (Application.quitting)</summary>
        Quit = 1 << 2,
        /// <summary>WebGL でフォーカスが外れたとき (タブを閉じる前に終了処理が呼ばれないため)</summary>
        FocusLostOnWebGL = 1 << 3,
    }

    [Serializable]
    public sealed class AppSettings
    {
        [Tooltip("中断として扱うきっかけ。None にすると OnSuspend も自動保存も行わない")]
        public SuspendTrigger SuspendTriggers = SuspendTrigger.Background | SuspendTrigger.Quit | SuspendTrigger.FocusLostOnWebGL;

        [Tooltip("中断時に、パッケージが持つ設定 (音量・画面設定など) を保存する")]
        public bool SaveSettingsOnSuspend = true;

        [Tooltip("ウィンドウのフォーカスが外れている間、IPauseService でポーズする (PC 向け。モバイルはバックグラウンドで OS が止める)")]
        public bool PauseOnFocusLost;

        [Tooltip("ウィンドウのフォーカスが外れている間、音を消す (AudioListener.volume を 0 にする)")]
        public bool MuteOnFocusLost;
    }

    /// <summary>
    /// アプリが中断されるときに呼ばれる。DI コンテナに IAppSuspendHandler としてバインドしておくと、
    /// バックグラウンドに回る・終了する直前に呼ばれるので、未保存のデータをここで保存する。
    /// <code>
    /// Container.Bind&lt;IAppSuspendHandler&gt;().To&lt;ProgressAutoSave&gt;().AsSingle();
    /// </code>
    /// </summary>
    /// <remarks>OS が待ってくれる時間は短いので、同期的に終わる処理だけを書く (await しない)。</remarks>
    public interface IAppSuspendHandler
    {
        void OnSuspend();
    }

    /// <summary>
    /// アプリの終了時に呼ばれる (中断のきっかけ SuspendTriggers の設定に関係なく、終了時は必ず呼ばれる)。
    /// IAppLifecycle を受け取らずに済むので、IAppLifecycle が使う側から循環参照にならない。
    /// </summary>
    /// <remarks>モバイル・WebGL では呼ばれないことがある。保存は IAppSuspendHandler で行うこと。</remarks>
    public interface IAppQuitHandler
    {
        void OnQuit();
    }

    /// <summary>
    /// アプリの中断・復帰・終了の通知。
    /// モバイルはバックグラウンドに回ったあと終了処理なしで落とされることがあるため、保存は <see cref="OnSuspend"/> で行う。
    /// <code>
    /// _app.OnSuspend.Subscribe(_ => SaveProgress()).AddTo(this);
    /// </code>
    /// </summary>
    public interface IAppLifecycle
    {
        /// <summary>ウィンドウ (アプリ) にフォーカスがあるか</summary>
        ReadOnlyReactiveProperty<bool> IsFocused { get; }

        /// <summary>中断中か (バックグラウンドに回っている、など)</summary>
        ReadOnlyReactiveProperty<bool> IsSuspended { get; }

        /// <summary>中断される直前。バックグラウンドへの移行・終了 (設定によってはフォーカス喪失) で 1 回ずつ流れる</summary>
        Observable<Unit> OnSuspend { get; }

        /// <summary>中断から戻ったとき</summary>
        Observable<Unit> OnResume { get; }

        /// <summary>アプリの終了時 (モバイル・WebGL では呼ばれないことがある)</summary>
        Observable<Unit> OnQuit { get; }
    }

    public sealed class AppLifecycleService : IAppLifecycle, IInitializable, IDisposable
    {
        private readonly AppSettings _settings;
        private readonly List<IAppSuspendHandler> _handlers;
        private readonly List<IAppQuitHandler> _quitHandlers;
        private readonly SuspendTrigger _triggers;
        private readonly ReactiveProperty<bool> _isFocused = new(true);
        private readonly ReactiveProperty<bool> _isSuspended = new(false);
        private readonly Subject<Unit> _onSuspend = new();
        private readonly Subject<Unit> _onResume = new();
        private readonly Subject<Unit> _onQuit = new();
        private GameObject _relay;
        private bool _quitting;

        public ReadOnlyReactiveProperty<bool> IsFocused => _isFocused;
        public ReadOnlyReactiveProperty<bool> IsSuspended => _isSuspended;
        public Observable<Unit> OnSuspend => _onSuspend;
        public Observable<Unit> OnResume => _onResume;
        public Observable<Unit> OnQuit => _onQuit;

        public AppLifecycleService([InjectOptional] AppSettings settings = null, [InjectOptional] List<IAppSuspendHandler> handlers = null,
            [InjectOptional] List<IAppQuitHandler> quitHandlers = null)
        {
            _quitHandlers = quitHandlers ?? new List<IAppQuitHandler>();
            _settings = settings ?? new AppSettings();
            _handlers = handlers ?? new List<IAppSuspendHandler>();
            _triggers = _settings.SuspendTriggers;
            if (Application.platform == RuntimePlatform.WebGLPlayer && (_triggers & SuspendTrigger.FocusLostOnWebGL) != 0)
            {
                _triggers |= SuspendTrigger.FocusLost;
            }
        }

        public void Initialize()
        {
            Application.focusChanged += NotifyFocus;
            Application.quitting += NotifyQuit;

            // OnApplicationPause は MonoBehaviour でしか受け取れないため、中継用のオブジェクトを置く
            _relay = new GameObject("[Abubu.App]");
            Object.DontDestroyOnLoad(_relay);
            _relay.hideFlags = HideFlags.HideInHierarchy;
            _relay.AddComponent<AppLifecycleRelay>().Initialize(this);
        }

        internal void NotifyPause(bool paused)
        {
            if ((_triggers & SuspendTrigger.Background) == 0) return;
            if (paused) Suspend();
            else Resume();
        }

        internal void NotifyFocus(bool focused)
        {
            _isFocused.Value = focused;
            if ((_triggers & SuspendTrigger.FocusLost) == 0) return;
            if (focused) Resume();
            else Suspend();
        }

        internal void NotifyQuit()
        {
            if (_quitting) return;
            _quitting = true;
            // 中断中に終了した場合は、中断時に保存済み
            if ((_triggers & SuspendTrigger.Quit) != 0) Suspend();
            foreach (var handler in _quitHandlers)
            {
                try
                {
                    handler.OnQuit();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            _onQuit.OnNext(Unit.Default);
        }

        /// <summary>中断の通知。モバイルではフォーカス喪失とバックグラウンド移行が続けて来るので、1 回にまとめる</summary>
        private void Suspend()
        {
            if (_isSuspended.Value) return;
            _isSuspended.Value = true;

            foreach (var handler in _handlers)
            {
                try
                {
                    handler.OnSuspend();
                }
                catch (Exception e)
                {
                    // 1 つの失敗で他の保存処理が止まらないようにする
                    Debug.LogException(e);
                }
            }
            _onSuspend.OnNext(Unit.Default);
        }

        private void Resume()
        {
            if (!_isSuspended.Value || _quitting) return;
            _isSuspended.Value = false;
            _onResume.OnNext(Unit.Default);
        }

        public void Dispose()
        {
            Application.focusChanged -= NotifyFocus;
            Application.quitting -= NotifyQuit;
            if (_relay != null) Object.Destroy(_relay);

            _isFocused.Dispose();
            _isSuspended.Dispose();
            _onSuspend.Dispose();
            _onResume.Dispose();
            _onQuit.Dispose();
        }
    }
}

namespace Abubu
{
    using Abubu.App;
    using R3;

    /// <summary>
    /// アプリのライフサイクルの static ショートカット。
    /// <code>
    /// AppLifecycle.OnSuspend.Subscribe(_ => Saves.Save("progress", progress)).AddTo(this);
    /// </code>
    /// </summary>
    public static class AppLifecycle
    {
        public static IAppLifecycle Service => AbubuServices.TryResolve<IAppLifecycle>();

        public static Observable<Unit> OnSuspend => Service?.OnSuspend ?? Observable.Empty<Unit>();
        public static Observable<Unit> OnResume => Service?.OnResume ?? Observable.Empty<Unit>();
        public static Observable<Unit> OnQuit => Service?.OnQuit ?? Observable.Empty<Unit>();
        public static bool IsFocused => Service?.IsFocused.CurrentValue ?? true;
    }
}
