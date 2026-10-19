using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Abubu.UI
{
    /// <summary>
    /// トースト (数秒で消える短い通知)。操作を止めず、続けて出したものは順番に表示する。
    /// <code>
    /// _toasts.Show("保存しました");
    /// _toasts.Show("実績を解除しました", duration: 4f);
    /// </code>
    /// 見た目は AbubuUiSettings > Toast Prefab で差し替えられる (未設定なら組み込みの見た目)。ポーズ中も表示される。
    /// </summary>
    public interface IToastService
    {
        /// <param name="duration">表示する秒数。null なら AbubuUiSettings > Toast Duration</param>
        void Show(string message, float? duration = null);

        /// <summary>表示中・待ちのトーストをすべて消す</summary>
        void Clear();
    }

    public sealed class ToastService : IToastService, IDisposable
    {
        private const float FadeInSeconds = 0.15f;
        private const float FadeOutSeconds = 0.25f;

        private readonly AbubuUiSettings _settings;
        private readonly Queue<(string Message, float Duration)> _queue = new();
        private CancellationTokenSource _cts = new();
        private Canvas _canvas;
        private GameObject _templateHolder;
        private UiToast _defaultTemplate;
        private UiToast _current;
        private bool _running;

        public ToastService([InjectOptional] AbubuUiSettings settings = null)
        {
            _settings = settings != null ? settings : ScriptableObject.CreateInstance<AbubuUiSettings>();
        }

        public void Show(string message, float? duration = null)
        {
            if (string.IsNullOrEmpty(message)) return;
            _queue.Enqueue((message, duration ?? _settings.ToastDuration));
            if (!_running) RunAsync(_cts.Token).Forget();
        }

        public void Clear()
        {
            _queue.Clear();
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            if (_current != null) Object.Destroy(_current.gameObject);
            _current = null;
            _running = false;
        }

        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            _running = true;
            try
            {
                while (_queue.Count > 0 && !ct.IsCancellationRequested)
                {
                    var (message, duration) = _queue.Dequeue();
                    _current = Object.Instantiate(GetTemplate(), EnsureCanvas().transform, false);
                    _current.Setup(message);
                    var group = _current.CanvasGroup;

                    await FadeAsync(group, 0f, 1f, FadeInSeconds, ct);
                    await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, duration)), ignoreTimeScale: true, cancellationToken: ct);
                    await FadeAsync(group, 1f, 0f, FadeOutSeconds, ct);

                    Object.Destroy(_current.gameObject);
                    _current = null;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!ct.IsCancellationRequested) _running = false;
            }
        }

        /// <summary>ポーズ中 (timeScale 0) でも動くよう、unscaled time で透明度を変える</summary>
        private static async UniTask FadeAsync(CanvasGroup group, float from, float to, float seconds, CancellationToken ct)
        {
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (group == null) return;
                group.alpha = Mathf.Lerp(from, to, t / seconds);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (group != null) group.alpha = to;
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            // ポップアップより手前に出す
            return _canvas = UiUtility.CreateCanvas(null, "[Abubu.Toast]", _settings.SortingOrder + 10, _settings);
        }

        private UiToast GetTemplate()
        {
            if (_settings.ToastPrefab != null) return _settings.ToastPrefab;
            if (_defaultTemplate != null) return _defaultTemplate;

            _templateHolder = new GameObject("[Abubu.UI.ToastTemplate]");
            _templateHolder.SetActive(false);
            Object.DontDestroyOnLoad(_templateHolder);
            return _defaultTemplate = UiDefaultWidgets.CreateToast(_templateHolder.transform);
        }

        public void Dispose()
        {
            _queue.Clear();
            _cts.Cancel();
            _cts.Dispose();
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            if (_templateHolder != null) Object.Destroy(_templateHolder);
        }
    }
}
