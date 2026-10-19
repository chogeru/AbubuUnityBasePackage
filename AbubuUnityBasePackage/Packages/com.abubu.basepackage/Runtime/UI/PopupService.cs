using System;
using System.Collections.Generic;
using Abubu.Pause;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using UnityEngine.UI;
using Zenject;
using Object = UnityEngine.Object;
using UnityScene = UnityEngine.SceneManagement.Scene;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.UI.UiModuleInstaller))]

namespace Abubu.UI
{
    /// <summary>
    /// ポップアップに付けると、開閉のタイミングで通知を受け取れる (任意)。
    /// 開閉のアニメーションは <see cref="IUiTransition"/>、最初に選択するボタンは <see cref="UiInitialSelection"/> で指定する。
    /// </summary>
    public interface IPopup
    {
        /// <summary>表示された直後</summary>
        void OnOpened();

        /// <summary>閉じられる直前 (閉じるアニメーションと破棄の前)</summary>
        void OnClosing();
    }

    /// <summary>
    /// ポップアップ (ダイアログ・設定画面など) をスタックで管理する。後から開いたものが手前に来て、Pop は一番手前から閉じる。
    /// <code>
    /// var dialog = _popups.Push(confirmDialogPrefab);
    /// _popups.Pop();          // 一番上を閉じる
    /// _popups.Close(dialog);  // 指定したものだけ閉じる (ポップアップ自身の「閉じる」ボタンから呼ぶ)
    /// _popups.PopAll();
    /// </code>
    /// 背面を覆う Backdrop が自動で付き、下のポップアップや画面は操作できなくなる。
    /// ポップアップを外から Destroy しても、Backdrop・ポーズ・Count はその場で更新される。
    /// インスタンスは <see cref="IUiFactory"/> で作る (既定は DI コンテナ経由なので [Inject] が使える)。
    /// </summary>
    public interface IPopupService
    {
        /// <summary>開いているポップアップの数</summary>
        ReadOnlyReactiveProperty<int> Count { get; }

        /// <summary>プレハブをポップアップ用 Canvas の下に生成して一番手前に表示する</summary>
        GameObject Push(GameObject prefab);

        /// <summary>コンポーネント型で指定する版。戻り値も同じ型で受け取れる</summary>
        T Push<T>(T prefab) where T : Component;

        /// <summary>一番手前のポップアップを閉じる。閉じるものが無ければ false</summary>
        bool Pop();

        /// <summary>指定したポップアップだけを閉じる (上に重なっているものは残す)。見つからなければ false</summary>
        bool Close(GameObject popup);

        /// <summary>指定したポップアップとその上に重なっているものをすべて閉じる</summary>
        void PopTo(GameObject popup);

        /// <summary>すべて閉じる</summary>
        void PopAll();
    }

    public sealed class PopupService : IPopupService, IDisposable
    {
        private sealed class Entry
        {
            public GameObject Popup;
            public GameObject Backdrop;
            /// <summary>開いたときのアクティブなシーン。そのシーンが閉じたらポップアップも閉じる (CloseOnSceneChange)</summary>
            public UnityScene Scene;
            /// <summary>開く前に選択されていたもの。閉じたときに選択を戻す</summary>
            public GameObject PreviousSelection;
        }

        private readonly AbubuUiSettings _settings;
        private readonly IPauseService _pause;
        private readonly IUiFactory _factory;
        private readonly UiTransitionRunner _transitions = new();
        private readonly List<Entry> _stack = new();
        private readonly ReactiveProperty<int> _count = new(0);
        private Canvas _canvas;
        private bool _warnedNoEventSystem;
        private bool _disposed;
        private bool _disposing;

        public ReadOnlyReactiveProperty<int> Count => _count;

        /// <param name="factory">インスタンスの生成方法。null なら注入なしで Instantiate する (テスト用)</param>
        public PopupService([InjectOptional] IPauseService pause = null, [InjectOptional] AbubuUiSettings settings = null,
            [InjectOptional] IUiFactory factory = null)
        {
            _pause = pause;
            _settings = settings != null ? settings : ScriptableObject.CreateInstance<AbubuUiSettings>();
            _factory = factory ?? new ContainerUiFactory();
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        // Canvas は DontDestroyOnLoad なので、放っておくと前のシーンのポップアップが次のシーンに残る。
        // アクティブシーンの切り替え (加算ロードでの SetActiveScene) では閉じず、開いたときのシーンが閉じたときだけ閉じる
        private void OnSceneUnloaded(UnityScene scene)
        {
            if (!_settings.CloseOnSceneChange) return;
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                if (i < _stack.Count && _stack[i].Scene == scene) Remove(_stack[i], restoreSelection: false);
            }
        }

        public GameObject Push(GameObject prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            PurgeDestroyed();
            WarnIfNoEventSystem();
            var canvas = EnsureCanvas();
            var backdrop = CreateBackdrop(canvas.transform);
            GameObject popup;
            try
            {
                popup = _factory.Create(prefab, canvas.transform);
                if (popup == null) throw new InvalidOperationException($"IUiFactory が {prefab.name} のインスタンスを返しませんでした。");
            }
            catch
            {
                // 生成に失敗したときに Backdrop だけが残ると、画面全体が操作できなくなる
                Object.Destroy(backdrop);
                throw;
            }

            popup.name = prefab.name;
            var entry = new Entry
            {
                Popup = popup,
                Backdrop = backdrop,
                Scene = SceneManager.GetActiveScene(),
                PreviousSelection = UiInitialSelection.Current,
            };
            _stack.Add(entry);
            UiDestroyNotifier.Attach(popup, OnPopupDestroyed);
            UpdateState();

            foreach (var receiver in popup.GetComponents<IPopup>())
            {
                try
                {
                    receiver.OnOpened();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, popup);
                }
            }

            UiInitialSelection.Apply(popup);
            _transitions.Show(popup);
            return popup;
        }

        public T Push<T>(T prefab) where T : Component
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            return Push(prefab.gameObject).GetComponent<T>();
        }

        public bool Pop()
        {
            PurgeDestroyed();
            if (_stack.Count == 0) return false;
            Remove(_stack[^1], restoreSelection: true);
            return true;
        }

        public bool Close(GameObject popup)
        {
            var entry = Find(popup);
            if (entry == null) return false;
            Remove(entry, restoreSelection: entry == _stack[^1]);
            return true;
        }

        public void PopTo(GameObject popup)
        {
            var entry = Find(popup);
            if (entry == null) return;

            // 手前から順に閉じる
            while (_stack.Contains(entry)) Pop();
        }

        public void PopAll()
        {
            while (Pop())
            {
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            // 終了時はアニメーションを待たずに片付ける
            _disposing = true;
            _transitions.CancelAll();
            PopAll();
            _disposed = true;
            _count.Dispose();
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
        }

        private Entry Find(GameObject popup)
        {
            PurgeDestroyed();
            return popup == null ? null : _stack.Find(e => e.Popup == popup);
        }

        /// <summary>
        /// スタックから外してから閉じる。先に外しておくことで、OnClosing の例外や OnClosing 内での Push / Pop があっても状態が崩れない
        /// </summary>
        private void Remove(Entry entry, bool restoreSelection)
        {
            if (!_stack.Remove(entry)) return;
            UpdateState();

            if (entry.Popup != null)
            {
                foreach (var receiver in entry.Popup.GetComponents<IPopup>())
                {
                    try
                    {
                        receiver.OnClosing();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, entry.Popup);
                    }
                }
            }

            if (restoreSelection) UiInitialSelection.Restore(entry.PreviousSelection);

            // 閉じるアニメーションが終わるまで Backdrop は残す (アニメーション中に背面が押されないように)
            if (_disposing) Release();
            else _transitions.Hide(entry.Popup, Release);

            void Release()
            {
                _factory.Release(entry.Popup);
                if (entry.Backdrop != null) Object.Destroy(entry.Backdrop);
            }
        }

        /// <summary>ポップアップが外から Destroy されたとき (閉じるボタンで Destroy(gameObject) した、シーンごと消えた など)</summary>
        private void OnPopupDestroyed(GameObject popup)
        {
            if (_disposed) return;
            // Pop で閉じたものはスタックから外し済みなので見つからない
            var entry = _stack.Find(e => ReferenceEquals(e.Popup, popup));
            if (entry == null) return;

            _stack.Remove(entry);
            if (entry.Backdrop != null) Object.Destroy(entry.Backdrop);
            UpdateState();
        }

        /// <summary>
        /// 外から Destroy されたのに通知が来なかったポップアップを外す
        /// (一度もアクティブにならなかったオブジェクトは OnDestroy が呼ばれないため)
        /// </summary>
        private void PurgeDestroyed()
        {
            var removed = false;
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Popup != null) continue;
                if (_stack[i].Backdrop != null) Object.Destroy(_stack[i].Backdrop);
                _stack.RemoveAt(i);
                removed = true;
            }

            if (removed) UpdateState();
        }

        private void UpdateState()
        {
            if (_disposed) return;
            _count.Value = _stack.Count;
            if (!_settings.PauseWhileOpen || _pause == null) return;

            if (_stack.Count > 0) _pause.Pause(this);
            else _pause.Resume(this);
        }

        private void WarnIfNoEventSystem()
        {
            if (_warnedNoEventSystem || EventSystem.current != null) return;
            _warnedNoEventSystem = true;
            Debug.LogWarning("[Abubu.UI] シーンに EventSystem が無いため、ポップアップのボタンは押せません。GameObject > UI > Event System を追加してください。");
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            return _canvas = UiUtility.CreateCanvas(_settings.PopupCanvasPrefab, "[Abubu.UI]", _settings.SortingOrder, _settings);
        }

        private GameObject CreateBackdrop(Transform parent)
        {
            GameObject go;
            if (_settings.BackdropPrefab != null)
            {
                go = Object.Instantiate(_settings.BackdropPrefab, parent, false);
                go.name = "Backdrop";
            }
            else
            {
                go = new GameObject("Backdrop", typeof(RectTransform));
                go.transform.SetParent(parent, false);

                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                // alpha 0 でも raycastTarget が有効なら背面の入力は遮断される
                var image = go.AddComponent<Image>();
                image.color = _settings.BackdropColor;
                image.raycastTarget = true;
            }

            if (_settings.CloseOnBackdropClick)
            {
                var trigger = go.GetComponent<EventTrigger>();
                if (trigger == null) trigger = go.AddComponent<EventTrigger>();
                var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                click.callback.AddListener(_ => Pop());
                trigger.triggers.Add(click);
            }

            return go;
        }
    }

    /// <summary>UI モジュール。AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class UiModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public UiModuleInstaller() { }

        public void Install(DiContainer container)
        {
            var settings = ResolveSettings(container);
            container.Bind<IUiFactory>().To<ContainerUiFactory>().AsSingle().IfNotBound();
            if (!container.HasBinding<IPopupService>()) container.BindInterfacesTo<PopupService>().AsSingle().WithArguments(settings);
            if (!container.HasBinding<IScreenService>()) container.BindInterfacesTo<ScreenService>().AsSingle().WithArguments(settings);
        }

        /// <summary>
        /// UI の設定。コンテナにバインド済みのもの (AbubuSettings.Ui) → Resources/AbubuUiSettings → 既定値 (null) の順に探す
        /// </summary>
        public static AbubuUiSettings ResolveSettings(DiContainer container)
        {
            if (container.HasBinding<AbubuUiSettings>()) return container.Resolve<AbubuUiSettings>();
            var settings = Resources.Load<AbubuUiSettings>(AbubuUiSettings.ResourcePath);
            if (settings != null) container.BindInstance(settings);
            return settings;
        }
    }
}

namespace Abubu
{
    using Abubu.UI;

    /// <summary>
    /// ポップアップの static ショートカット。
    /// <code>
    /// Popups.Push(confirmDialogPrefab);
    /// Popups.Pop();
    /// </code>
    /// </summary>
    public static class Popups
    {
        public static IPopupService Service => AbubuServices.TryResolve<IPopupService>();

        public static GameObject Push(GameObject prefab) => Service?.Push(prefab);
        public static T Push<T>(T prefab) where T : Component => Service?.Push(prefab);
        public static bool Pop() => Service?.Pop() ?? false;
        public static bool Close(GameObject popup) => Service?.Close(popup) ?? false;
        public static void PopAll() => Service?.PopAll();
        public static int Count => Service?.Count.CurrentValue ?? 0;
    }
}
