using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Components
{
    /// <summary>
    /// このオブジェクトが表示されている間ゲームをポーズする。ポーズメニューのパネルに付けるだけで良い。
    /// 複数のパネルが重なっても、全部閉じるまで再開しない。
    /// </summary>
    [AddComponentMenu("Abubu/Pause While Enabled")]
    public sealed class PauseWhileEnabled : MonoBehaviour
    {
        private void OnEnable() => GamePause.Pause(this);
        private void OnDisable() => GamePause.Resume(this);
    }
}
