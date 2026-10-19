using UnityEngine;

namespace Abubu.Localization
{
    /// <summary>
    /// AudioSource に付けると、Key に対応する言語ごとの AudioClip (LocalizationTable の Assets) を設定する。
    /// ボイスやナレーションの差し替えに使う。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Abubu/Localized Audio")]
    public sealed class LocalizedAudio : LocalizedBehaviour
    {
        [SerializeField] private string key;

        [Tooltip("言語が切り替わって Clip が変わったら、再生中なら新しい Clip で再生し直す")]
        [SerializeField] private bool restartIfPlaying = true;

        public string Key
        {
            get => key;
            set
            {
                key = value;
                Refresh();
            }
        }

        public override void Refresh()
        {
            var service = Service;
            if (service == null || string.IsNullOrEmpty(key)) return;

            var clip = service.GetAsset<AudioClip>(key);
            var source = GetComponent<AudioSource>();
            if (clip == null || source.clip == clip) return;

            var wasPlaying = source.isPlaying;
            source.clip = clip;
            if (wasPlaying && restartIfPlaying) source.Play();
        }
    }
}
