using System;
using System.Collections.Generic;
using Abubu.Inputs.UI;
using R3;
using Zenject;

namespace Abubu.UI
{
    /// <summary>
    /// 一番手前のポップアップ・画面の操作の案内 (<see cref="IUiPromptService"/>) を、ボタン案内バー (UiButtonPromptBar) に渡す。
    /// 案内が無いときは null (バーの既定の案内) になる。
    /// </summary>
    public sealed class UiPromptButtonSource : IButtonPromptSource, IDisposable
    {
        private readonly IUiPromptService _prompts;
        private readonly ReactiveProperty<IReadOnlyList<ButtonPrompt>> _current = new(null);
        private readonly IDisposable _subscription;

        public ReadOnlyReactiveProperty<IReadOnlyList<ButtonPrompt>> Prompts => _current;

        public UiPromptButtonSource([InjectOptional] IUiPromptService prompts = null)
        {
            _prompts = prompts;
            if (_prompts == null) return;
            _subscription = _prompts.Changed.Subscribe(this, static (_, self) => self.Update());
            Update();
        }

        private void Update()
        {
            var source = _prompts.Top.CurrentValue?.GetPrompts();
            if (source == null)
            {
                _current.Value = null;
                return;
            }

            var prompts = new List<ButtonPrompt>(source.Count);
            foreach (var prompt in source) prompts.Add(new ButtonPrompt(prompt.ActionPath, prompt.Label));
            _current.Value = prompts;
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _current.Dispose();
        }
    }
}
