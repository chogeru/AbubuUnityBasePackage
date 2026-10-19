using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;
using Object = UnityEngine.Object;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Abubu.UI
{
    /// <summary>
    /// 画面に付けると、表示・非表示のタイミングで通知を受け取れる (任意)。
    /// </summary>
    public interface IScreen
    {
        /// <summary>一番手前になった直後 (開いたとき・上の画面から戻ってきたとき)</summary>
        void OnShown();

        /// <summary>一番手前でなくなる直前 (次の画面を開いたとき・閉じるとき)</summary>
        void OnHidden();
    }

    /// <summary>
    /// 全画面の UI (タイトル → メニュー → 設定 など) を、戻る操作ができる履歴として管理する。
    /// 表示されるのは一番手前の 1 画面だけで、<see cref="Back"/> で 1 つ前の画面に戻る。
    /// <code>
    /// _screens.Push(menuScreenPrefab);
    /// _screens.Push(settingsScreenPrefab);
    /// _screens.Back();          // メニューに戻る
    /// _screens.BackToRoot();
    /// </code>
    /// ダイアログのように手前に重ねて背面を残したいものは <see cref="IPopupService"/> を使う (ポップアップは画面より手前に出る)。
    /// プレハブは DI コンテナ経由で生成されるので、画面のコンポーネントで [Inject] が使える。
    /// </summary>
    public interface IScreenService
    {
        /// <summary>履歴に積まれている画面の数</summary>
        ReadOnlyReactiveProperty<int> Count { get; }

        /// <summary>一番手前の画面 (無ければ null)</summary>
        GameObject Current { get; }

        /// <summary>画面が切り替わったとき (開く・戻る・置き換え・閉じる)</summary>
        Observable<Unit> Changed { get; }

        /// <summary>画面を開いて一番手前に表示する。今の画面は非表示になり、Back で戻れる</summary>
        GameObject Push(GameObject prefab);

        /// <summary>コンポーネント型で指定する版。戻り値も同じ型で受け取れる</summary>
        T Push<T>(T prefab) where T : Component;

        /// <summary>今の画面を閉じて別の画面に置き換える (履歴は増えない)</summary>
        GameObject Replace(GameObject prefab);

        /// <summary>今の画面を閉じて 1 つ前に戻る。最初の画面 (戻り先が無い) なら何もせず false</summary>
        bool Back();

        /// <summary>指定した画面まで戻る (その上に積まれた画面をすべて閉じる)</summary>
        void BackTo(GameObject screen);

        /// <summary>最初の画面まで戻る</summary>
        void BackToRoot();

        /// <summary>最初の画面も含めてすべて閉じる</summary>
        void Clear();
    }

    public sealed class ScreenService : IScreenService, IDisposable
    {
        private sealed class Entry
        {
            public GameObject Prefab;
            /// <summary>前の画面を残さない設定で非表示になっている間は null (戻るときに Prefab から作り直す)</summary>
            public GameObject Instance;
            /// <summary>開いたときのアクティブなシーン。そのシーンが閉じたら画面も閉じる (CloseOnSceneChange)</summary>
            public UnityScene Scene;
            /// <summary>OnShown を通知済みで、まだ OnHidden を通知していない</summary>
            public bool Visible;
        }

        private readonly AbubuUiSettings _settings;
        private readonly IUiFactory _factory;
        private readonly IPopupService _popups;
        private readonly UiEventSystemProvider _eventSystems;
        private readonly IUiInputModeSource _input;
        private readonly Subject<Unit> _changed = new();
        private readonly UiTransitionRunner _transitions = new();
        private readonly List<Entry> _stack = new();
        private readonly ReactiveProperty<int> _count = new(0);
        private Canvas _canvas;
        private bool _disposed;
        private bool _disposing;

        public ReadOnlyReactiveProperty<int> Count => _count;
        public Observable<Unit> Changed => _changed;
        public GameObject Current => _stack.Count > 0 ? _stack[^1].Instance : null;

        /// <param name="factory">インスタンスの生成方法。null なら注入なしで Instantiate する (テスト用)</param>
        /// <param name="popups">開いているポップアップから選択を奪わないために使う (任意)</param>
        public ScreenService([InjectOptional] AbubuUiSettings settings = null, [InjectOptional] IUiFactory factory = null,
            [InjectOptional] IPopupService popups = null, [InjectOptional] UiEventSystemProvider eventSystems = null,
            [InjectOptional] IUiInputModeSource input = null)
        {
            _settings = settings != null ? settings : AbubuUiSettings.Default;
            _factory = factory ?? new ContainerUiFactory();
            _popups = popups;
            _eventSystems = eventSystems;
            _input = input;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        // Canvas は DontDestroyOnLoad なので、放っておくと前のシーンの画面が次のシーンに残る。
        // 加算ロードでのアクティブシーン切り替えでは閉じず、開いたときのシーンが閉じたときだけ閉じる
        private void OnSceneUnloaded(UnityScene scene)
        {
            if (!_settings.CloseOnSceneChange || !_stack.Exists(e => e.Scene == scene)) return;

            var topRemoved = _stack[^1].Scene == scene;
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Scene != scene) continue;
                var entry = _stack[i];
                _stack.RemoveAt(i);
                Hide(entry, keep: false);
            }

            if (topRemoved && _stack.Count > 0) Show(_stack[^1]);
            _count.Value = _stack.Count;
        }

        public GameObject Push(GameObject prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            PurgeDestroyed();
            if (_stack.Count > 0) Hide(_stack[^1], _settings.KeepPreviousScreens);

            return Add(prefab);
        }

        public T Push<T>(T prefab) where T : Component
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            return Push(prefab.gameObject).GetComponent<T>();
        }

        private GameObject Add(GameObject prefab)
        {
            var entry = new Entry { Prefab = prefab, Scene = SceneManager.GetActiveScene() };
            _stack.Add(entry);
            try
            {
                Show(entry);
            }
            catch
            {
                // 生成に失敗した画面を履歴に残さない (前の画面は非表示のままなので表示し直す)
                _stack.Remove(entry);
                if (_stack.Count > 0) Show(_stack[^1]);
                _count.Value = _stack.Count;
                throw;
            }
            _count.Value = _stack.Count;
            return entry.Instance;
        }

        public GameObject Replace(GameObject prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            PurgeDestroyed();
            if (_stack.Count > 0)
            {
                Hide(_stack[^1], keep: false);
                _stack.RemoveAt(_stack.Count - 1);
            }

            return Add(prefab);
        }

        public bool Back()
        {
            PurgeDestroyed();
            if (_stack.Count <= 1) return false;

            CloseAbove(_stack.Count - 2);
            return true;
        }

        public void BackTo(GameObject screen)
        {
            PurgeDestroyed();
            var index = _stack.FindIndex(e => e.Instance == screen && screen != null);
            if (index >= 0) CloseAbove(index);
        }

        public void BackToRoot()
        {
            PurgeDestroyed();
            if (_stack.Count > 1) CloseAbove(0);
        }

        public void Clear()
        {
            if (_stack.Count == 0) return;
            // 先に履歴を空にしてから閉じる (OnHidden の中で Push / Back されても状態が崩れないように)
            var entries = _stack.ToArray();
            _stack.Clear();
            _count.Value = 0;
            _changed.OnNext(Unit.Default);
            for (var i = entries.Length - 1; i >= 0; i--) Hide(entries[i], keep: false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            // 終了時はアニメーションを待たずに片付ける
            _disposing = true;
            _transitions.CancelAll();
            Clear();
            _disposed = true;
            _count.Dispose();
            _changed.Dispose();
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
        }

        /// <summary>index より上の画面を手前から閉じ、index の画面を表示する</summary>
        /// <remarks>先に履歴から外してから閉じる。OnHidden の中で Back などが呼ばれても同じ画面を二重に閉じない</remarks>
        private void CloseAbove(int index)
        {
            var closing = _stack.GetRange(index + 1, _stack.Count - index - 1);
            _stack.RemoveRange(index + 1, closing.Count);
            var target = _stack[index];
            _count.Value = _stack.Count;

            for (var i = closing.Count - 1; i >= 0; i--) Hide(closing[i], keep: false);
            // OnHidden の中で別の画面が開かれていたら、そちらを優先する
            if (_stack.Count > 0 && _stack[^1] == target) Show(target);
        }

        private void Show(Entry entry)
        {
            if (entry.Instance == null)
            {
                var canvas = _canvas != null
                    ? _canvas
                    : _canvas = UiUtility.CreateCanvas(_settings.ScreenCanvasPrefab, "[Abubu.Screens]", _settings.ScreenSortingOrder, _settings);
                _eventSystems?.Ensure();
                var instance = _factory.Create(entry.Prefab, UiUtility.ContentRoot(canvas));
                if (instance == null) throw new InvalidOperationException($"IUiFactory が {entry.Prefab.name} のインスタンスを返しませんでした。");
                instance.name = entry.Prefab.name;
                UiDestroyNotifier.Attach(instance, OnScreenDestroyed);
                entry.Instance = instance;
            }

            entry.Instance.SetActive(true);
            if (!entry.Visible)
            {
                entry.Visible = true;
                foreach (var receiver in entry.Instance.GetComponents<IScreen>())
                {
                    try
                    {
                        receiver.OnShown();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, entry.Instance);
                    }
                }
            }

            // ポップアップが開いている間は、ポップアップの選択を奪わない
            if (_popups == null || _popups.Count.CurrentValue == 0) UiInitialSelection.ApplyOnOpen(entry.Instance, _settings.SelectOnOpen, _input);
            _changed.OnNext(Unit.Default);
            // 閉じるアニメーションの途中で戻ってきた場合は、それを取り消して開くアニメーションを再生する
            _transitions.Show(entry.Instance);
        }

        /// <summary>画面が外から Destroy されたとき。一番手前だったら、その下の画面を表示し直す</summary>
        private void OnScreenDestroyed(GameObject screen)
        {
            if (_disposed) return;
            // 意図して閉じたものは Instance を null にしてあるので見つからない
            var index = _stack.FindIndex(e => ReferenceEquals(e.Instance, screen));
            if (index < 0) return;

            var wasTop = index == _stack.Count - 1;
            _stack.RemoveAt(index);
            if (wasTop && _stack.Count > 0) Show(_stack[^1]);
            _count.Value = _stack.Count;
            _changed.OnNext(Unit.Default);
        }

        /// <param name="keep">true なら非表示にして残す。false なら破棄する (閉じるアニメーションがあれば、終わってから)</param>
        private void Hide(Entry entry, bool keep)
        {
            if (entry.Instance == null) return;

            var instance = entry.Instance;
            if (!keep) entry.Instance = null;

            // 非表示中 (前の画面として残っているもの) は通知済み
            if (entry.Visible)
            {
                entry.Visible = false;
                foreach (var receiver in instance.GetComponents<IScreen>())
                {
                    try
                    {
                        receiver.OnHidden();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, instance);
                    }
                }
            }

            if (_disposing) Complete();
            else _transitions.Hide(instance, Complete);

            void Complete()
            {
                if (keep)
                {
                    if (instance != null) instance.SetActive(false);
                }
                else
                {
                    _factory.Release(instance);
                }
            }
        }

        /// <summary>
        /// 外から Destroy された画面を履歴から外す。一番手前が消えていたら、その下の画面を表示し直す。
        /// </summary>
        /// <remarks>
        /// 意図して破棄した画面は Instance に本当の null を入れているので、Unity の「破棄済み」(== null だが参照は残る) と区別できる。
        /// </remarks>
        private void PurgeDestroyed()
        {
            var topRemoved = false;
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                var instance = _stack[i].Instance;
                if (ReferenceEquals(instance, null) || instance != null) continue;
                if (i == _stack.Count - 1) topRemoved = true;
                _stack.RemoveAt(i);
            }

            if (_stack.Count != _count.Value)
            {
                if (topRemoved && _stack.Count > 0) Show(_stack[^1]);
                _count.Value = _stack.Count;
            }
        }
    }
}

namespace Abubu
{
    using Abubu.UI;

    /// <summary>
    /// 画面スタックの static ショートカット。
    /// <code>
    /// Screens.Push(settingsScreenPrefab);
    /// Screens.Back();
    /// </code>
    /// </summary>
    public static class Screens
    {
        /// <summary>サービス本体。取得できない場合は null (警告を型ごとに 1 回出す)</summary>
        public static IScreenService Service => AbubuServices.TryResolve<IScreenService>();

        /// <summary>ショートカット内部用。取得できない場合は null で、エラーを型ごとに 1 回出す</summary>
        private static IScreenService Svc => AbubuServices.ResolveOrLogError<IScreenService>();

        public static GameObject Push(GameObject prefab) => Svc?.Push(prefab);
        public static T Push<T>(T prefab) where T : Component => Svc?.Push(prefab);
        public static GameObject Replace(GameObject prefab) => Svc?.Replace(prefab);
        public static bool Back() => Svc?.Back() ?? false;
        public static void BackToRoot() => Svc?.BackToRoot();
        public static void Clear() => Svc?.Clear();
        public static GameObject Current => Svc?.Current;
        public static int Count => Svc?.Count.CurrentValue ?? 0;
    }

    /// <summary>
    /// 「戻る」とフォーカス (ゲームパッド・キーボードでの選択) の共通処理。
    /// <code>
    /// UiNavigation.Back();             // ポップアップがあれば閉じ、無ければ前の画面に戻る
    /// UiNavigation.EnsureSelection();  // 何も選択されていなければ、一番手前の UI の最初のボタンを選ぶ
    /// </code>
    /// </summary>
    public static class UiNavigation
    {
        /// <summary>最後に「戻る」が処理されたフレーム</summary>
        public static int LastBackFrame { get; private set; } = -1;

        /// <summary>
        /// このフレームですでに「戻る」が処理されたか。同じキー (Esc など) でポーズメニューを開く処理は、これが true なら開かない
        /// (ポップアップを閉じたのと同じフレームで開き直さないように)
        /// </summary>
        public static bool HandledBackThisFrame => LastBackFrame == UnityEngine.Time.frameCount;

        /// <summary>一番手前のポップアップを閉じる。ポップアップが無ければ前の画面に戻る。何もしなかったら false</summary>
        public static bool Back() => Back(AbubuServices.ResolveOrLogError<IPopupService>(), AbubuServices.ResolveOrLogError<IScreenService>());

        public static bool Back(IPopupService popups, IScreenService screens)
        {
            var handled = (popups != null && popups.Pop()) || (screens != null && screens.Back());
            if (handled) LastBackFrame = UnityEngine.Time.frameCount;
            return handled;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => LastBackFrame = -1;

        /// <summary>
        /// 選択が無い・一番手前の UI の外にある (マウスで背景をクリックした、ポップアップの裏のボタンが選ばれている など) ときに、
        /// 一番手前のポップアップ (無ければ画面) の最初の選択先を選び直す。ゲームパッドの操作を受けたときに呼ぶ
        /// </summary>
        /// <returns>選び直したら true</returns>
        public static bool EnsureSelection() =>
            EnsureSelection(AbubuServices.ResolveOrLogError<IPopupService>(), AbubuServices.ResolveOrLogError<IScreenService>());

        public static bool EnsureSelection(IPopupService popups, IScreenService screens)
        {
            var root = popups?.Current;
            if (root == null) root = screens?.Current;
            if (root == null || !root.activeInHierarchy) return false;

            var selected = UiInitialSelection.Current;
            if (selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(root.transform)) return false;

            var before = selected;
            UiInitialSelection.Apply(root, force: true);
            return UiInitialSelection.Current != before;
        }
    }
}
