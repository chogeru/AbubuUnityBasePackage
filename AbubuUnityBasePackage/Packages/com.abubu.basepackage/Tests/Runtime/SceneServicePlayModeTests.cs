using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Abubu.SceneFlow;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    /// <summary>
    /// 実際の SceneManager / Canvas を使う SceneService・FadeTransition のテスト。
    /// Build Settings に無いシーンは読み込めないため、シーンは SceneManager.CreateScene で実行時に作る。
    /// (遷移の手順そのものは EditMode の SceneServiceTests で偽物を使って確かめている)
    /// </summary>
    public sealed class SceneServicePlayModeTests
    {
        private const string FadeObjectName = "[Abubu.SceneFade]";

        private sealed class NoTransition : ISceneTransition
        {
            public UniTask FadeOutAsync(float duration, Color color, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask FadeInAsync(float duration, CancellationToken ct) => UniTask.CompletedTask;
        }

        // ---- FadeTransition ----

        [UnityTest]
        public IEnumerator FadeTransition_CreatesCanvasOnFirstFade_AndDestroysOnDispose() => UniTask.ToCoroutine(async () =>
        {
            var transition = new FadeTransition(new SceneSettings());
            try
            {
                Assert.That(GameObject.Find(FadeObjectName), Is.Null, "使われるまでは何も作らない");

                await transition.FadeInAsync(0f, CancellationToken.None);
                Assert.That(GameObject.Find(FadeObjectName), Is.Null, "フェードアウト前のフェードインでは作らない");

                await transition.FadeOutAsync(0f, Color.red, CancellationToken.None);
                var root = GameObject.Find(FadeObjectName);
                Assert.That(root, Is.Not.Null);
                var group = root.GetComponentInChildren<CanvasGroup>();
                Assert.That(group.alpha, Is.EqualTo(1f));
                Assert.That(group.blocksRaycasts, Is.True, "覆っている間は入力を遮る");

                await transition.FadeInAsync(0f, CancellationToken.None);
                Assert.That(group.alpha, Is.Zero);
                Assert.That(group.blocksRaycasts, Is.False);
            }
            finally
            {
                transition.Dispose();
            }

            await UniTask.Yield();
            Assert.That(GameObject.Find(FadeObjectName), Is.Null);
        });

        [UnityTest]
        public IEnumerator FadeTransition_CancelledFadeOut_CompletesWithoutThrowing() => UniTask.ToCoroutine(async () =>
        {
            var transition = new FadeTransition(new SceneSettings());
            try
            {
                var cts = new CancellationTokenSource();
                var fade = transition.FadeOutAsync(5f, Color.black, cts.Token);
                await UniTask.Yield();
                cts.Cancel();

                // キャンセルされたら最後の値まで進めて例外を投げずに戻る (中止の判断は SceneService が ct を見て行う)
                await fade;
                Assert.That(GameObject.Find(FadeObjectName).GetComponentInChildren<CanvasGroup>().alpha, Is.EqualTo(1f));
            }
            finally
            {
                transition.Dispose();
            }
        });

        // ---- 実際の SceneManager を使う追加読み込み / 破棄 ----

        [UnityTest]
        public IEnumerator UnloadAsync_RuntimeCreatedScene_Unloads() => UniTask.ToCoroutine(async () =>
        {
            var service = new SceneService(new SceneSettings(), new NoTransition());
            try
            {
                var name = "AbubuTest_Additive_" + System.Guid.NewGuid().ToString("N");
                SceneManager.CreateScene(name);
                Assert.That(SceneManager.GetSceneByName(name).isLoaded, Is.True);

                Assert.That(await service.UnloadAsync(name), Is.True);
                Assert.That(SceneManager.GetSceneByName(name).isLoaded, Is.False);

                // 読み込まれていないシーンの破棄は何もせず true
                Assert.That(await service.UnloadAsync(name), Is.True);
            }
            finally
            {
                service.Dispose();
            }
        });

        [UnityTest]
        public IEnumerator UnknownScene_IsRejectedWithFalse() => UniTask.ToCoroutine(async () =>
        {
            var service = new SceneService(new SceneSettings(), new NoTransition());
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("\"Abubu_NoSuchScene\" が見つかりません"));
                Assert.That(await service.LoadAdditiveAsync("Abubu_NoSuchScene"), Is.False);
                LogAssert.Expect(LogType.Error, new Regex("\"Abubu_NoSuchScene\" が見つかりません"));
                Assert.That(await service.LoadAsync("Abubu_NoSuchScene"), Is.False);
                Assert.That(service.IsLoading.CurrentValue, Is.False);
                Assert.That(service.CurrentSceneName, Is.EqualTo(SceneManager.GetActiveScene().name));
            }
            finally
            {
                service.Dispose();
            }
        });
    }
}
