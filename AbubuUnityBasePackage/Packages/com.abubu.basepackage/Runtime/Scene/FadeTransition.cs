using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Abubu.SceneFlow
{
    /// <summary>
    /// 画面全体を単色でフェードする標準トランジション。
    /// <see cref="SceneSettings.FadePrefab"/> が未設定ならコードから Canvas を生成するため、インポート直後から使える。
    /// </summary>
    /// <remarks>
    /// ・フェード用のオブジェクトは最初のフェード時に作る (使われないうちは何も生成しない)。DontDestroyOnLoad に置かれ、Dispose で破棄される。
    /// ・自動生成の Canvas は ScreenSpaceOverlay なので、VR (XR) のヘッドセットには表示されない。
    ///   VR ではカメラ前のクワッドや XR のフェード機能を使う <see cref="ISceneTransition"/> を実装し、
    ///   AbubuInstaller より前の Installer で Container.Bind&lt;ISceneTransition&gt;().To&lt;MyTransition&gt;().AsSingle() とバインドする。
    ///   (FadePrefab に WorldSpace / ScreenSpaceCamera の Canvas を指定する方法もある)
    /// ・FadePrefab を使う場合、ルートか子に CanvasGroup が必要。子の Image (最初に見つかったもの) にフェード色が反映される。
    /// </remarks>
    public sealed class FadeTransition : ISceneTransition, IDisposable
    {
        private readonly SceneSettings _settings;
        private GameObject _root;
        private CanvasGroup _group;
        private Image _image;
        private MotionHandle _handle;
        private bool _disposed;

        public FadeTransition(SceneSettings settings)
        {
            _settings = settings ?? new SceneSettings();
        }

        public UniTask FadeOutAsync(float duration, Color color, CancellationToken ct)
        {
            if (!EnsureCreated()) return UniTask.CompletedTask;
            if (_image != null) _image.color = color;
            _group.blocksRaycasts = true; // 遷移中の入力をブロック
            return FadeAsync(1f, duration, ct);
        }

        public async UniTask FadeInAsync(float duration, CancellationToken ct)
        {
            // 一度もフェードアウトしていなければ、覆っているものも無いので何もしない
            if (_group == null) return;
            await FadeAsync(0f, duration, ct);
            if (_group != null) _group.blocksRaycasts = false;
        }

        /// <summary>フェード用のオブジェクトが無ければ作る。破棄済みなら false</summary>
        private bool EnsureCreated()
        {
            if (_disposed) return false;
            if (_group != null) return true;

            // シーン遷移などで外から壊されていた場合に備えて作り直す
            if (_root != null) Object.Destroy(_root);
            if (_settings.FadePrefab != null && TryCreateFromPrefab(_settings.FadePrefab)) return true;
            CreateDefault();
            return true;
        }

        private bool TryCreateFromPrefab(GameObject prefab)
        {
            var go = Object.Instantiate(prefab);
            go.name = "[Abubu.SceneFade]";
            var group = go.GetComponentInChildren<CanvasGroup>(true);
            if (group == null)
            {
                Debug.LogError($"[Abubu.Scene] FadePrefab \"{prefab.name}\" に CanvasGroup がありません。既定のフェードを使います。");
                Object.Destroy(go);
                return false;
            }

            Object.DontDestroyOnLoad(go);
            _root = go;
            _group = group;
            _image = go.GetComponentInChildren<Image>(true);
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            return true;
        }

        private void CreateDefault()
        {
            var go = new GameObject("[Abubu.SceneFade]");
            Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _settings.SortingOrder;
            go.AddComponent<GraphicRaycaster>();

            _root = go;
            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var imageGo = new GameObject("Fade");
            imageGo.transform.SetParent(go.transform, false);
            _image = imageGo.AddComponent<Image>();
            _image.color = _settings.FadeColor;
            var rect = _image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private async UniTask FadeAsync(float to, float duration, CancellationToken ct)
        {
            _handle.TryCancel();
            if (duration <= 0f)
            {
                _group.alpha = to;
                return;
            }

            _handle = LMotion.Create(_group.alpha, to, duration)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(_group, static (x, g) => g.alpha = x);
            // キャンセル時は最後の値まで進めて戻る (例外は投げない。中止の判断は呼び出し側が ct を見て行う)
            await _handle.ToUniTask(CancelBehavior.Complete, ct).SuppressCancellationThrow();
        }

        /// <summary>フェード用のオブジェクトを破棄する。何度呼んでもよい</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle.TryCancel();
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _group = null;
            _image = null;
        }
    }
}
