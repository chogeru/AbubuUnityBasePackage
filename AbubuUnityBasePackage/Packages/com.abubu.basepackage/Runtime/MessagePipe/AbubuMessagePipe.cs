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
    /// <remarks>
    /// 既存プロジェクトへの導入を考慮した動作:
    /// <list type="bullet">
    /// <item>ユーザーが自分の Installer で先に <c>Container.BindMessagePipe()</c> を呼んでいた場合は、その設定 (MessagePipeOptions) を再利用し、二重に登録しない。
    /// (Abubu より後の Installer で BindMessagePipe を呼ぶと重複するため、その場合は Abubu より前に呼ぶこと)</item>
    /// <item>GlobalMessagePipe は未設定のときだけ設定する。ユーザーが自分で SetProvider している場合は上書きしない。
    /// 不要なら <see cref="SetGlobalProvider"/> を false にする。</item>
    /// </list>
    /// </remarks>
    public static class AbubuMessagePipe
    {
        // コンテナごとに保持する (static フィールドだと Domain Reload 無効時に前回の値が残るため)
        private static readonly ConditionalWeakTable<DiContainer, MessagePipeOptions> OptionsTable = new();

        /// <summary>
        /// false にすると GlobalMessagePipe.SetProvider を一切呼ばない (ProjectContext の初期化より前に設定すること)。
        /// 既定は true だが、その場合も他所で設定済みなら上書きしない。
        /// </summary>
        public static bool SetGlobalProvider { get; set; } = true;

        /// <summary>
        /// GlobalMessagePipe を最後に設定したのが Abubu かどうか。
        /// Domain Reload 無効時は前回の再生で設定した (古いコンテナを指す) プロバイダが残るため、
        /// 自分が設定したものだけは次の再生で上書きしてよいと判断するのに使う。
        /// </summary>
        private static bool _globalProviderOwned;

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

            // ユーザーが自分で BindMessagePipe 済み (このコンテナまたは親) なら、それを使う。
            // BindMessagePipe は MessagePipeOptions をインスタンスとして登録するので、インストール中でも安全に解決できる
            if (container.HasBinding<MessagePipeOptions>())
            {
                options = container.TryResolve<MessagePipeOptions>();
                if (options != null)
                {
                    OptionsTable.Add(container, options);
                    return options;
                }
            }

            options = container.BindMessagePipe();
            OptionsTable.Add(container, options);
            // GlobalMessagePipe (static 取得・Diagnostics ウィンドウ) を有効化 (未設定の場合のみ)
            if (SetGlobalProvider) container.BindInterfacesTo<GlobalProviderSetter>().AsSingle().NonLazy();
            return options;
        }

        public static DiContainer BindAbubuMessage<T>(this DiContainer container)
        {
            container.BindMessageBroker<T>(EnsureInstalled(container));
            return container;
        }

        [Preserve]
        private sealed class GlobalProviderSetter : IInitializable
        {
            private readonly DiContainer _container;

            [Preserve]
            public GlobalProviderSetter(DiContainer container) => _container = container;

            public void Initialize()
            {
                if (!SetGlobalProvider) return;
                // 他所 (ユーザーのコード) で設定済みなら触らない。Abubu が前回設定したもの (古いコンテナ) なら差し替える
                if (GlobalMessagePipe.IsInitialized && !_globalProviderOwned) return;

                GlobalMessagePipe.SetProvider(_container.AsServiceProvider());
                _globalProviderOwned = true;
            }
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
