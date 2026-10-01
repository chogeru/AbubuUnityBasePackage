using System;
using System.Collections.Generic;
using Abubu.Pause;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting;
using UnityEngine.UI;
using Zenject;
using Object = UnityEngine.Object;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.UI.UiModuleInstaller))]

namespace Abubu.UI
{
    /// <summary>
    /// ポップアップに付けると、開閉のタイミングで通知を受け取れる (任意)。
    /// </summary>
    public interface IPopup
    {
        /// <summary>表示された直後</summary>
        void OnOpened();

        /// <summary>閉じられる直前 (Destroy の前)</summary>
        void OnClosing();
    }

    /// <summary>
    /// 任意。Resources/AbubuUiSettings として置くと読み込まれる。無ければ既定値を使う。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/UI Settings", fileName = "AbubuUiSettings")]
    public sealed class AbubuUiSettings : ScriptableObject
    {
        public const string ResourcePath = "AbubuUiSettings";

        [Tooltip("ポップアップが 1 つでも開いている間、IPauseService でポーズする")]
        public bool PauseWhileOpen;

        [Tooltip("ポップアップの背面を覆う色。背後のボタンを押せなくする (alpha 0 でも操作は遮断される)")]
        public Color BackdropColor = new(0f, 0f, 0f, 0.5f);

        [Tooltip("背面をクリックしたら一番上のポップアップを閉じる")]
        public bool CloseOnBackdropClick;

        [Tooltip("ポップアップ用 Canvas の Sorting Order")]
        public int SortingOrder = 100;

        [Tooltip("Canvas Scaler の基準解像度")]
        public Vector2 ReferenceResolution = new(1920f, 1080f);
    }

    /// <summary>
    /// ポップアップ (ダイアログ・設定画面など) をスタックで管理する。後から開いたものが手前に来て、Pop は一番手前から閉じる。
    /// <code>
    /// var dialog = _popups.Push(confirmDialogPrefab);
    /// _popups.Pop();          // 一番上を閉じる
    /// _popups.PopAll();
    /// </code>
    /// 背面を覆う Backdrop が自動で付き、下のポップアップや画面は操作できなくなる。
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
        }

        private readonly AbubuUiSettings _settings;
        private readonly IPauseService _pause;
        private readonly List<Entry> _stack = new();
        private readonly ReactiveProperty<int> _count = new(0);
        private Canvas _canvas;

        public ReadOnlyReactiveProperty<int> Count => _count;

        public PopupService([InjectOptional] IPauseService pause = null, [InjectOptional] AbubuUiSettings settings = null)
        {
            _pause = pause;
            _settings = settings != null ? settings : ScriptableObject.CreateInstance<AbubuUiSettings>();
        }

        public GameObject Push(GameObject prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            PurgeDestroyed();
            var canvas = EnsureCanvas();

            var backdrop = CreateBackdrop(canvas.transform);
            var popup = Object.Instantiate(prefab, canvas.transform);
            popup.name = prefab.name;
            _stack.Add(new Entry { Popup = popup, Backdrop = backdrop });
            UpdateState();

            foreach (var receiver in popup.GetComponents<IPopup>()) receiver.OnOpened();
            return popup;
        }

        public T Push<T>(T prefab) where T : Component => Push(prefab.gameObject).GetComponent<T>();

        public bool Pop()
        {
            PurgeDestroyed();
            if (_stack.Count == 0) return false;

            Close(_stack[^1]);
            _stack.RemoveAt(_stack.Count - 1);
            UpdateState();
            return true;
        }

        public void PopTo(GameObject popup)
        {
            PurgeDestroyed();
            var index = _stack.FindIndex(e => e.Popup == popup);
            if (index < 0) return;

            // 手前から順に閉じる
            while (_stack.Count > index) Pop();
        }

        public void PopAll()
        {
            while (Pop())
            {
            }
        }

        public void Dispose()
        {
            PopAll();
            _count.Dispose();
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
        }

        private static void Close(Entry entry)
        {
            if (entry.Popup != null)
            {
                foreach (var receiver in entry.Popup.GetComponents<IPopup>()) receiver.OnClosing();
                Object.Destroy(entry.Popup);
            }

            if (entry.Backdrop != null) Object.Destroy(entry.Backdrop);
        }

        /// <summary>外から Destroy されたポップアップを積み残さないよう、スタックから外す</summary>
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
            _count.Value = _stack.Count;
            if (!_settings.PauseWhileOpen || _pause == null) return;

            if (_stack.Count > 0) _pause.Pause(this);
            else _pause.Resume(this);
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;

            var go = new GameObject("[Abubu.UI]");
            Object.DontDestroyOnLoad(go);

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _settings.SortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _settings.ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return _canvas;
        }

        private GameObject CreateBackdrop(Transform parent)
        {
            var go = new GameObject("Backdrop", typeof(RectTransform));
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

            if (_settings.CloseOnBackdropClick)
            {
                var trigger = go.AddComponent<EventTrigger>();
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
            var settings = Resources.Load<AbubuUiSettings>(AbubuUiSettings.ResourcePath);
            container.BindInterfacesTo<PopupService>().AsSingle().WithArguments(settings);
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
        public static void PopAll() => Service?.PopAll();
        public static int Count => Service?.Count.CurrentValue ?? 0;
    }
}
