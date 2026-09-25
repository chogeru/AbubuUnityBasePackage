using System;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Samples
{
    /// <summary>SampleTitle → SampleGame に渡すデータ</summary>
    [Serializable]
    public sealed class SampleGamePayload
    {
        public string Message;
        public string StartedAt;
    }

    /// <summary>イベントバスの動作確認用メッセージ</summary>
    public readonly struct SampleScored
    {
        public readonly int Total;
        public SampleScored(int total) => Total = total;
    }

    /// <summary>
    /// タイトル側サンプル。ボタンから各機能を呼ぶだけ (static ショートカットのみで書いている)。
    /// Tools > Abubu > Create Sample Scenes で生成されるシーンに配置される。
    /// </summary>
    public sealed class SampleTitleController : MonoBehaviour
    {
        public const string SceneName = "AbubuSampleTitle";

        [SerializeField] private Text status;
        [SerializeField] private GameObject volumePanel;
        [SerializeField] private GameObject graphicsPanel;

        private void Start()
        {
            Sound.PlayBgm("bgm_a");
            SetStatus("BGM: bgm_a");
        }

        public void PlayBgmA() { Sound.PlayBgm("bgm_a"); SetStatus("BGM: bgm_a (クロスフェード)"); }
        public void PlayBgmB() { Sound.PlayBgm("bgm_b"); SetStatus("BGM: bgm_b (クロスフェード)"); }
        public void StopBgm() { Sound.StopBgm(); SetStatus("BGM 停止"); }

        public void PlayJump() => Sound.PlaySe("jump");
        public void PlayCoin() => Sound.PlaySe("coin");

        /// <summary>連打して SE プール・同時発音数制限・クールダウンを確認する</summary>
        public void PlayCoinBurst()
        {
            for (var i = 0; i < 40; i++) Sound.PlaySe("coin");
            SetStatus("coin x40 (クールダウン 0.05 秒で間引かれる)");
        }

        public void ToggleWind() => ToggleAmbient("wind");
        public void ToggleRain() => ToggleAmbient("rain");

        public void PlayEffect()
        {
            var cam = Camera.main;
            var origin = cam != null ? cam.transform.position + cam.transform.forward * 8f : Vector3.zero;
            var position = origin + new Vector3(UnityEngine.Random.Range(-4f, 4f), UnityEngine.Random.Range(-2f, 2f), 0f);
            Fx.Play("burst", position);
            SetStatus("Effect: burst (SE 同時再生・自動返却)");
        }

        public void ToggleVolumePanel() => Toggle(volumePanel);
        public void ToggleGraphicsPanel() => Toggle(graphicsPanel);

        public void GoToGame() => Scenes.LoadWith(SampleGameController.SceneName, new SampleGamePayload
        {
            Message = "Title から渡されたデータです",
            StartedAt = DateTime.Now.ToString("HH:mm:ss"),
        });

        private void ToggleAmbient(string key)
        {
            var ambient = Sound.Service?.Ambient;
            if (ambient == null) return;

            if (ambient.IsPlaying(key)) Sound.StopAmbient(key);
            else Sound.PlayAmbient(key);
            SetStatus($"Ambient: {key} {(ambient.IsPlaying(key) ? "ON" : "OFF")}");
        }

        private static void Toggle(GameObject go)
        {
            if (go != null) go.SetActive(!go.activeSelf);
        }

        private void SetStatus(string message)
        {
            if (status != null) status.text = message;
        }
    }

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
