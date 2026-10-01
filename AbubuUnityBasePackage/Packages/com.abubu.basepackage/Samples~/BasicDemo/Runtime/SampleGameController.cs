using System;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Samples
{
    /// <summary>ゲーム側サンプル。payload 受け取り・プール・イベント・セーブを確認する</summary>
    public sealed class SampleGameController : MonoBehaviour
    {
        public const string SceneName = "AbubuSampleGame";
        private const string ScoreKey = "sample.score";

        [SerializeField] private Text info;
        [SerializeField] private GameObject cubePrefab;

        private int _score;

        private void Start()
        {
            Sound.PlayBgm("bgm_b");
            _score = Saves.Load(ScoreKey, 0);

            // イベントバス: 購読は AddTo(this) で GameObject の寿命に合わせる
            GameEvents.Receive<SampleScored>()
                .Subscribe(this, static (e, self) => self.Refresh($"イベント受信: Total = {e.Total}"))
                .AddTo(this);

            Refresh(Scenes.TryGetPayload<SampleGamePayload>(out var payload)
                ? $"Payload: {payload.Message} ({payload.StartedAt})"
                : "Payload なし (このシーンから直接再生した)");
        }

        /// <summary>プールからキューブを出し、2 秒後に自動で返却する</summary>
        public void SpawnCubes()
        {
            if (cubePrefab == null) return;
            for (var i = 0; i < 10; i++)
            {
                var position = new Vector3(UnityEngine.Random.Range(-3f, 3f), 5f + i * 0.5f, UnityEngine.Random.Range(-1f, 1f));
                var cube = Pools.Rent(cubePrefab, position, UnityEngine.Random.rotation);
                if (cube.TryGetComponent<Rigidbody>(out var body))
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                Pools.ReturnAfter(cube, 2f);
            }
            Sound.PlaySe("jump");
        }

        public void AddScore()
        {
            _score++;
            Saves.Save(ScoreKey, _score);
            Sound.PlaySe("coin");
            GameEvents.Publish(new SampleScored(_score));
        }

        public void ResetScore()
        {
            _score = 0;
            Saves.Delete(ScoreKey);
            Refresh("スコアをリセットしました");
        }

        public void Reload() => Scenes.Reload();
        public void BackToTitle() => Scenes.Load(SampleTitleController.SceneName);

        private void Refresh(string message)
        {
            if (info != null) info.text = $"{message}\nScore (セーブ済み): {_score}";
        }
    }
}
