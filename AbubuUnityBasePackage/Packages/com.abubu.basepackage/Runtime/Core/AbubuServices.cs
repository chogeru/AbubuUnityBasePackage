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
    /// Sound / Scenes などの static ショートカットは内部で <see cref="TryResolve{T}"/> を使っている。
    /// static フィールドは RuntimeInitializeOnLoadMethod で毎回リセットするので、
    /// Enter Play Mode Options (Domain Reload 無効) の環境でも前回の再生の状態は残らない。
    /// </remarks>
    public static class AbubuServices
    {
        /// <summary>Application.quitting 以降 true。終了中にサービスを取得しようとしたときの判定に使う</summary>
        private static bool _quitting;

        /// <summary>アプリ終了処理中は false。終了中に ProjectContext が再生成されるのを防ぐ</summary>
        public static bool IsAvailable => !_quitting && (ProjectContext.HasInstance || ProjectContext.TryGetPrefab() != null);

        /// <summary>ProjectContext のコンテナ。初回アクセス時に Resources/ProjectContext が生成される。</summary>
        public static DiContainer Container => ProjectContext.Instance.Container;

        /// <summary>
        /// サービスを取得する。見つからなければ Zenject が例外を投げる。
        /// 無くても動かしたい場合は <see cref="TryResolve{T}"/> を使う。
        /// </summary>
        public static T Resolve<T>() => Container.Resolve<T>();

        /// <summary>「未セットアップ」の警告を出し済みか (ログを何度も出さないため)</summary>
        private static bool _warnedNotSetup;

        /// <summary>
        /// 終了処理中・未セットアップ時は null を返す (static ショートカットはこちらを使う)。
        /// 未セットアップが原因の場合だけ、気づけるように警告を 1 回出す
        /// </summary>
        public static T TryResolve<T>() where T : class
        {
            if (IsAvailable)
            {
                // GameInput.Move のように毎フレーム呼ばれるため、コンテナを引くのは最初の 1 回だけにする。
                // コンテナが作り直されたら (テストなど) 引き直す。未登録 (null) は、後から登録される場合に備えて覚えない
                var container = Container;
                if (!ReferenceEquals(Cache<T>.Container, container))
                {
                    var service = container.TryResolve<T>();
                    if (service == null) return null;

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

            if (!_quitting && !_warnedNotSetup)
            {
                _warnedNotSetup = true;
                Debug.LogWarning("[Abubu] ProjectContext が見つからないため、Sound / Scenes などのショートカットは何もしません。Tools > Abubu > Setup Project を実行してください。");
            }
            return null;
        }

        /// <summary>
        /// 再生開始時、どのシーンのロードよりも先に呼ばれる。static の状態を初期化し、終了検知を登録する。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain Reload 無効時のために毎回リセットする
            _quitting = false;
            _warnedNotSetup = false;
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
    /// 任意モジュール (MessagePipe / Addressables など) の Installer。
    /// アセンブリに [assembly: AbubuModuleInstaller(typeof(MyInstaller))] を付けると AbubuInstaller が自動で実行する。
    /// 依存パッケージが無ければそのアセンブリ自体がコンパイルされないため、コア側に #if が要らない。
    /// </summary>
    /// <remarks>
    /// 実装クラスには引数なしのコンストラクタが必要 (AbubuInstaller がリフレクションで生成する)。
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
