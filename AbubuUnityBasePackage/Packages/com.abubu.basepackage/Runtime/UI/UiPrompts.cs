using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.UI
{
    /// <summary>ボタン操作の案内 1 つ分 ("Ⓐ 決定" "Ⓑ 戻る" など)。アイコンは Input 側が ActionPath から決める</summary>
    [Serializable]
    public struct UiPrompt
    {
        [Tooltip("Input Action のパス (UI/Submit など)")]
        public string ActionPath;

        [Tooltip("表示する文言")]
        public string Label;

        public UiPrompt(string actionPath, string label)
        {
            ActionPath = actionPath;
            Label = label;
        }
    }

    /// <summary>
    /// ポップアップ・画面のルートのコンポーネントに実装すると、それが一番手前にある間、画面下などに操作の案内が表示される
    /// (表示は Input 側の UiButtonPromptBar などが <see cref="IUiPromptService"/> を見て行う)。
    /// <code>
    /// public IReadOnlyList&lt;UiPrompt&gt; GetPrompts() => new[] { new UiPrompt("UI/Submit", "決定"), new UiPrompt("UI/Cancel", "戻る") };
    /// </code>
    /// </summary>
    public interface IUiPromptProvider
    {
        IReadOnlyList<UiPrompt> GetPrompts();
    }

    /// <summary>一番手前のポップアップ (無ければ画面) の <see cref="IUiPromptProvider"/></summary>
    public interface IUiPromptService
    {
        /// <summary>一番手前の UI の案内 (無ければ null)。ポップアップ・画面が切り替わると変わる</summary>
        ReadOnlyReactiveProperty<IUiPromptProvider> Top { get; }

        /// <summary>案内の内容が変わったときに呼ぶ (一番手前は同じまま、表示する案内だけ変えたい場合)</summary>
        void Refresh();

        /// <summary>案内の表示を更新すべきとき (一番手前が変わった・Refresh が呼ばれた) に流れる</summary>
        Observable<Unit> Changed { get; }
    }

    public sealed class UiPromptService : IUiPromptService, IDisposable
    {
        private readonly IPopupService _popups;
        private readonly IScreenService _screens;
        private readonly ReactiveProperty<IUiPromptProvider> _top = new(null);
        private readonly Subject<Unit> _changed = new();
        private readonly CompositeDisposable _subscriptions = new();

        public ReadOnlyReactiveProperty<IUiPromptProvider> Top => _top;
        public Observable<Unit> Changed => _changed;

        public UiPromptService([InjectOptional] IPopupService popups = null, [InjectOptional] IScreenService screens = null)
        {
            _popups = popups;
            _screens = screens;
            _popups?.Changed.Subscribe(this, static (_, self) => self.Update()).AddTo(_subscriptions);
            _screens?.Changed.Subscribe(this, static (_, self) => self.Update()).AddTo(_subscriptions);
            Update();
        }

        public void Refresh()
        {
            Update();
            _changed.OnNext(Unit.Default);
        }

        private void Update()
        {
            var root = _popups?.Current;
            if (root == null) root = _screens?.Current;
            var provider = root != null ? root.GetComponent<IUiPromptProvider>() : null;
            if (ReferenceEquals(provider, _top.Value)) return;
            _top.Value = provider;
            _changed.OnNext(Unit.Default);
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
            _top.Dispose();
            _changed.Dispose();
        }
    }
}
