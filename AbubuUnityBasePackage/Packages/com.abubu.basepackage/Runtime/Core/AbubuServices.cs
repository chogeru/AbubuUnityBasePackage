using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Abubu
{
    /// <summary>
    /// DI を使わない場所 (static 関数、DI 管理外の MonoBehaviour など) から
    /// サービスを取得するための入口。通常は [Inject] を推奨。
    /// </summary>
    /// <remarks>
    /// Sound / Scenes などの static ショートカットは内部で <see cref="ResolveOrLogError{T}"/> を使っている。
    /// static フィールドは RuntimeInitializeOnLoadMethod で毎回リセットするので、
    /// Enter Play Mode Options (Domain Reload 無効) の環境でも前回の再生の状態は残らない。
    /// </remarks>
    public static class AbubuServices
    {
        /// <summary>Application.quitting 以降 true。終了中にサービスを取得しようとしたときの判定に使う</summary>
        private static bool _quitting;

        /// <summary>Resources/ProjectContext の有無 (null = 未確認)。Resources.Load を毎回しないよう再生ごとに 1 回だけ調べる</summary>
        private static bool? _hasPrefab;

        /// <summary>
        /// サービスを取得できる状態か。アプリ終了処理中は false (終了中に ProjectContext が再生成されるのを防ぐ)。
        /// ProjectContext がまだ無くても Resources/ProjectContext があれば true (初回の取得時に生成される)。
        /// </summary>
        /// <remarks>Resources/ProjectContext の有無は再生開始後の最初の 1 回だけ調べ、以降はその結果を使う。</remarks>
        public static bool IsAvailable
        {
            get
            {
                if (_quitting) return false;
                if (ProjectContext.HasInstance) return true;
                _hasPrefab ??= ProjectContext.TryGetPrefab() != null;
                return _hasPrefab.Value;
            }
        }

        /// <summary>ProjectContext のコンテナ。初回アクセス時に Resources/ProjectContext が生成される。</summary>
        public static DiContainer Container => ProjectContext.Instance.Container;

        /// <summary>
        /// サービスを取得する。見つからなければ Zenject が例外を投げる。
        /// 無くても動かしたい場合は <see cref="TryResolve{T}"/> を使う。
        /// </summary>
        public static T Resolve<T>() => Container.Resolve<T>();

        /// <summary>取得できなかった理由</summary>
        private enum MissingReason
        {
            None,
            /// <summary>アプリ終了処理中 (ログは出さない)</summary>
            Quitting,
            /// <summary>ProjectContext もそのプレハブも無い</summary>
            NotSetup,
            /// <summary>ProjectContext はあるが、その型が登録されていない</summary>
            NotRegistered,
        }

        /// <summary>取得失敗のログを出し済みの型 (型ごとに 1 回だけ出すため)</summary>
        private static readonly HashSet<Type> ReportedTypes = new();

        /// <summary>
        /// サービスを取得する。終了処理中・未セットアップ・未登録の場合は null を返す。
        /// 終了処理中以外の失敗は、気づけるように型ごとに 1 回だけ警告を出す。
        /// </summary>
        /// <remarks>
        /// 未登録になる典型例: プロジェクト独自の ProjectContext を使っていて、そこに AbubuInstaller が置かれていない。
        /// </remarks>
        public static T TryResolve<T>() where T : class
        {
            var service = TryResolveCore<T>(out var reason);
            if (service == null) Report<T>(reason, LogType.Warning);
            return service;
        }

        /// <summary>
        /// static ショートカット (Sound / Scenes / Saves など) 用。<see cref="TryResolve{T}"/> と同じだが、
        /// 取得できなかった場合のログをエラーとして型ごとに 1 回だけ出す (呼び出しが黙って無視されるのを防ぐ)。
        /// </summary>
        public static T ResolveOrLogError<T>() where T : class
        {
            var service = TryResolveCore<T>(out var reason);
            if (service == null) Report<T>(reason, LogType.Error);
            return service;
        }

        private static T TryResolveCore<T>(out MissingReason reason) where T : class
        {
            if (_quitting)
            {
                reason = MissingReason.Quitting;
                return null;
            }
            if (!IsAvailable)
            {
                reason = MissingReason.NotSetup;
                return null;
            }

            reason = MissingReason.None;
            // GameInput.Move のように毎フレーム呼ばれるため、コンテナを引くのは最初の 1 回だけにする。
            // コンテナが作り直されたら (テストなど) 引き直す。未登録 (null) は、後から登録される場合に備えて覚えない
            var container = Container;
            if (!ReferenceEquals(Cache<T>.Container, container))
            {
                var service = container.TryResolve<T>();
                if (service == null)
                {
                    reason = MissingReason.NotRegistered;
                    return null;
                }

                if (!Cache<T>.Registered)
                {
                    Cache<T>.Registered = true;
                    CacheClearers.Add(static () =>
                    {
                        Cache<T>.Container = null;
                        Cache<T>.Value = null;
                    });
                }
                Cache<T>.Container = container;
                Cache<T>.Value = service;
            }
            return Cache<T>.Value;
        }

        /// <summary>取得できなかった理由をログに出す (型ごとに 1 回だけ。終了処理中は出さない)</summary>
        private static void Report<T>(MissingReason reason, LogType logType)
        {
            if (reason == MissingReason.None || reason == MissingReason.Quitting) return;
            if (!ReportedTypes.Add(typeof(T))) return;

            var message = reason == MissingReason.NotSetup
                ? $"[Abubu] ProjectContext が見つからないため {typeof(T).Name} を取得できません。Sound / Scenes / Saves などのショートカットは何もしません。" +
                  "Tools > Abubu > Setup Project を実行してください (このログは型ごとに 1 回だけ表示します)。"
                : $"[Abubu] ProjectContext に {typeof(T).Name} が登録されていません。独自の ProjectContext を使っている場合は、" +
                  "AbubuInstaller を追加するか、自前の Installer から AbubuInstaller.Install(Container, settings) を呼んでください (このログは型ごとに 1 回だけ表示します)。";
            Debug.LogFormat(logType, LogOption.None, null, "{0}", message);
        }

        /// <summary>
        /// 再生開始時、どのシーンのロードよりも先に呼ばれる。static の状態を初期化し、終了検知を登録する。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain Reload 無効時のために毎回リセットする
            _quitting = false;
            _hasPrefab = null;
            ReportedTypes.Clear();
            foreach (var clear in CacheClearers) clear();
            // -= してから += することで、何度呼ばれても登録は 1 つだけになる
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => _quitting = true;

        /// <summary><see cref="TryResolve{T}"/> で取得済みのサービス (型ごと)</summary>
        private static class Cache<T> where T : class
        {
            public static DiContainer Container;
            public static T Value;
            public static bool Registered;
        }

        /// <summary>型ごとの Cache を空にする処理の一覧 (再生開始時にまとめて呼ぶ)</summary>
        private static readonly List<Action> CacheClearers = new();

        /// <summary>指定シーンに SceneContext があればそのコンテナ、無ければ ProjectContext のコンテナを返す</summary>
        /// <remarks>
        /// シーンに置かれたオブジェクトへ後から Inject したいときに使う。
        /// SceneContext がまだ Install を終えていない (HasResolved が false) 場合は ProjectContext 側を返す。
        /// シーン内を検索するので、毎フレーム呼ぶような使い方は避けること。
        /// </remarks>
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
    /// Abubu のサービスを登録する Installer であることを示す目印。
    /// 自前の MonoInstaller から AbubuInstaller.Install(Container, settings) を呼んでいる場合は、その Installer に付けると
    /// 起動時の ProjectContext 自動生成 (AbubuBootstrap) の対象になる (AbubuInstaller 自体は付けなくても型名で判定される)。
    /// </summary>
    public interface IAbubuProjectInstaller
    {
    }

    /// <summary>ProjectContext のプレハブが Abubu 用に構成されているかを調べる</summary>
    public static class AbubuProjectContextInspector
    {
        /// <summary>AbubuInstaller の完全名 (Core から Abubu.Installer アセンブリを参照できないため名前で比べる)</summary>
        private const string AbubuInstallerTypeName = "Abubu.AbubuInstaller";

        /// <summary>
        /// プレハブ (または ProjectContext のインスタンス) に、AbubuInstaller か <see cref="IAbubuProjectInstaller"/> を実装した
        /// MonoInstaller が含まれているか。子オブジェクトと、Context の Installers / InstallerPrefabs を調べる。
        /// </summary>
        public static bool ContainsAbubuInstaller(GameObject prefab)
        {
            if (prefab == null) return false;

            foreach (var installer in prefab.GetComponentsInChildren<MonoInstaller>(true))
            {
                if (IsAbubuInstaller(installer)) return true;
            }

            var context = prefab.GetComponent<Context>();
            if (context == null) return false;
            foreach (var installer in context.Installers)
            {
                if (IsAbubuInstaller(installer)) return true;
            }
            foreach (var installer in context.InstallerPrefabs)
            {
                if (IsAbubuInstaller(installer)) return true;
            }
            return false;
        }

        private static bool IsAbubuInstaller(MonoInstaller installer) =>
            installer != null && (installer is IAbubuProjectInstaller || installer.GetType().FullName == AbubuInstallerTypeName);
    }

    /// <summary>
    /// 任意モジュール (MessagePipe / Addressables など) の Installer。
    /// アセンブリに [assembly: AbubuModuleInstaller(typeof(MyInstaller))] を付けると AbubuInstaller が自動で実行する。
    /// 依存パッケージが無ければそのアセンブリ自体がコンパイルされないため、コア側に #if が要らない。
    /// </summary>
    /// <remarks>
    /// 探索の対象は Abubu.Core を参照しているアセンブリ (ユーザーのアセンブリでも良い。名前空間・アセンブリ名に Abubu. を付ける必要はない)。
    /// 実装クラスには引数なしのコンストラクタが必要 (AbubuInstaller がリフレクションで生成する)。
    /// IL2CPP のコード削除に備えて、アセンブリに [assembly: UnityEngine.Scripting.AlwaysLinkAssembly]、コンストラクタに [Preserve] を付けること。
    /// </remarks>
    public interface IAbubuModuleInstaller
    {
        /// <summary>ProjectContext のコンテナにモジュールのサービスを登録する。</summary>
        void Install(Zenject.DiContainer container);
    }

    /// <summary>
    /// アセンブリに付けて、そのアセンブリが提供する <see cref="IAbubuModuleInstaller"/> を AbubuInstaller に知らせる目印。
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class AbubuModuleInstallerAttribute : Attribute
    {
        /// <summary>実行する Installer の型 (<see cref="IAbubuModuleInstaller"/> を実装していること)</summary>
        public Type InstallerType { get; }
        public AbubuModuleInstallerAttribute(Type installerType) => InstallerType = installerType;
    }
}
