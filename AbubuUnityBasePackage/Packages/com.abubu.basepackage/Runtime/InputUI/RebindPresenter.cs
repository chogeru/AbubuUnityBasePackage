using System;
using System.Collections.Generic;
using System.Threading;
using Abubu.Presentation;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Abubu.Inputs.UI
{
    /// <summary>
    /// 割り当て変更 (キーコンフィグ) 画面の Presenter。<see cref="IInputService"/> と <see cref="IRebindView"/> を結線する。
    /// <code>
    /// // RebindView (uGUI) を置けば自動で生成される。独自 UI の場合は IRebindView を実装して
    /// // ViewBase&lt;RebindPresenter&gt; を継承するだけでよい
    /// </code>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>割り当て変更は <see cref="IInputService.RebindAsync"/> で行う (保存もされる)。複合バインディングは各パートを順に割り当てる</item>
    /// <item>同じ ActionMap の別の割り当てと操作がかぶったら <see cref="IRebindView.ConflictPolicy"/> に従う
    /// (Allow: 警告だけ / Swap: 相手に変更前の操作を割り当てる / Reject: 取り消す)</item>
    /// <item>操作の系統 (<see cref="IInputService.CurrentScheme"/>: キーボード&amp;マウス / ゲームパッド) の切り替え・割り当ての変更・
    /// アイコン表の差し替えで表示を作り直す (RebindEntry.BindingIndex が -1 の行は、使用中の系統向けの割り当てに切り替わる)</item>
    /// <item>入力待ち中は <see cref="IInputService.IsRebinding"/> が true になる。UI の「戻る」などはそれを見て止める</item>
    /// <item>表示文字列の決め方 (優先順): コンテナの <see cref="IRebindLabels"/> → View の Labels → 英語の既定</item>
    /// </list>
    /// </remarks>
    public class RebindPresenter : PresenterBase
    {
        /// <summary>割り当て変更前の状態 (1 パート分)</summary>
        protected readonly struct Previous
        {
            /// <summary>割り当てのインデックス</summary>
            public readonly int Index;
            /// <summary>変更前の上書き (null = 上書き無し)</summary>
            public readonly string OverridePath;
            /// <summary>変更前の実際の操作</summary>
            public readonly string EffectivePath;

            public Previous(int index, string overridePath, string effectivePath)
            {
                Index = index;
                OverridePath = overridePath;
                EffectivePath = effectivePath;
            }
        }

        protected IInputService Input { get; }
        protected IRebindView View { get; }
        protected IRebindLabels Labels { get; }
        protected IInputGlyphService Glyphs { get; }

        private CancellationTokenSource _rebindCts;
        private int _listeningRow = -1;
        private bool _disposed;

        /// <summary>入力待ち中か</summary>
        public bool IsRebinding => _listeningRow >= 0;

        /// <summary>入力待ち中の行 (無ければ -1)</summary>
        public int ListeningRow => _listeningRow;

        /// <param name="input">入力</param>
        /// <param name="view">表示先</param>
        /// <param name="labels">表示文字列 (任意。ローカライズ用に差し替える)</param>
        /// <param name="glyphs">ボタンアイコン (任意。あれば行にアイコンを出す)</param>
        public RebindPresenter(IInputService input, IRebindView view, IRebindLabels labels = null, IInputGlyphService glyphs = null)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            View = view ?? throw new ArgumentNullException(nameof(view));
            Labels = labels ?? view.Labels ?? new RebindLabels();
            Glyphs = glyphs;
        }

        protected override void OnInitialize()
        {
            View.OnRebindRequested.Subscribe(this, static (row, self) => self.RebindAsync(row).Forget()).AddTo(Disposables);
            View.OnResetRequested.Subscribe(this, static (row, self) => self.Reset(row)).AddTo(Disposables);
            View.OnResetAllRequested.Subscribe(this, static (_, self) => self.ResetAll()).AddTo(Disposables);
            View.OnCancelRequested.Subscribe(this, static (_, self) => self.Cancel()).AddTo(Disposables);

            // CurrentScheme は購読時に現在値が流れるので、ここで最初の表示も行われる。
            // CurrentDevice ではなく CurrentScheme を見るので、タッチ・ペンを触っても行が「未割り当て」にならない
            Input.CurrentScheme.Subscribe(this, static (_, self) => self.RefreshAll()).AddTo(Disposables);
            Input.BindingsChanged.Subscribe(this, static (_, self) => self.RefreshAll()).AddTo(Disposables);
            Glyphs?.Changed.Subscribe(this, static (_, self) => self.RefreshAll()).AddTo(Disposables);
        }

        // ---------------------------------------------------------------- 操作

        /// <summary>
        /// 行の割り当てを対話的に変更する。変更が確定したら true (キャンセル・取り消し (Reject) なら false)
        /// </summary>
        public async UniTask<bool> RebindAsync(int row, CancellationToken ct = default)
        {
            if (_disposed || IsRebinding) return false;
            if (!TryResolve(row, out var action, out var actionPath, out var bindingIndex)) return false;

            var targets = new List<int>();
            InputBindingUtility.GetTargetIndices(action, bindingIndex, targets);
            var previous = new List<Previous>(targets.Count);
            foreach (var index in targets)
            {
                var binding = action.bindings[index];
                previous.Add(new Previous(index, binding.overridePath, binding.effectivePath));
            }

            _listeningRow = row;
            _rebindCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            View.SetMessage(string.Empty);
            View.SetListening(true, Labels.ListeningPrompt(GetActionName(row, action)));
            RefreshAll();

            bool completed;
            try
            {
                completed = await Input.RebindAsync(actionPath, bindingIndex, _rebindCts.Token);
            }
            finally
            {
                _listeningRow = -1;
                var cts = _rebindCts;
                _rebindCts = null;
                // キャンセル時はキャンセル処理の途中 (Cancel のコールバック内) でここに来るので、Dispose しない
                if (!cts.IsCancellationRequested) cts.Dispose();
                if (!_disposed) View.SetListening(false, string.Empty);
            }
            if (_disposed) return completed;

            if (completed)
            {
                RemoveDefaultOverrides(action, actionPath, targets);
                completed = ResolveConflicts(action, actionPath, bindingIndex, previous);
            }
            RefreshAll();
            return completed;
        }

        /// <summary>入力待ちをキャンセルする</summary>
        public void Cancel() => _rebindCts?.Cancel();

        /// <summary>行の割り当てを初期状態に戻す (保存される)</summary>
        public void Reset(int row)
        {
            if (_disposed || IsRebinding) return;
            if (!TryResolve(row, out _, out var actionPath, out var bindingIndex)) return;
            View.SetMessage(string.Empty);
            Input.ResetBinding(actionPath, bindingIndex);
            RefreshAll();
        }

        /// <summary>すべての割り当てを初期状態に戻す (保存データも消える)</summary>
        public void ResetAll()
        {
            if (_disposed || IsRebinding) return;
            View.SetMessage(string.Empty);
            Input.ResetBindings();
            RefreshAll();
        }

        /// <summary>初期状態と同じ操作に変更された割り当ては、上書きを解除する (リセットボタンの状態・保存データを正しくする)</summary>
        private void RemoveDefaultOverrides(InputAction action, string actionPath, List<int> targets)
        {
            var changed = false;
            foreach (var index in targets)
            {
                var binding = action.bindings[index];
                if (binding.overridePath == null || !string.Equals(binding.overridePath, binding.path, StringComparison.OrdinalIgnoreCase)) continue;
                Input.SetBindingOverride(actionPath, index, null);
                changed = true;
            }
            if (changed) Input.SaveBindings();
        }

        // ---------------------------------------------------------------- かぶりの処理

        /// <summary>変更後の割り当てが他とかぶっていたら、ポリシーに従って処理する。変更を残したら true</summary>
        protected virtual bool ResolveConflicts(InputAction action, string actionPath, int bindingIndex, IReadOnlyList<Previous> previous)
        {
            var conflicts = new List<InputBindingRef>();
            InputBindingUtility.FindConflicts(action, bindingIndex, conflicts);
            if (conflicts.Count == 0) return true;

            var others = JoinActionNames(conflicts);
            switch (View.ConflictPolicy)
            {
                case RebindConflictPolicy.Reject:
                {
                    var attempted = Input.GetBindingDisplayString(actionPath, bindingIndex);
                    foreach (var p in previous) Input.SetBindingOverride(actionPath, p.Index, p.OverridePath);
                    Input.SaveBindings();
                    View.SetMessage(Labels.Rejected(attempted, others));
                    return false;
                }
                case RebindConflictPolicy.Swap:
                {
                    foreach (var p in previous)
                    {
                        var partConflicts = new List<InputBindingRef>();
                        InputBindingUtility.FindConflicts(action, p.Index, partConflicts);
                        foreach (var other in partConflicts)
                        {
                            // 相手の元の割り当てと同じなら、上書きではなく上書きの解除にする (保存データを汚さない)
                            var path = string.Equals(p.EffectivePath, other.Binding.path, StringComparison.OrdinalIgnoreCase)
                                ? null
                                : p.EffectivePath ?? string.Empty;
                            Input.SetBindingOverride(other.ActionPath, other.BindingIndex, path);
                        }
                    }
                    Input.SaveBindings();
                    View.SetMessage(Labels.Swapped(others));
                    return true;
                }
                default:
                    View.SetMessage(Labels.Conflict(others));
                    return true;
            }
        }

        // ---------------------------------------------------------------- 表示

        /// <summary>全行の表示を作り直す</summary>
        public void RefreshAll()
        {
            if (_disposed) return;
            var entries = View.Entries;
            if (entries == null) return;
            for (var row = 0; row < entries.Count; row++) RefreshRow(row);
        }

        /// <summary>1 行の表示を作り直す</summary>
        protected virtual void RefreshRow(int row)
        {
            var listening = row == _listeningRow;
            if (!TryResolve(row, out var action, out var actionPath, out var bindingIndex))
            {
                var entry = GetEntry(row);
                var name = action != null ? GetActionName(row, action) : entry?.DisplayName ?? entry?.ActionPath ?? string.Empty;
                View.SetRow(row, new RebindRowState(name, Labels.Unbound, null, false, false, string.Empty, false, false));
                return;
            }

            var conflicts = new List<InputBindingRef>();
            InputBindingUtility.FindConflicts(action, bindingIndex, conflicts);
            var hasConflict = conflicts.Count > 0;

            string bindingText;
            Sprite glyph = null;
            if (listening)
            {
                bindingText = Labels.Listening;
            }
            else
            {
                bindingText = Input.GetBindingDisplayString(actionPath, bindingIndex);
                if (string.IsNullOrEmpty(bindingText)) bindingText = Labels.Unbound;
                if (Glyphs != null) glyph = Glyphs.GetGlyph(actionPath, bindingIndex).Sprite;
            }

            View.SetRow(row, new RebindRowState(
                GetActionName(row, action),
                bindingText,
                glyph,
                listening,
                hasConflict,
                hasConflict ? Labels.Conflict(JoinActionNames(conflicts)) : string.Empty,
                IsOverridden(action, bindingIndex),
                !IsRebinding));
        }

        // ---------------------------------------------------------------- 共通

        /// <summary>行のアクションと割り当てのインデックスを求める。割り当てが無ければ false (action は見つかっていれば入る)</summary>
        protected bool TryResolve(int row, out InputAction action, out string actionPath, out int bindingIndex)
        {
            action = null;
            actionPath = null;
            bindingIndex = -1;

            var entry = GetEntry(row);
            if (entry == null || string.IsNullOrEmpty(entry.ActionPath)) return false;
            action = Input.GetAction(entry.ActionPath);
            if (action == null) return false;

            actionPath = entry.ActionPath;
            bindingIndex = entry.BindingIndex >= 0
                ? entry.BindingIndex
                : InputBindingUtility.FindBindingIndex(action, Input.CurrentScheme.CurrentValue);
            return bindingIndex >= 0 && bindingIndex < action.bindings.Count;
        }

        private RebindEntry GetEntry(int row)
        {
            var entries = View.Entries;
            return entries != null && row >= 0 && row < entries.Count ? entries[row] : null;
        }

        /// <summary>行のアクションの表示名</summary>
        protected string GetActionName(int row, InputAction action)
        {
            var entry = GetEntry(row);
            if (entry != null && !string.IsNullOrEmpty(entry.DisplayName)) return entry.DisplayName;
            return Labels.ActionName(InputBindingUtility.GetActionPath(action), action.name);
        }

        /// <summary>アクションの表示名 (行に設定があればその表示名)</summary>
        private string GetActionName(InputAction action)
        {
            var entries = View.Entries;
            if (entries != null)
            {
                for (var row = 0; row < entries.Count; row++)
                {
                    var entry = entries[row];
                    if (entry == null || string.IsNullOrEmpty(entry.DisplayName)) continue;
                    if (ReferenceEquals(Input.GetAction(entry.ActionPath), action)) return entry.DisplayName;
                }
            }
            return Labels.ActionName(InputBindingUtility.GetActionPath(action), action.name);
        }

        private string JoinActionNames(List<InputBindingRef> conflicts)
        {
            var names = new List<string>();
            foreach (var conflict in conflicts)
            {
                var name = GetActionName(conflict.Action);
                if (!names.Contains(name)) names.Add(name);
            }
            return string.Join(", ", names);
        }

        private static bool IsOverridden(InputAction action, int bindingIndex)
        {
            var targets = new List<int>();
            InputBindingUtility.GetTargetIndices(action, bindingIndex, targets);
            if (!targets.Contains(bindingIndex)) targets.Add(bindingIndex);
            foreach (var index in targets)
            {
                // 空文字の上書き (入れ替えで未割り当てになった) も変更扱い
                if (action.bindings[index].overridePath != null) return true;
            }
            return false;
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _rebindCts?.Cancel();
            base.Dispose();
        }
    }
}
