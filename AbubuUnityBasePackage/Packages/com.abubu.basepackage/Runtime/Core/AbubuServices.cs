using Abubu.Audio;
using Abubu.Events;
using Abubu.Pool;
using Abubu.Save;
using Abubu.Scene;
using R3;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Abubu
{
    /// <summary>
    /// DI を使わない場所 (static 関数、DI 管理外の MonoBehaviour など) から
    /// サービスを取得するための入口。通常は [Inject] を推奨。
    /// </summary>
    public static class AbubuServices
    {
        private static bool _quitting;

        /// <summary>アプリ終了処理中は false。終了中に ProjectContext が再生成されるのを防ぐ</summary>
        public static bool IsAvailable => !_quitting && (ProjectContext.HasInstance || ProjectContext.TryGetPrefab() != null);

        /// <summary>ProjectContext のコンテナ。初回アクセス時に Resources/ProjectContext が生成される。</summary>
        public static DiContainer Container => ProjectContext.Instance.Container;

        public static T Resolve<T>() => Container.Resolve<T>();

        private static bool _warnedNotSetup;

        /// <summary>
        /// 終了処理中・未セットアップ時は null を返す (static ショートカットはこちらを使う)。
        /// 未セットアップが原因の場合だけ、気づけるように警告を 1 回出す
        /// </summary>
        public static T TryResolve<T>() where T : class
        {
            if (IsAvailable) return Container.TryResolve<T>();

            if (!_quitting && !_warnedNotSetup)
            {
                _warnedNotSetup = true;
                Debug.LogWarning("[Abubu] ProjectContext が見つからないため、Sound / Scenes などのショートカットは何もしません。Tools > Abubu > Setup Project を実行してください。");
            }
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain Reload 無効時のために毎回リセットする
            _quitting = false;
            _warnedNotSetup = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => _quitting = true;

        /// <summary>指定シーンに SceneContext があればそのコンテナ、無ければ ProjectContext のコンテナを返す</summary>
        public static DiContainer ResolveContainer(UnityScene scene)
        {
            foreach (var context in Object.FindObjectsByType<SceneContext>(FindObjectsSortMode.None))
            {
                if (context.gameObject.scene == scene && context.HasResolved) return context.Container;
            }

            return Container;
        }
    }

    /// <summary>
    /// 任意モジュール (MessagePipe / Addressables など) の Installer。
    /// アセンブリに [assembly: AbubuModuleInstaller(typeof(MyInstaller))] を付けると AbubuInstaller が自動で実行する。
    /// 依存パッケージが無ければそのアセンブリ自体がコンパイルされないため、コア側に #if が要らない。
    /// </summary>
    public interface IAbubuModuleInstaller
    {
        void Install(Zenject.DiContainer container);
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class AbubuModuleInstallerAttribute : Attribute
    {
        public Type InstallerType { get; }
        public AbubuModuleInstallerAttribute(Type installerType) => InstallerType = installerType;
    }
}
