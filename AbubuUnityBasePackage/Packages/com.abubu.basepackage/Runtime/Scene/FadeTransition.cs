using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Abubu.Scene
{
    /// <summary>
    /// 画面全体を単色でフェードする標準トランジション。
    /// プレハブ不要でコードから Canvas を生成するため、インポート直後から使える。
    /// 独自演出にしたい場合は ISceneTransition を実装して Rebind する。
    /// </summary>
    public sealed class FadeTransition : ISceneTransition, IDisposable
    {
        private readonly CanvasGroup _group;
        private readonly Image _image;
        private MotionHandle _handle;

        public FadeTransition(SceneSettings settings)
        {
            var go = new GameObject("[Abubu.SceneFade]");
            Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = settings.SortingOrder;
            go.AddComponent<GraphicRaycaster>();

            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var imageGo = new GameObject("Fade");
            imageGo.transform.SetParent(go.transform, false);
            _image = imageGo.AddComponent<Image>();
            _image.color = settings.FadeColor;
            var rect = _image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public UniTask FadeOutAsync(float duration, Color color, CancellationToken ct)
        {
            _image.color = color;
            _group.blocksRaycasts = true; // 遷移中の入力をブロック
            return FadeAsync(1f, duration, ct);
        }

        public async UniTask FadeInAsync(float duration, CancellationToken ct)
        {
            await FadeAsync(0f, duration, ct);
            _group.blocksRaycasts = false;
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
            await _handle.ToUniTask(CancelBehavior.Complete, ct).SuppressCancellationThrow();
        }

        public void Dispose()
        {
            _handle.TryCancel();
            if (_group != null) Object.Destroy(_group.gameObject);
        }
    }
}
