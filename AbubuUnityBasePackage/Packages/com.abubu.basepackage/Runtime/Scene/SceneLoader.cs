using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: InternalsVisibleTo("Abubu.Tests.Editor")]
[assembly: InternalsVisibleTo("Abubu.Tests.Runtime")]

namespace Abubu.SceneFlow
{
    /// <summary>
    /// SceneService が SceneManager を直接呼ばないための内部の継ぎ目 (テストで偽物に差し替える)。
    /// 公開 API ではない。通常は <see cref="UnitySceneLoader"/> が使われる。
    /// </summary>
    internal interface ISceneLoader
    {
        /// <summary>アクティブなシーンの名前</summary>
        string ActiveSceneName { get; }

        /// <summary>読み込めるシーンか (Build Settings 登録済み、またはエディタでは存在するシーンファイルのパス)</summary>
        bool CanLoad(string scene);

        /// <summary>そのシーンが読み込み済みか</summary>
        bool IsLoaded(string scene);

        /// <summary>シーンを Single で読み込み始める。アクティベートは <see cref="ISceneOperation.AllowActivation"/> まで保留する。開始できなければ null</summary>
        ISceneOperation LoadSingle(string scene);

        /// <summary>シーンを Additive で読み込み始める。開始できなければ null</summary>
        ISceneOperation LoadAdditive(string scene);

        /// <summary>シーンの破棄を始める。開始できなければ (最後の 1 シーンなど) null</summary>
        ISceneOperation Unload(string scene);
    }

    /// <summary>読み込み / 破棄の進行中の処理 (AsyncOperation の薄い包み)</summary>
    internal interface ISceneOperation
    {
        /// <summary>0〜1。アクティベート保留中は 0.9 で止まる (Unity の仕様)</summary>
        float Progress { get; }

        /// <summary>保留していたアクティベートを許可する</summary>
        void AllowActivation();

        /// <summary>完了時に呼ばれる処理を登録する (完了済みならすぐ呼ばれる)</summary>
        void OnCompleted(Action callback);

        /// <summary>完了まで待つ。ct でキャンセルしても処理自体は止まらない</summary>
        UniTask WaitAsync(CancellationToken ct);
    }

    /// <summary><see cref="ISceneLoader"/> の標準実装 (SceneManager を呼ぶ)</summary>
    internal sealed class UnitySceneLoader : ISceneLoader
    {
        public static readonly UnitySceneLoader Instance = new();

        public string ActiveSceneName => SceneManager.GetActiveScene().name;

        public bool CanLoad(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return false;
#if UNITY_EDITOR
            if (IsEditorScenePath(scene)) return System.IO.File.Exists(scene);
#endif
            return Application.CanStreamedLevelBeLoaded(scene);
        }

        public bool IsLoaded(string scene) => SceneManager.GetSceneByName(scene).isLoaded;

        public ISceneOperation LoadSingle(string scene)
        {
            AsyncOperation operation;
#if UNITY_EDITOR
            // Build Settings 未登録のシーン (テスト用シーンなど) はパス指定でエディタ専用 API から開く
            if (IsEditorScenePath(scene))
            {
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    scene, new LoadSceneParameters(LoadSceneMode.Single));
            }
            else
#endif
            {
                operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            }
            if (operation == null) return null;
            operation.allowSceneActivation = false;
            return new Operation(operation);
        }

        public ISceneOperation LoadAdditive(string scene) => Wrap(SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive));

        public ISceneOperation Unload(string scene) => Wrap(SceneManager.UnloadSceneAsync(scene));

        private static ISceneOperation Wrap(AsyncOperation operation) => operation == null ? null : new Operation(operation);

#if UNITY_EDITOR
        private static bool IsEditorScenePath(string scene) => scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
#endif

        private sealed class Operation : ISceneOperation
        {
            private readonly AsyncOperation _operation;

            public Operation(AsyncOperation operation) => _operation = operation;

            public float Progress => _operation.progress;
            public void AllowActivation() => _operation.allowSceneActivation = true;
            public void OnCompleted(Action callback) => _operation.completed += _ => callback();
            public UniTask WaitAsync(CancellationToken ct) => _operation.ToUniTask(cancellationToken: ct);
        }
    }
}
