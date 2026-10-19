using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Abubu.Debugging
{
    /// <summary>
    /// FPS・メモリ・Time.timeScale などを画面に重ねて表示し、デバッグ用のボタン (コマンド) も置けるデバッグ表示 (IMGUI)。
    /// <code>
    /// DebugOverlay.EnsureExists();                                                    // どこからでも生成 (シーンに置いてもよい)
    /// DebugOverlay.Register("score", () => _score.ToString(), owner: this);            // 値の表示。owner が破棄されたら自動で消える
    /// DebugOverlay.RegisterCommand("Add 100 coins", () => _coins += 100, this, "Cheat"); // ボタン (カテゴリ "Cheat" にまとめて表示)
    /// DebugOverlay.RegisterToggle("God mode", () => _god, v => _god = v, this, "Cheat"); // オン/オフ
    /// DebugOverlay.RegisterSlider("TimeScale", () => Time.timeScale, v => Time.timeScale = v, 0f, 2f, this, "Time"); // スライダー
    /// var handle = DebugOverlay.RegisterCommand("Reset", Reset);
    /// handle.Dispose();                                                               // 個別に消す
    /// </code>
    /// 画面の隅の小さなボタン・キー (既定 F1)・ゲームパッド (既定 L3+R3 長押し) で詳細の表示/非表示を切り替える。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>他のモジュールに依存しない。自動では生成されない (GameObject > Abubu > Debug Overlay でシーンに置くか、<see cref="EnsureExists"/> を呼ぶ)。
    /// Register* はオーバーレイが無いときに呼んでもよい (生成されたら表示される)。</item>
    /// <item>表示内容は refreshInterval ごとに作り直し、OnGUI では作り直さない (毎フレームの GC を出さない)。
    /// 登録した getter もこの間隔でしか呼ばれない。</item>
    /// <item>エディタ・開発ビルド以外では何も表示しない。リリースビルドでも使う場合は allowInReleaseBuild をオンにするか、
    /// Scripting Define Symbols に ABUBU_DEBUG_OVERLAY_IN_RELEASE を追加する。</item>
    /// <item>キー切り替えは旧 Input Manager / 新 Input System のどちらでも動く。
    /// 新 Input System では、同じ名前のキーが無い KeyCode を指定した場合は F1 になる。KeyCode.None でキー切り替えなし。</item>
    /// <item>ゲームパッド操作は新 Input System (com.unity.inputsystem) がある場合だけ動く。gamepadToggle の操作で表示すると
    /// ゲームパッドで操作できる状態 (<see cref="HasGamepadFocus"/>) になり、十字キー上下で選択、A (South) で実行、
    /// 十字キー左右でトグル / スライダーを変更、B (East) で操作をやめる。切り替え操作をしたゲームパッドの入力だけを読む。
    /// 操作中は EventSystem.sendNavigationEvents をオフにして uGUI に方向・決定・キャンセルが届かないようにし、
    /// <see cref="GamepadFocusChanged"/> を通知する (Abubu.Inputs の InputService はこれを見てゲーム操作の ActionMap を無効にする)。
    /// Abubu.Inputs を使わない場合は、ゲーム側で入力を無視すること (<c>if (DebugOverlay.HasGamepadFocus) return;</c>)。</item>
    /// <item>ログ末尾の表示 (showLogTail) は既定でオフ。Application.logMessageReceived (メインスレッドのログ) の直近 logTailCount 件を、
    /// logTailFilter で選んだ種類だけ表示する (フィルタは受け取った時点で適用する)。</item>
    /// <item>IMGUI のボタンは uGUI のクリックを遮らない。ボタンの裏に uGUI がある場合は <see cref="IsPointerOver"/> で判定するか、
    /// anchor を変えること。</item>
    /// </list>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DebugOverlay : MonoBehaviour
    {
        /// <summary>表示する画面の隅</summary>
        public enum Corner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
        }

        /// <summary>ゲームパッドで表示を切り替える操作</summary>
        public enum GamepadToggle
        {
            /// <summary>ゲームパッドでは切り替えない</summary>
            None,
            /// <summary>左右のスティック押し込み (L3 + R3) を同時に押す</summary>
            BothSticks,
            /// <summary>Select (View / Share / -) ボタン</summary>
            Select,
            /// <summary>Select と Start を同時に押す</summary>
            SelectAndStart,
        }

        /// <summary>ログ末尾に表示するログの種類</summary>
        [Flags]
        public enum LogFilter
        {
            None = 0,
            Log = 1 << 0,
            Warning = 1 << 1,
            /// <summary>Error / Exception / Assert</summary>
            Error = 1 << 2,
            All = Log | Warning | Error,
        }

        private sealed class Entry
        {
            public Func<string> Getter;
            public Object Owner;
            public bool HasOwner;

            /// <summary>owner を指定していて、それが破棄されたか</summary>
            public bool IsOwnerDestroyed => HasOwner && Owner == null;
        }

        private enum ControlKind
        {
            Command,
            Toggle,
            Slider,
        }

        /// <summary>RegisterCommand / RegisterToggle / RegisterSlider で登録された操作</summary>
        private sealed class Control
        {
            public ControlKind Kind;
            public string Category;
            public string Label;
            public Action Action;
            public Func<bool> GetBool;
            public Action<bool> SetBool;
            public Func<float> GetFloat;
            public Action<float> SetFloat;
            public float Min;
            public float Max;
            public float Step;
            public Object Owner;
            public bool HasOwner;

            /// <summary>描画用に保持している値と文字 (refreshInterval ごとと操作時にだけ更新する)</summary>
            public readonly GUIContent Content = new();
            public bool CachedBool;
            public float CachedFloat = float.NaN;
            public bool HasCache;

            public bool IsOwnerDestroyed => HasOwner && Owner == null;
        }

        /// <summary>登録の戻り値。Dispose すると (同じ名前で置き換えられていなければ) 消す</summary>
        private sealed class Registration : IDisposable
        {
            private Action<object> _remove;
            private object _target;

            public Registration(Action<object> remove, object target)
            {
                _remove = remove;
                _target = target;
            }

            public void Dispose()
            {
                if (_target == null) return;
                _remove(_target);
                _remove = null;
                _target = null;
            }
        }

        /// <summary>パネルの 1 行 (カテゴリ見出し または 操作)</summary>
        private struct Row
        {
            public GUIContent Header;
            public Control Control;
        }

        private const string DefaultCategory = "Commands";
        private const int MaxLogLineLength = 160;

        private static readonly Dictionary<string, Entry> Items = new();
        private static readonly List<string> RemoveBuffer = new();
        /// <summary>カテゴリごとにまとめた順 (カテゴリは最初に登録された順、カテゴリ内は登録順)</summary>
        private static readonly List<Control> Controls = new();
        private static readonly Action<object> RemoveEntry = target =>
        {
            var entry = (Entry)target;
            foreach (var item in Items)
            {
                if (!ReferenceEquals(item.Value, entry)) continue;
                Items.Remove(item.Key);
                return;
            }
        };
        private static readonly Action<object> RemoveControl = target =>
        {
            if (Controls.Remove((Control)target)) _controlsVersion++;
        };
        private static int _controlsVersion;
        private static DebugOverlay _instance;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ABUBU_DEBUG_OVERLAY_IN_RELEASE
        private static readonly bool AllowedByBuild = true;
#else
        private static readonly bool AllowedByBuild = false;
#endif

        [Tooltip("リリースビルド (開発ビルド以外) でも表示する。既定はオフ")]
        [SerializeField] private bool allowInReleaseBuild = false;

        [Tooltip("起動時から詳細を表示する")]
        [SerializeField] private bool startVisible = true;

        [Tooltip("詳細の表示/非表示を切り替えるキー。None ならキーでは切り替えない")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        [Header("ゲームパッド (Input System がある場合のみ)")]
        [Tooltip("表示を切り替えるボタンの組み合わせ。None ならゲームパッドでは切り替えない")]
        [SerializeField] private GamepadToggle gamepadToggle = GamepadToggle.BothSticks;

        [Tooltip("gamepadToggle を押し続ける秒数 (誤操作防止)。0 なら押した瞬間に切り替える")]
        [SerializeField, Min(0f)] private float gamepadHoldTime = 0.5f;

        [Header("表示")]
        [Tooltip("表示する画面の隅")]
        [SerializeField] private Corner anchor = Corner.TopLeft;

        [Tooltip("切り替えボタンを表示する")]
        [SerializeField] private bool showToggleButton = true;

        [Tooltip("切り替えボタンの文字")]
        [SerializeField] private string toggleLabel = "Dbg";

        [Tooltip("文字の色")]
        [SerializeField] private Color textColor = Color.white;

        [Tooltip("背景の色")]
        [SerializeField] private Color backgroundColor = new(0f, 0f, 0f, 0.6f);

        [Tooltip("ゲームパッドで選択中の行の色")]
        [SerializeField] private Color selectionColor = new(1f, 0.8f, 0.2f, 0.45f);

        [Tooltip("フォントサイズ = セーフエリアの高さ × この値")]
        [SerializeField, Range(0.005f, 0.1f)] private float fontScale = 1f / 45f;

        [Tooltip("フォントサイズの下限 / 上限 (px)")]
        [SerializeField] private Vector2Int fontSizeRange = new(12, 40);

        [Tooltip("Screen.safeArea (ノッチ・角丸) の内側に表示する")]
        [SerializeField] private bool respectSafeArea = true;

        [Header("ログ")]
        [Tooltip("直近のログを表示する (既定はオフ)")]
        [SerializeField] private bool showLogTail = false;

        [Tooltip("表示するログの件数")]
        [SerializeField, Range(1, 50)] private int logTailCount = 8;

        [Tooltip("表示するログの種類 (受け取った時点で絞り込む)")]
        [SerializeField] private LogFilter logTailFilter = LogFilter.All;

        [Header("更新")]
        [Tooltip("表示内容を作り直す間隔 (秒)。登録した getter もこの間隔で呼ばれる")]
        [SerializeField, Min(0.02f)] private float refreshInterval = 0.25f;

        [Tooltip("FPS の平均を取る時間 (秒)")]
        [SerializeField, Min(0.1f)] private float fpsInterval = 0.5f;

        private readonly StringBuilder _builder = new();
        private readonly GUIContent _content = new();
        private readonly GUIContent _buttonContent = new();
        private readonly List<Row> _rows = new();
        private readonly List<int> _selectableRows = new();
        private readonly Dictionary<string, GUIContent> _headers = new();
        private int _rowsVersion = -1;
        private float _elapsed;
        private int _frames;
        private float _fps;
        private float _frameMs;
        private float _refreshElapsed;
        private bool _textDirty = true;
        private bool _visible;
        private bool _gamepadFocus;
        /// <summary>最後に通知した HasGamepadFocus</summary>
        private bool _focusNotified;
        /// <summary>操作中に sendNavigationEvents をオフにした EventSystem (戻す用)</summary>
        private EventSystem _navigationSuppressed;
        private int _selected;
        private float _comboHeld;
        private bool _comboFired;
        private GUIStyle _label;
        private GUIStyle _button;
        private GUIStyle _commandButton;
        private int _styleFontSize = -1;
        private Color _styleTextColor;
        private Vector2 _textSize;
        private float _controlsWidth;
        private float _rowHeight;
        private Rect _buttonRect;
        private Rect _panelRect;

        // ログ末尾 (リングバッファ)
        private string[] _logMessages;
        private LogType[] _logTypes;
        private int _logStart;
        private int _logCount;
        private bool _logSubscribed;

#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM && ABUBU_INPUTSYSTEM
        private UnityEngine.InputSystem.Key _toggleKey = UnityEngine.InputSystem.Key.F1;
#endif

        /// <summary>詳細を表示しているか</summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (!value) _gamepadFocus = false;
                UpdateFocusState();
                // 表示したら次の Update ですぐ作り直す
                _refreshElapsed = refreshInterval;
            }
        }

        /// <summary>表示する画面の隅</summary>
        public Corner Anchor
        {
            get => anchor;
            set => anchor = value;
        }

        /// <summary>直近のログを表示するか (既定はオフ)</summary>
        public bool ShowLogTail
        {
            get => showLogTail;
            set
            {
                showLogTail = value;
                UpdateLogSubscription();
                _refreshElapsed = refreshInterval;
            }
        }

        /// <summary>表示するログの件数 (1〜50)。変えると溜まっていたログは消える</summary>
        public int LogTailCount
        {
            get => logTailCount;
            set
            {
                logTailCount = Mathf.Clamp(value, 1, 50);
                _logMessages = null; // 次のログで作り直す
                _logCount = 0;
                _logStart = 0;
            }
        }

        /// <summary>表示するログの種類 (受け取った時点で絞り込む)</summary>
        public LogFilter LogTailFilter
        {
            get => logTailFilter;
            set => logTailFilter = value;
        }

        /// <summary>
        /// ゲームパッドでオーバーレイを操作中か。true の間はゲーム側でゲームパッド入力を無視すること
        /// (<c>if (DebugOverlay.HasGamepadFocus) return;</c>)
        /// </summary>
        public static bool HasGamepadFocus => _instance != null && _instance.isActiveAndEnabled && _instance._visible && _instance._gamepadFocus;

        /// <summary>
        /// <see cref="HasGamepadFocus"/> が変わったときに呼ばれる (引数は新しい値)。
        /// 入力モジュールなど、操作中にゲームへの入力を止めたい側が購読する
        /// <code>
        /// DebugOverlay.GamepadFocusChanged += focused => player.enabled = !focused;
        /// </code>
        /// </summary>
        public static event Action<bool> GamepadFocusChanged;

        /// <summary>
        /// シーンに無ければ DontDestroyOnLoad の DebugOverlay を作る。
        /// エディタ・開発ビルド以外 (ABUBU_DEBUG_OVERLAY_IN_RELEASE も無い) では何もせず null を返す
        /// </summary>
        public static DebugOverlay EnsureExists()
        {
            if (!AllowedByBuild) return null;
            if (_instance != null) return _instance;

            var existing = FindFirstObjectByType<DebugOverlay>();
            if (existing != null) return existing;

            var go = new GameObject("[Abubu.DebugOverlay]");
            DontDestroyOnLoad(go);
            return go.AddComponent<DebugOverlay>();
        }

        // ---- 値の表示 ----

        /// <summary>
        /// 表示項目を追加する。同じ名前で呼ぶと置き換わる。
        /// owner を渡すと、それが破棄されたときに自動で消える (シーンをまたいで破棄済みオブジェクトを参照し続けない)。
        /// 戻り値を Dispose すると消える
        /// </summary>
        public static IDisposable Register(string label, Func<string> getter, Object owner = null)
        {
            if (string.IsNullOrEmpty(label)) throw new ArgumentException("label が空です", nameof(label));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            RemoveDestroyedOwners();
            var entry = new Entry { Getter = getter, Owner = owner, HasOwner = !ReferenceEquals(owner, null) };
            Items[label] = entry;
            return new Registration(RemoveEntry, entry);
        }

        public static void Unregister(string label)
        {
            if (!string.IsNullOrEmpty(label)) Items.Remove(label);
        }

        // ---- 操作 (コマンド・トグル・スライダー) ----

        /// <summary>
        /// ボタンを追加する。押すと action が呼ばれる (例外はログに出して続行)。
        /// 同じ category と label で呼ぶと置き換わる。owner・戻り値の扱いは <see cref="Register"/> と同じ
        /// </summary>
        /// <param name="label">ボタンの文字</param>
        /// <param name="action">押したときの処理</param>
        /// <param name="owner">これが破棄されたら自動で消える (任意)</param>
        /// <param name="category">まとめて表示するカテゴリ名。省略時は "Commands"</param>
        public static IDisposable RegisterCommand(string label, Action action, Object owner = null, string category = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return AddControl(new Control { Kind = ControlKind.Command, Label = label, Action = action }, owner, category);
        }

        /// <summary>
        /// オン/オフを切り替えるボタンを追加する。表示は getter の値 ([x] / [ ])、押すと setter(!getter()) が呼ばれる
        /// </summary>
        public static IDisposable RegisterToggle(string label, Func<bool> getter, Action<bool> setter, Object owner = null, string category = null)
        {
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            return AddControl(new Control { Kind = ControlKind.Toggle, Label = label, GetBool = getter, SetBool = setter }, owner, category);
        }

        /// <summary>
        /// min〜max の値を変えるスライダーを追加する。ゲームパッドでは十字キー左右で step ずつ変わる
        /// </summary>
        /// <param name="step">ゲームパッド操作 1 回で変わる量。0 以下なら (max - min) / 10</param>
        public static IDisposable RegisterSlider(string label, Func<float> getter, Action<float> setter, float min, float max,
            Object owner = null, string category = null, float step = 0f)
        {
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            if (!(max > min)) throw new ArgumentException("max は min より大きくしてください", nameof(max));
            return AddControl(new Control
            {
                Kind = ControlKind.Slider,
                Label = label,
                GetFloat = getter,
                SetFloat = setter,
                Min = min,
                Max = max,
                Step = step > 0f ? step : (max - min) / 10f,
            }, owner, category);
        }

        /// <summary>RegisterCommand / RegisterToggle / RegisterSlider で追加したものを消す</summary>
        public static void UnregisterCommand(string label, string category = null)
        {
            var index = IndexOfControl(NormalizeCategory(category), label);
            if (index < 0) return;
            Controls.RemoveAt(index);
            _controlsVersion++;
        }

        private static IDisposable AddControl(Control control, Object owner, string category)
        {
            if (string.IsNullOrEmpty(control.Label)) throw new ArgumentException("label が空です", "label");

            RemoveDestroyedOwners();
            control.Category = NormalizeCategory(category);
            control.Owner = owner;
            control.HasOwner = !ReferenceEquals(owner, null);

            var existing = IndexOfControl(control.Category, control.Label);
            if (existing >= 0)
            {
                Controls[existing] = control; // 置き換え (並び順は保つ)
            }
            else
            {
                // 同じカテゴリの最後の後ろに入れる (カテゴリごとにまとめて表示するため)
                var insertAt = Controls.Count;
                for (var i = Controls.Count - 1; i >= 0; i--)
                {
                    if (Controls[i].Category != control.Category) continue;
                    insertAt = i + 1;
                    break;
                }
                Controls.Insert(insertAt, control);
            }
            _controlsVersion++;
            return new Registration(RemoveControl, control);
        }

        private static string NormalizeCategory(string category) => string.IsNullOrEmpty(category) ? DefaultCategory : category;

        private static int IndexOfControl(string category, string label)
        {
            for (var i = 0; i < Controls.Count; i++)
            {
                if (Controls[i].Category == category && Controls[i].Label == label) return i;
            }
            return -1;
        }

        /// <summary>画面座標 (左下原点、Input の座標系) が表示中のボタン / パネルに重なっているか。uGUI 側のクリック判定の除外に使う</summary>
        public static bool IsPointerOver(Vector2 screenPosition)
        {
            if (_instance == null || !_instance.isActiveAndEnabled) return false;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return (_instance.showToggleButton && _instance._buttonRect.Contains(guiPoint))
                || (_instance._visible && _instance._panelRect.Contains(guiPoint));
        }

        /// <summary>詳細の表示/非表示を切り替える</summary>
        public void Toggle() => Visible = !_visible;

        // Domain Reload 無効時に、前回の再生の項目や参照が残らないようにする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Items.Clear();
            RemoveBuffer.Clear();
            Controls.Clear();
            _controlsVersion++;
            _instance = null;
        }

        private static void RemoveDestroyedOwners()
        {
            foreach (var item in Items)
            {
                if (item.Value.IsOwnerDestroyed) RemoveBuffer.Add(item.Key);
            }
            foreach (var key in RemoveBuffer) Items.Remove(key);
            RemoveBuffer.Clear();

            if (Controls.RemoveAll(static c => c.IsOwnerDestroyed) > 0) _controlsVersion++;
        }

        // ---- テスト用 (Abubu.Tests.Runtime からのみ使う) ----

        /// <summary>登録済みの操作を表示順に "カテゴリ/ラベル" で返す</summary>
        internal static List<string> GetControlKeysForTest()
        {
            RemoveDestroyedOwners();
            return Controls.ConvertAll(static c => c.Category + "/" + c.Label);
        }

        /// <summary>表示中の文字列 (統計・登録値・ログ)</summary>
        internal string TextForTest => _content.text;

        /// <summary>すぐに表示内容を作り直す</summary>
        internal void RefreshForTest()
        {
            RemoveDestroyedOwners();
            RebuildRowsIfNeeded();
            RebuildText();
        }

        /// <summary>ゲームパッドで選択中の操作 ("カテゴリ/ラベル")。無ければ null</summary>
        internal string SelectedKeyForTest
        {
            get
            {
                var control = SelectedControl;
                return control == null ? null : control.Category + "/" + control.Label;
            }
        }

        /// <summary>選択中の操作のボタン文字 (トグル・スライダーの値の確認用)</summary>
        internal string SelectedTextForTest => SelectedControl?.Content.text;

        // ---- ゲームパッド操作 (入力の読み取りとは分けてあり、テストからも呼べる) ----

        /// <summary>
        /// ゲームパッドの切り替え操作。非表示なら表示して操作できる状態に、表示中 (操作中でない) なら操作できる状態に、操作中なら閉じる
        /// </summary>
        internal void ToggleFromGamepad()
        {
            if (_visible && _gamepadFocus)
            {
                Visible = false;
                return;
            }
            Visible = true;
            _gamepadFocus = true;
            UpdateFocusState();
            RebuildRowsIfNeeded();
        }

        /// <summary>選択を delta 行動かす (端で反対側に回る)</summary>
        internal void MoveSelection(int delta)
        {
            RebuildRowsIfNeeded();
            var count = _selectableRows.Count;
            if (count == 0) return;
            _selected = ((_selected + delta) % count + count) % count;
        }

        /// <summary>選択中の操作を実行する (コマンドは実行、トグルは反転、スライダーは何もしない)</summary>
        internal void ActivateSelected()
        {
            var control = SelectedControl;
            if (control == null) return;
            switch (control.Kind)
            {
                case ControlKind.Command:
                    Invoke(control);
                    break;
                case ControlKind.Toggle:
                    SetToggle(control, !control.CachedBool);
                    break;
            }
        }

        /// <summary>選択中のトグル / スライダーを direction (+1 / -1) の向きに変える</summary>
        internal void AdjustSelected(int direction)
        {
            var control = SelectedControl;
            if (control == null || direction == 0) return;
            switch (control.Kind)
            {
                case ControlKind.Toggle:
                    SetToggle(control, direction > 0);
                    break;
                case ControlKind.Slider:
                    SetSlider(control, control.CachedFloat + control.Step * Math.Sign(direction));
                    break;
            }
        }

        /// <summary>ゲームパッドでの操作をやめる (表示はそのまま)</summary>
        internal void ReleaseGamepadFocus()
        {
            _gamepadFocus = false;
            UpdateFocusState();
        }

        /// <summary>HasGamepadFocus が変わっていたら、uGUI のナビゲーションを止める / 戻し、GamepadFocusChanged を通知する</summary>
        private void UpdateFocusState()
        {
            var focused = _visible && _gamepadFocus && isActiveAndEnabled;
            if (focused == _focusNotified) return;
            _focusNotified = focused;

            if (focused)
            {
                var eventSystem = EventSystem.current;
                if (eventSystem != null && eventSystem.sendNavigationEvents)
                {
                    eventSystem.sendNavigationEvents = false;
                    _navigationSuppressed = eventSystem;
                }
            }
            else
            {
                if (_navigationSuppressed != null) _navigationSuppressed.sendNavigationEvents = true;
                _navigationSuppressed = null;
#if ENABLE_INPUT_SYSTEM && ABUBU_INPUTSYSTEM
                _focusPad = null;
#endif
            }

            try
            {
                GamepadFocusChanged?.Invoke(focused);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private Control SelectedControl
        {
            get
            {
                RebuildRowsIfNeeded();
                if (_selectableRows.Count == 0) return null;
                _selected = Mathf.Clamp(_selected, 0, _selectableRows.Count - 1);
                return _rows[_selectableRows[_selected]].Control;
            }
        }

        private void Invoke(Control control)
        {
            try
            {
                control.Action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            // 実行結果 (登録値など) をすぐ反映する
            _refreshElapsed = refreshInterval;
        }

        private void SetToggle(Control control, bool value)
        {
            try
            {
                control.SetBool(value);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            UpdateCache(control);
            _refreshElapsed = refreshInterval;
        }

        private void SetSlider(Control control, float value)
        {
            try
            {
                control.SetFloat(Mathf.Clamp(value, control.Min, control.Max));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            UpdateCache(control);
            _refreshElapsed = refreshInterval;
        }

        /// <summary>getter から値を読み直し、変わっていたらボタンの文字を作り直す</summary>
        private void UpdateCache(Control control)
        {
            switch (control.Kind)
            {
                case ControlKind.Command:
                    if (!control.HasCache) control.Content.text = control.Label;
                    break;
                case ControlKind.Toggle:
                {
                    bool value;
                    try
                    {
                        value = control.GetBool();
                    }
                    catch (Exception)
                    {
                        value = false;
                    }
                    if (control.HasCache && value == control.CachedBool) break;
                    control.CachedBool = value;
                    control.Content.text = (value ? "[x] " : "[ ] ") + control.Label;
                    _textDirty = true;
                    break;
                }
                case ControlKind.Slider:
                {
                    float value;
                    try
                    {
                        value = control.GetFloat();
                    }
                    catch (Exception)
                    {
                        value = control.Min;
                    }
                    if (control.HasCache && value.Equals(control.CachedFloat)) break;
                    control.CachedFloat = value;
                    control.Content.text = control.Label + "  " + value.ToString("0.##");
                    _textDirty = true;
                    break;
                }
            }
            control.HasCache = true;
        }

        // ---- ライフサイクル ----

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            _visible = startVisible;
            // 再生中に何度も呼ばれる OnGUI の Layout イベントを省く (GUILayout は使っていない)
            useGUILayout = false;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM && ABUBU_INPUTSYSTEM
            // KeyCode と Key は名前が同じものが多い (F1〜F12、英字、Space、Tab など)。対応が無ければ F1 のまま
            if (Enum.TryParse<UnityEngine.InputSystem.Key>(toggleKey.ToString(), out var key) && key != UnityEngine.InputSystem.Key.None)
            {
                _toggleKey = key;
            }
#endif
            if (!AllowedByBuild && !allowInReleaseBuild) enabled = false;
        }

        private void OnEnable() => UpdateLogSubscription();

        private void OnDisable()
        {
            _gamepadFocus = false;
            UpdateFocusState();
            UpdateLogSubscription();
        }

        private void OnDestroy()
        {
            _gamepadFocus = false;
            UpdateFocusState();
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (toggleKey != KeyCode.None)
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                if (UnityEngine.Input.GetKeyDown(toggleKey)) Toggle();
#elif ENABLE_INPUT_SYSTEM && ABUBU_INPUTSYSTEM
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame) Toggle();
#endif
            }
#if ENABLE_INPUT_SYSTEM && ABUBU_INPUTSYSTEM
            UpdateGamepads();
#endif

            // timeScale が 0 でも測れるよう unscaledDeltaTime を使う
            var dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            _frames++;
            if (_elapsed >= fpsInterval)
            {
                _fps = _frames / _elapsed;
                _frameMs = _elapsed / _frames * 1000f;
                _elapsed = 0f;
                _frames = 0;
            }

            _refreshElapsed += dt;
            if (_refreshElapsed < refreshInterval) return;
            _refreshElapsed = 0f;
            RemoveDestroyedOwners();
            if (!_visible) return;
            RebuildRowsIfNeeded();
            RebuildText();
        }

#if ENABLE_INPUT_SYSTEM && ABUBU_INPUTSYSTEM
        /// <summary>切り替え操作をしたゲームパッド (操作中はこのゲームパッドだけを読む)</summary>
        private UnityEngine.InputSystem.Gamepad _focusPad;

        private void UpdateGamepads()
        {
            // 切り替え: どのゲームパッドでも、組み合わせを gamepadHoldTime 押し続けたら 1 回だけ切り替える (離すまで再発火しない)
            UnityEngine.InputSystem.Gamepad comboPad = null;
            var pads = UnityEngine.InputSystem.Gamepad.all;
            for (var i = 0; i < pads.Count; i++)
            {
                if (!IsComboPressed(pads[i])) continue;
                comboPad = pads[i];
                break;
            }

            if (comboPad == null)
            {
                _comboHeld = 0f;
                _comboFired = false;
            }
            else if (!_comboFired)
            {
                _comboHeld += Time.unscaledDeltaTime;
                if (_comboHeld >= gamepadHoldTime)
                {
                    _comboFired = true;
                    ToggleFromGamepad();
                    if (HasGamepadFocus) _focusPad = comboPad;
                    return; // 同じフレームの他のボタンは見ない
                }
            }

            if (!_visible || !_gamepadFocus) return;
            var pad = _focusPad != null && _focusPad.added ? _focusPad : UnityEngine.InputSystem.Gamepad.current;
            if (pad == null) return;
            if (pad.dpad.up.wasPressedThisFrame) MoveSelection(-1);
            if (pad.dpad.down.wasPressedThisFrame) MoveSelection(1);
            if (pad.dpad.left.wasPressedThisFrame) AdjustSelected(-1);
            if (pad.dpad.right.wasPressedThisFrame) AdjustSelected(1);
            if (pad.buttonSouth.wasPressedThisFrame) ActivateSelected();
            if (pad.buttonEast.wasPressedThisFrame) ReleaseGamepadFocus();
        }

        private bool IsComboPressed(UnityEngine.InputSystem.Gamepad pad) => gamepadToggle switch
        {
            GamepadToggle.BothSticks => pad.leftStickButton.isPressed && pad.rightStickButton.isPressed,
            GamepadToggle.Select => pad.selectButton.isPressed,
            GamepadToggle.SelectAndStart => pad.selectButton.isPressed && pad.startButton.isPressed,
            _ => false,
        };
#endif

        // ---- ログ末尾 ----

        private void UpdateLogSubscription()
        {
            var want = showLogTail && isActiveAndEnabled;
            if (want == _logSubscribed) return;
            _logSubscribed = want;
            if (want) Application.logMessageReceived += OnLogMessage;
            else Application.logMessageReceived -= OnLogMessage;
        }

        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if ((logTailFilter & ToFilter(type)) == 0) return;

            var capacity = Mathf.Clamp(logTailCount, 1, 50);
            if (_logMessages == null || _logMessages.Length != capacity)
            {
                _logMessages = new string[capacity];
                _logTypes = new LogType[capacity];
                _logStart = 0;
                _logCount = 0;
            }

            // 1 行目だけを、長すぎる場合は切り詰めて持つ (表示の幅を抑える)
            message ??= string.Empty;
            var newline = message.IndexOf('\n');
            var length = Mathf.Min(newline >= 0 ? newline : message.Length, MaxLogLineLength);
            if (length < message.Length) message = message.Substring(0, length);

            var index = (_logStart + _logCount) % capacity;
            if (_logCount == capacity) _logStart = (_logStart + 1) % capacity;
            else _logCount++;
            _logMessages[index] = message;
            _logTypes[index] = type;
        }

        private static LogFilter ToFilter(LogType type) => type switch
        {
            LogType.Log => LogFilter.Log,
            LogType.Warning => LogFilter.Warning,
            _ => LogFilter.Error,
        };

        // ---- 表示内容の作成 (refreshInterval ごと) ----

        /// <summary>登録済みの操作が変わっていたら行の並びを作り直す</summary>
        private void RebuildRowsIfNeeded()
        {
            if (_rowsVersion == _controlsVersion) return;
            _rowsVersion = _controlsVersion;

            var selected = _selectableRows.Count > 0 && _selected < _selectableRows.Count ? _rows[_selectableRows[_selected]].Control : null;
            _rows.Clear();
            _selectableRows.Clear();
            string category = null;
            foreach (var control in Controls)
            {
                if (control.Category != category)
                {
                    category = control.Category;
                    if (!_headers.TryGetValue(category, out var header))
                    {
                        header = new GUIContent("- " + category + " -");
                        _headers[category] = header;
                    }
                    _rows.Add(new Row { Header = header });
                }
                if (ReferenceEquals(control, selected)) _selected = _selectableRows.Count;
                _selectableRows.Add(_rows.Count);
                _rows.Add(new Row { Control = control });
                UpdateCache(control);
            }
            if (_selectableRows.Count == 0) _selected = 0;
            else _selected = Mathf.Clamp(_selected, 0, _selectableRows.Count - 1);
            _textDirty = true;
        }

        /// <summary>表示文字列を作り直す (refreshInterval ごと。OnGUI では呼ばない)</summary>
        private void RebuildText()
        {
            _builder.Clear();
            _builder.Append("FPS ").Append(_fps.ToString("F1")).Append("  (").Append(_frameMs.ToString("F1")).AppendLine(" ms)");
            _builder.Append("Scene ").AppendLine(SceneManager.GetActiveScene().name);
            _builder.Append("TimeScale ").AppendLine(Time.timeScale.ToString("0.##"));
            _builder.Append("Resolution ").Append(Screen.width).Append('x').Append(Screen.height).Append(" @").Append(Screen.currentResolution.refreshRateRatio.value.ToString("0")).AppendLine("Hz");
            _builder.Append("Mono ").Append(ToMb(Profiler.GetMonoUsedSizeLong())).Append(" / ").Append(ToMb(Profiler.GetMonoHeapSizeLong())).AppendLine(" MB");
            _builder.Append("Total ").Append(ToMb(Profiler.GetTotalAllocatedMemoryLong())).Append(" / ").Append(ToMb(Profiler.GetTotalReservedMemoryLong())).AppendLine(" MB");
            _builder.Append("GC ").Append(GC.CollectionCount(0));
            foreach (var item in Items)
            {
                string value;
                try
                {
                    value = item.Value.Getter();
                }
                catch (Exception e)
                {
                    value = "<error: " + e.Message + ">";
                }

                _builder.AppendLine().Append(item.Key).Append(' ').Append(value);
            }

            if (showLogTail && _logCount > 0)
            {
                _builder.AppendLine().Append("- Log -");
                for (var i = 0; i < _logCount; i++)
                {
                    var index = (_logStart + i) % _logMessages.Length;
                    _builder.AppendLine().Append(_logTypes[index] switch
                    {
                        LogType.Log => "[I] ",
                        LogType.Warning => "[W] ",
                        _ => "[E] ",
                    }).Append(_logMessages[index]);
                }
            }

            // トグル・スライダーの値を読み直す (変わったものだけ文字を作り直す)
            foreach (var row in _rows)
            {
                if (row.Control != null) UpdateCache(row.Control);
            }

            _content.text = _builder.ToString();
            _textDirty = true;
        }

        // ---- 描画 ----

        private void OnGUI()
        {
            var e = Event.current.type;
            // 描画とクリック・ドラッグ判定に必要なイベントだけ処理する
            if (e != EventType.Repaint && e != EventType.MouseDown && e != EventType.MouseUp && e != EventType.MouseDrag &&
                e != EventType.TouchDown && e != EventType.TouchUp && e != EventType.TouchMove) return;

            // 登録・解除された操作を (破棄済みの操作を押せないよう) すぐ反映する。変わっていなければ数値の比較だけ
            if (_visible) RebuildRowsIfNeeded();

            var area = GetGuiArea();
            if (EnsureStyles(area.height) || _textDirty)
            {
                if (_visible && string.IsNullOrEmpty(_content.text))
                {
                    RebuildRowsIfNeeded();
                    RebuildText();
                }
                MeasureContents();
                _textDirty = false;
            }

            var margin = _styleFontSize * 0.5f;
            var buttonSize = showToggleButton ? new Vector2(_styleFontSize * 3f, _styleFontSize * 1.6f) : Vector2.zero;
            var controlsHeight = _rows.Count * (_rowHeight + margin * 0.25f);
            var contentWidth = Mathf.Max(_textSize.x, _controlsWidth);
            var panelSize = new Vector2(contentWidth + margin * 2f, _textSize.y + margin + (controlsHeight > 0f ? controlsHeight + margin * 0.5f : 0f));
            var right = anchor is Corner.TopRight or Corner.BottomRight;
            var bottom = anchor is Corner.BottomLeft or Corner.BottomRight;

            // ボタンを隅に置き、パネルはその内側 (上なら下、下なら上) に置く
            var buttonX = right ? area.xMax - margin - buttonSize.x : area.x + margin;
            var buttonY = bottom ? area.yMax - margin - buttonSize.y : area.y + margin;
            _buttonRect = new Rect(buttonX, buttonY, buttonSize.x, buttonSize.y);

            var gap = showToggleButton ? buttonSize.y + margin : 0f;
            var panelX = right ? area.xMax - margin - panelSize.x : area.x + margin;
            var panelY = bottom ? area.yMax - margin - gap - panelSize.y : area.y + margin + gap;
            _panelRect = new Rect(panelX, panelY, panelSize.x, panelSize.y);

            if (showToggleButton && GUI.Button(_buttonRect, _buttonContent, _button)) Toggle();
            if (!_visible) return;

            var x = _panelRect.x + margin;
            var y = _panelRect.y + margin * 0.5f;
            if (e == EventType.Repaint)
            {
                var previous = GUI.color;
                GUI.color = backgroundColor;
                GUI.DrawTexture(_panelRect, Texture2D.whiteTexture);
                GUI.color = previous;
                _label.Draw(new Rect(x, y, _textSize.x, _textSize.y), _content, false, false, false, false);
            }
            if (_rows.Count == 0) return;

            y += _textSize.y + margin * 0.5f;
            var selectedRow = _gamepadFocus && _selectableRows.Count > 0 ? _selectableRows[Mathf.Clamp(_selected, 0, _selectableRows.Count - 1)] : -1;
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var rect = new Rect(x, y, contentWidth, _rowHeight);
                y += _rowHeight + margin * 0.25f;

                if (row.Header != null)
                {
                    if (e == EventType.Repaint) _label.Draw(rect, row.Header, false, false, false, false);
                    continue;
                }

                var control = row.Control;
                switch (control.Kind)
                {
                    case ControlKind.Command:
                        if (GUI.Button(rect, control.Content, _commandButton)) Invoke(control);
                        break;
                    case ControlKind.Toggle:
                        if (GUI.Button(rect, control.Content, _commandButton)) SetToggle(control, !control.CachedBool);
                        break;
                    case ControlKind.Slider:
                    {
                        var labelWidth = rect.width * 0.5f;
                        if (e == EventType.Repaint) _label.Draw(new Rect(rect.x, rect.y, labelWidth, rect.height), control.Content, false, false, false, false);
                        var current = float.IsNaN(control.CachedFloat) ? control.Min : control.CachedFloat;
                        var value = GUI.HorizontalSlider(new Rect(rect.x + labelWidth, rect.y + rect.height * 0.3f, rect.width - labelWidth, rect.height * 0.4f),
                            current, control.Min, control.Max);
                        if (!Mathf.Approximately(value, current)) SetSlider(control, value);
                        break;
                    }
                }

                // ゲームパッドで選択中の行は上から半透明の色を重ねる
                if (i == selectedRow && e == EventType.Repaint)
                {
                    var previous = GUI.color;
                    GUI.color = selectionColor;
                    GUI.DrawTexture(new Rect(rect.x - margin * 0.5f, rect.y, rect.width + margin, rect.height), Texture2D.whiteTexture);
                    GUI.color = previous;
                }
            }
        }

        /// <summary>文字と操作の大きさを測り直す (内容が変わったときだけ)</summary>
        private void MeasureContents()
        {
            _textSize = _label.CalcSize(_content);
            _rowHeight = _styleFontSize * 1.6f;
            _controlsWidth = 0f;
            foreach (var row in _rows)
            {
                float width;
                if (row.Header != null) width = _label.CalcSize(row.Header).x;
                else if (row.Control.Kind == ControlKind.Slider) width = _label.CalcSize(row.Control.Content).x * 2f; // ラベル + 同じ幅のスライダー
                else width = _commandButton.CalcSize(row.Control.Content).x;
                if (width > _controlsWidth) _controlsWidth = width;
            }
            if (_rows.Count > 0) _controlsWidth = Mathf.Max(_controlsWidth, _styleFontSize * 10f);
        }

        /// <summary>IMGUI 座標 (左上原点) での表示可能領域</summary>
        private Rect GetGuiArea()
        {
            if (!respectSafeArea) return new Rect(0f, 0f, Screen.width, Screen.height);
            var safe = Screen.safeArea;
            return new Rect(safe.x, Screen.height - safe.yMax, safe.width, safe.height);
        }

        private static string ToMb(long bytes) => (bytes / (1024f * 1024f)).ToString("F1");

        /// <summary>画面の高さに応じてフォントサイズを決める (高解像度でも読めるように)。作り直したら true</summary>
        private bool EnsureStyles(float areaHeight)
        {
            var min = Mathf.Max(1, fontSizeRange.x);
            var max = Mathf.Max(min, fontSizeRange.y);
            var fontSize = Mathf.Clamp(Mathf.RoundToInt(areaHeight * fontScale), min, max);
            if (_label != null && fontSize == _styleFontSize && _styleTextColor == textColor) return false;

            _styleFontSize = fontSize;
            _styleTextColor = textColor;
            _label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, normal = { textColor = textColor }, richText = false, wordWrap = false };
            _button = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
            _commandButton = new GUIStyle(_button) { alignment = TextAnchor.MiddleLeft };
            _buttonContent.text = toggleLabel;
            return true;
        }
    }
}
