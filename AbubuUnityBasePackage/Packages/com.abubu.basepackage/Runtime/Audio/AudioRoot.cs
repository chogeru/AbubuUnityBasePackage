using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Abubu.Audio
{
    /// <summary>
    /// 全 AudioSource をぶら下げる DontDestroyOnLoad な GameObject。
    /// シーンを跨いでも BGM / 環境音が途切れない。
    /// </summary>
    public sealed class AudioRoot : IDisposable
    {
        private readonly SoundSettings _settings;
        private readonly SoundLibrary _library;

        public Transform Transform { get; }

        public AudioRoot(SoundSettings settings)
        {
            _settings = settings;
            _library = settings.Library != null ? settings.Library : ScriptableObject.CreateInstance<SoundLibrary>();

            var go = new GameObject("[Abubu.Audio]");
            Object.DontDestroyOnLoad(go);
            Transform = go.transform;
        }

        public AudioSource CreateSource(string name, SoundCategory category)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = _settings.GetMixerGroup(category);
            return source;
        }

        public bool TryResolve(SoundCategory category, string key, out SoundEntry entry)
        {
            if (!string.IsNullOrEmpty(key) && _library.TryGet(category, key, out entry) && entry.HasClip) return true;

            Debug.LogWarning($"[Abubu.Audio] {category} \"{key}\" が SoundLibrary に見つからないか、Clip が未設定です。");
            entry = null;
            return false;
        }

        public void Dispose()
        {
            if (Transform != null) Object.Destroy(Transform.gameObject);
        }
    }
}
