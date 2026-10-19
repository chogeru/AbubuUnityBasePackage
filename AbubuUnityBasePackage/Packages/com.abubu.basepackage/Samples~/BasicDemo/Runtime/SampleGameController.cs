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
        [Tooltip("Build Settings 未登録でもエディタで遷移できるよう、タイトルシーンのパスを保持する (サンプル生成時に設定される)")]
        [SerializeField] private string titleScenePath;

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
                // プール機能が無効 (AbubuSettings.Modules) または未初期化の場合は null が返る
                if (cube == null)
                {
                    Refresh("プールが利用できません (AbubuSettings でプール機能が有効か確認してください)");
                    return;
                }
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

        public void Reload()
        {
            // Build Settings に登録済みなら通常のリロード (payload も引き継がれる)。未登録ならエディタ上でパス指定で読み直す
            if (Application.CanStreamedLevelBeLoaded(SceneName))
            {
                Scenes.Reload();
                return;
            }

            var path = SampleScenes.Resolve(SceneName, gameObject.scene.path);
            if (Scenes.TryGetPayload<SampleGamePayload>(out var payload)) Scenes.LoadWith(path, payload);
            else Scenes.Load(path);
        }
        public void BackToTitle() => Scenes.Load(SampleScenes.Resolve(SampleTitleController.SceneName, titleScenePath));

        private void Refresh(string message)
        {
            if (info != null) info.text = $"{message}\nScore (セーブ済み): {_score}";
        }
    }
}
