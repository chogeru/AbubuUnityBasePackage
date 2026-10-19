using UnityEngine;

namespace Abubu.App
{
    /// <summary>OnApplicationPause を <see cref="AppLifecycleService"/> に中継する (MonoBehaviour でしか受け取れないため)</summary>
    [AddComponentMenu("")]
    public sealed class AppLifecycleRelay : MonoBehaviour
    {
        private AppLifecycleService _service;

        internal void Initialize(AppLifecycleService service) => _service = service;

        private void OnApplicationPause(bool paused) => _service?.NotifyPause(paused);
    }
}
