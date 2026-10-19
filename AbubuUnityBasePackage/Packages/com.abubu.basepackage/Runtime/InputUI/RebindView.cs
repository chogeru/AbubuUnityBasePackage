using System.Collections.Generic;
using Abubu.Presentation;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// 割り当て変更 (キーコンフィグ) 画面の uGUI 実装。Canvas 上のオブジェクトに付け、行プレハブと表示するアクションを設定するだけで動く。
    /// <code>
    /// // Inspector: Row Prefab = RebindRowView のプレハブ / Row Container = VerticalLayoutGroup など
    /// //            Entries = "Player/Jump", "Player/Attack", "Player/Move" (BindingIndex -1 で使用中のデバイス向け)
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>Row Prefab が未設定なら、Row Container の子にある <see cref="RebindRowView"/> を上から順に Entries の行として使う。</para>
    /// <para>GameObject &gt; Abubu &gt; Rebind Panel で、行テンプレート付きのパネルを生成できる。</para>
    /// <para>表示文字列は Labels で変更できる (既定は英語)。コンテナに IRebindLabels がバインドされていればそちらが優先される。</para>
    /// <para>
    /// TextMeshPro や UI Toolkit で作る場合は <see cref="IRebindView"/> を実装し、
    /// <c>ViewBase&lt;RebindPresenter&gt;</c> を継承すれば同じ Presenter がそのまま使える。
    /// </para>
    /// </remarks>
    public sealed class RebindView : ViewBase<RebindPresenter>, IRebindView
    {
        [Tooltip("表示するアクション (行) の一覧")]
        [SerializeField] private List<RebindEntry> entries = new();

        [Tooltip("1 行分のプレハブ (シーン内に非表示で置いたテンプレートでもよい)。未設定なら Row Container の子の RebindRowView を使う")]
        [SerializeField] private RebindRowView rowPrefab;
        [Tooltip("行を並べる親 (未設定ならこのオブジェクト)")]
        [SerializeField] private Transform rowContainer;

        [Tooltip("同じ ActionMap の別の割り当てと操作がかぶったときの扱い")]
        [SerializeField] private RebindConflictPolicy conflictPolicy = RebindConflictPolicy.Swap;

        [SerializeField] private Button resetAllButton;
        [Tooltip("入力待ちをキャンセルするボタン (任意)。AbubuInputSettings.RebindCancelPaths の操作 (既定: Esc / ゲームパッドの Start) なら、" +
                 "どのデバイスで押してもキャンセルできる。マウスでこのボタンを押しても、左クリックが割り当てられることはない")]
        [SerializeField] private Button cancelButton;

        [Tooltip("入力待ち中に表示するオブジェクト (「ボタンを押してください」のパネルなど)")]
        [SerializeField] private GameObject listeningPanel;
        [SerializeField] private Text listeningLabel;
        [Tooltip("結果の通知 (入れ替えた・取り消した など) を表示する")]
        [SerializeField] private Text messageLabel;

        [Tooltip("表示文字列。コンテナに IRebindLabels がバインドされている場合はそちらが優先される")]
        [SerializeField] private RebindLabels labels = new();

        [Header("TextMeshPro などを使う場合")]
        [SerializeField] private UnityEvent<string> onListeningPromptChanged = new();
        [SerializeField] private UnityEvent<string> onMessageChanged = new();

        private readonly List<RebindRowView> _rows = new();
        private readonly Subject<int> _rebindRequested = new();
        private readonly Subject<int> _resetRequested = new();
        private readonly CompositeDisposable _disposables = new();

        /// <summary>表示するアクションの一覧 (Start より前に変更すること)</summary>
        public List<RebindEntry> EntryList => entries;

        /// <summary>生成した行</summary>
        public IReadOnlyList<RebindRowView> Rows => _rows;

        /// <inheritdoc />
        public IReadOnlyList<RebindEntry> Entries => entries;

        /// <inheritdoc />
        public RebindConflictPolicy ConflictPolicy => conflictPolicy;

        /// <inheritdoc />
        public IRebindLabels Labels => labels;

        /// <inheritdoc />
        public Observable<int> OnRebindRequested => _rebindRequested;

        /// <inheritdoc />
        public Observable<int> OnResetRequested => _resetRequested;

        /// <inheritdoc />
        public Observable<Unit> OnResetAllRequested => resetAllButton != null ? resetAllButton.OnClickAsObservable() : Observable.Empty<Unit>();

        /// <inheritdoc />
        public Observable<Unit> OnCancelRequested => cancelButton != null ? cancelButton.OnClickAsObservable() : Observable.Empty<Unit>();

        protected override void OnViewStart()
        {
            BuildRows();
            SetListening(false, string.Empty);
            SetMessage(string.Empty);
        }

        protected override void OnViewDestroy()
        {
            _disposables.Dispose();
            _rebindRequested.Dispose();
            _resetRequested.Dispose();
        }

        private void BuildRows()
        {
            var parent = rowContainer != null ? rowContainer : transform;
            _rows.Clear();

            if (rowPrefab != null)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var row = Instantiate(rowPrefab, parent, false);
                    // シーン内に非表示で置いたテンプレートを Row Prefab にした場合も表示する
                    if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
                    _rows.Add(row);
                }
            }
            else
            {
                // 配置済みの行を上から順に使う。余った行は隠す
                var existing = parent.GetComponentsInChildren<RebindRowView>(true);
                for (var i = 0; i < existing.Length; i++)
                {
                    if (i < entries.Count) _rows.Add(existing[i]);
                    else existing[i].gameObject.SetActive(false);
                }
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = i;
                var view = _rows[i];
                if (view.RebindButton != null)
                {
                    view.RebindButton.OnClickAsObservable().Subscribe(_ => _rebindRequested.OnNext(row)).AddTo(_disposables);
                }
                if (view.ResetButton != null)
                {
                    view.ResetButton.OnClickAsObservable().Subscribe(_ => _resetRequested.OnNext(row)).AddTo(_disposables);
                }
            }
        }

        /// <inheritdoc />
        public void SetRow(int row, in RebindRowState state)
        {
            if (row >= 0 && row < _rows.Count && _rows[row] != null) _rows[row].Apply(state);
        }

        /// <inheritdoc />
        public void SetListening(bool listening, string prompt)
        {
            if (listeningPanel != null) listeningPanel.SetActive(listening);
            if (listeningLabel != null) listeningLabel.text = listening ? prompt : string.Empty;
            if (cancelButton != null) cancelButton.interactable = listening;
            onListeningPromptChanged.Invoke(listening ? prompt : string.Empty);
        }

        /// <inheritdoc />
        public void SetMessage(string message)
        {
            message ??= string.Empty;
            if (messageLabel != null)
            {
                messageLabel.text = message;
                messageLabel.enabled = message.Length > 0;
            }
            onMessageChanged.Invoke(message);
        }
    }
}
