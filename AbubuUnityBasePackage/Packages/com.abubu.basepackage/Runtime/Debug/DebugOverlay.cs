using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Abubu.Debugging
{
    /// <summary>
    /// FPS・メモリ・Time.timeScale などを画面に重ねて表示するデバッグ表示 (IMGUI)。
    /// 他のモジュールに依存しないので、シーンに置くだけで使える。
    /// <code>
    /// DebugOverlay.EnsureExists();                                   // どこからでも生成
    /// DebugOverlay.Register("score", () => _score.ToString());       // 任意の項目を追加
    /// DebugOverlay.Unregister("score");
    /// </code>
    /// 画面左上の小さな [Dbg] ボタン (またはキー) で詳細の表示/非表示を切り替える。
    /// </summary>
    /// <remarks>
    /// 新 Input System のみ有効なプロジェクトではキー切り替えは使えないため、ボタンか <see cref="Toggle"/> を使う。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DebugOverlay : MonoBehaviour
    {
        private static readonly Dictionary<string, Func<string>> Items = new();
        private static DebugOverlay _instance;

        [Tooltip("開発ビルド / エディタ以外 (リリースビルド) では何も表示しない")]
        [SerializeField] private bool onlyInDevelopmentBuild = true;

        [Tooltip("起動時から詳細を表示する")]
        [SerializeField] private bool startVisible = true;

        [Tooltip("詳細の表示/非表示を切り替えるキー (旧 Input Manager が有効な場合のみ)")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        [Tooltip("FPS の平均を取る時間 (秒)")]
        [SerializeField, Min(0.1f)] private float fpsInterval = 0.5f;

        private readonly StringBuilder _builder = new();
        private float _elapsed;
        private int _frames;
        private float _fps;
        private float _frameMs;
        private bool _visible;
        private GUIStyle _label;
        private GUIStyle _button;
        private int _styleFontSize;

        /// <summary>詳細を表示しているか</summary>
        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        /// <summary>シーンに無ければ DontDestroyOnLoad の DebugOverlay を作る</summary>
        public static DebugOverlay EnsureExists()
        {
            if (_instance != null) return _instance;

            var existing = FindFirstObjectByType<DebugOverlay>();
            if (existing != null) return existing;

            var go = new GameObject("[Abubu.DebugOverlay]");
            DontDestroyOnLoad(go);
            return go.AddComponent<DebugOverlay>();
        }

        /// <summary>表示項目を追加する。同じ名前で呼ぶと置き換わる。getter は毎フレーム呼ばれるので軽く保つこと</summary>
        public static void Register(string label, Func<string> getter)
        {
            if (string.IsNullOrEmpty(label)) throw new ArgumentException("label が空です", nameof(label));
            Items[label] = getter ?? throw new ArgumentNullException(nameof(getter));
        }

        public static void Unregister(string label) => Items.Remove(label);

        /// <summary>詳細の表示/非表示を切り替える</summary>
        public void Toggle() => _visible = !_visible;

        // Domain Reload 無効時に、前回の再生の項目や参照が残らないようにする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Items.Clear();
            _instance = null;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            _visible = startVisible;
            if (onlyInDevelopmentBuild && !Debug.isDebugBuild) enabled = false;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (UnityEngine.Input.GetKeyDown(toggleKey)) Toggle();
#endif

            // timeScale が 0 でも測れるよう unscaledDeltaTime を使う
            _elapsed += Time.unscaledDeltaTime;
            _frames++;
            if (_elapsed < fpsInterval) return;

            _fps = _frames / _elapsed;
            _frameMs = _elapsed / _frames * 1000f;
            _elapsed = 0f;
            _frames = 0;
        }

        private void OnGUI()
        {
            EnsureStyles();

            var margin = _styleFontSize * 0.5f;
            var buttonSize = new Vector2(_styleFontSize * 3f, _styleFontSize * 1.6f);
            if (GUI.Button(new Rect(margin, margin, buttonSize.x, buttonSize.y), "Dbg", _button)) Toggle();
            if (!_visible) return;

            _builder.Clear();
            _builder.Append("FPS ").Append(_fps.ToString("F1")).Append("  (").Append(_frameMs.ToString("F1")).AppendLine(" ms)");
            _builder.Append("Scene ").AppendLine(SceneManager.GetActiveScene().name);
            _builder.Append("TimeScale ").AppendLine(Time.timeScale.ToString("0.##"));
            _builder.Append("Resolution ").Append(Screen.width).Append('x').Append(Screen.height).Append(" @").Append(Screen.currentResolution.refreshRateRatio.value.ToString("0")).AppendLine("Hz");
            _builder.Append("Mono ").Append(ToMb(Profiler.GetMonoUsedSizeLong())).Append(" / ").Append(ToMb(Profiler.GetMonoHeapSizeLong())).AppendLine(" MB");
            _builder.Append("Total ").Append(ToMb(Profiler.GetTotalAllocatedMemoryLong())).Append(" / ").Append(ToMb(Profiler.GetTotalReservedMemoryLong())).AppendLine(" MB");
            _builder.Append("GC ").Append(GC.CollectionCount(0)).AppendLine();
            foreach (var item in Items)
            {
                string value;
                try
                {
                    value = item.Value();
                }
                catch (Exception e)
                {
                    value = "<error: " + e.Message + ">";
                }

                _builder.Append(item.Key).Append(' ').AppendLine(value);
            }

            var content = new GUIContent(_builder.ToString().TrimEnd());
            var size = _label.CalcSize(content);
            var rect = new Rect(margin, margin * 2f + buttonSize.y, size.x + margin * 2f, size.y + margin);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + margin, rect.y + margin * 0.5f, size.x, size.y), content, _label);
        }

        private static string ToMb(long bytes) => (bytes / (1024f * 1024f)).ToString("F1");

        /// <summary>画面の高さに応じてフォントサイズを決める (高解像度でも読めるように)</summary>
        private void EnsureStyles()
        {
            var fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 45f), 12, 40);
            if (_label != null && fontSize == _styleFontSize) return;

            _styleFontSize = fontSize;
            _label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, normal = { textColor = Color.white }, richText = false };
            _button = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
        }
    }
}
