using System.Runtime.CompilerServices;
using Abubu;
using Abubu.Events;
using MessagePipe;
using UnityEngine.Scripting;
using Zenject;

[assembly: AbubuModuleInstaller(typeof(MessagePipeModuleInstaller))]

namespace Abubu.Events
{
    /// <summary>
    /// MessagePipe の Zenject 連携。AbubuInstaller が自動で BindMessagePipe を行う。
    /// メッセージ型は IL2CPP の制約で個別登録が必要:
    /// <code>
    /// // 任意の Installer で
    /// Container.BindAbubuMessage&lt;EnemyDied&gt;();
    /// // 使う側
    /// [Inject] IPublisher&lt;EnemyDied&gt; _publisher;
    /// [Inject] ISubscriber&lt;EnemyDied&gt; _subscriber;
    /// </code>
    /// </summary>
    public static class AbubuMessagePipe
    {
        // コンテナごとに保持する (static フィールドだと Domain Reload 無効時に前回の値が残るため)
        private static readonly ConditionalWeakTable<DiContainer, MessagePipeOptions> OptionsTable = new();

        /// <summary>
        /// このコンテナ (または親) で MessagePipe が未設定なら設定し、Options を返す。何度呼んでも安全。
        /// AbubuInstaller より前の Installer から BindAbubuMessage を呼んでも動作する。
        /// </summary>
        public static MessagePipeOptions EnsureInstalled(DiContainer container)
        {
            if (OptionsTable.TryGetValue(container, out var options)) return options;

            // 親 (ProjectContext) で設定済みならそれを使う
            foreach (var parent in container.ParentContainers)
            {
                if (OptionsTable.TryGetValue(parent, out options))
                {
                    OptionsTable.Add(container, options);
                    return options;
                }
            }

            options = container.BindMessagePipe();
            OptionsTable.Add(container, options);
            // GlobalMessagePipe (static 取得・Diagnostics ウィンドウ) を有効化
            container.BindInterfacesTo<GlobalProviderSetter>().AsSingle().NonLazy();
            return options;
        }

        public static DiContainer BindAbubuMessage<T>(this DiContainer container)
        {
            container.BindMessageBroker<T>(EnsureInstalled(container));
            return container;
        }

        private sealed class GlobalProviderSetter : IInitializable
        {
            private readonly DiContainer _container;
            public GlobalProviderSetter(DiContainer container) => _container = container;
            public void Initialize() => GlobalMessagePipe.SetProvider(_container.AsServiceProvider());
        }
    }

    /// <summary>MessagePipe 導入時に AbubuInstaller から自動で呼ばれる</summary>
    [Preserve]
    public sealed class MessagePipeModuleInstaller : IAbubuModuleInstaller
    {
        [Preserve]
        public MessagePipeModuleInstaller() { }

        public void Install(DiContainer container) => AbubuMessagePipe.EnsureInstalled(container);
    }
}
