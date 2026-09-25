using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Abubu.Scene;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace Abubu.Boot
{
    [Serializable]
    public sealed class BootSettings
    {
        [Tooltip("任意。起動専用シーン (ロゴ・初期化用)。空ならどのシーンからでもそのまま始まる")]
        public string BootSceneName;
        [Tooltip("Boot シーンから通常起動した場合に、初期化後に遷移するシーン (タイトルなど)")]
        public string FirstSceneName;
        [Tooltip("エディタで再生したとき、開いているシーンに関係なく Boot シーンから起動し、初期化後に元のシーンへ戻る")]
        public bool EditorStartFromBootScene = true;
    }

    /// <summary>
    /// 起動時に一度だけ実行する初期化処理。Installer で Container.Bind&lt;IBootTask&gt;().To&lt;MyTask&gt;().AsSingle() と登録する。
    /// (例: セーブデータ読み込み、Addressables のカタログ更新、ログイン処理)
    /// </summary>
    public interface IBootTask
    {
        /// <summary>小さい順に実行される</summary>
        int Order { get; }
        UniTask RunAsync(CancellationToken ct);
    }

    public interface IBootService
    {
        ReadOnlyReactiveProperty<bool> IsReady { get; }
        /// <summary>すべての IBootTask が終わるまで待つ。シーン側の Start で await すると安全</summary>
        UniTask WaitReadyAsync(CancellationToken ct = default);
    }

    public sealed class BootService : IBootService, IInitializable, IDisposable
    {
        /// <summary>エディタ再生時に戻るシーン名を受け渡すキー (Abubu.Editor 側と共有)</summary>
        public const string EditorReturnSceneKey = "Abubu.Boot.ReturnScene";

        private readonly BootSettings _settings;
        private readonly List<IBootTask> _tasks;
        private readonly ISceneService _scene;
        private readonly ReactiveProperty<bool> _isReady = new(false);
        private readonly CancellationTokenSource _cts = new();

        public ReadOnlyReactiveProperty<bool> IsReady => _isReady;

        public BootService(BootSettings settings, ISceneService scene, List<IBootTask> tasks = null)
        {
            _settings = settings;
            _scene = scene;
            _tasks = tasks ?? new List<IBootTask>();
        }

        public void Initialize() => RunAsync(_cts.Token).Forget();

        public async UniTask WaitReadyAsync(CancellationToken ct = default)
        {
            if (_isReady.Value) return;
            await _isReady.Where(static x => x).FirstAsync(ct);
        }

        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            foreach (var task in _tasks.OrderBy(t => t.Order))
            {
                try
                {
                    await task.RunAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    // 1 つの初期化失敗でゲーム全体を止めない
                    Debug.LogException(e);
                }
            }

            _isReady.Value = true;

            var next = GetNextScene();
            if (!string.IsNullOrEmpty(next)) await _scene.LoadAsync(next, ct: ct);
        }

        private string GetNextScene()
        {
            if (string.IsNullOrEmpty(_settings.BootSceneName)) return null;
            if (SceneManager.GetActiveScene().name != _settings.BootSceneName) return null;

#if UNITY_EDITOR
            var returnScene = UnityEditor.SessionState.GetString(EditorReturnSceneKey, string.Empty);
            UnityEditor.SessionState.EraseString(EditorReturnSceneKey);
            if (!string.IsNullOrEmpty(returnScene)) return returnScene;
#endif
            return _settings.FirstSceneName;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _isReady.Dispose();
        }
    }

    /// <summary>
    /// どのシーンから再生しても、最初のシーンがロードされる前に ProjectContext (= 全サービス) を生成する。
    /// これにより「このシーンから直接再生したら落ちる」を防ぐ。
    /// </summary>
    public static class AbubuBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (ProjectContext.HasInstance) return;

            if (ProjectContext.TryGetPrefab() == null)
            {
                Debug.LogWarning("[Abubu] Resources/ProjectContext.prefab がありません。Tools > Abubu > Setup Project を実行してください。");
                return;
            }

            // 生成と同時に Installer 実行・IInitializable 呼び出しまで行われる
            _ = ProjectContext.Instance;
        }
    }
}
