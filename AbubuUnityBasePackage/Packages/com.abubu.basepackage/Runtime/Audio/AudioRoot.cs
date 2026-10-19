using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Abubu.Audio
{
    /// <summary>
    /// 全 AudioSource をぶら下げる DontDestroyOnLoad な GameObject。
    /// シーンを跨いでも BGM / 環境音が途切れない。
    /// GameObject は最初に AudioSource が必要になったときに作られる (音を使わないなら何も生成しない)。
    /// Clip の取り出し方 (<see cref="IAudioClipProvider"/>) もここで保持し、各プレイヤーが共有する。
    /// <code>
    /// var root = new AudioRoot(settings);                         // 直接参照 (DirectAudioClipProvider)
    /// var root = new AudioRoot(settings, new MyClipProvider());   // 独自の読み込み方
    /// </code>
    /// </summary>
    public sealed class AudioRoot : IDisposable
    {
        private readonly SoundSettings _settings;
        private readonly SoundLibrary _library;
        /// <summary>Library 未設定時に自前で作った空のライブラリ (Dispose で破棄する)</summary>
        private readonly bool _ownsLibrary;
        private Transform _transform;
        private bool _disposed;

        /// <summary>AudioSource の親。初回アクセス時に生成される</summary>
        public Transform Transform
        {
            get
            {
                if (_transform == null && !_disposed)
                {
                    var go = new GameObject("[Abubu.Audio]");
                    if (Application.isPlaying) Object.DontDestroyOnLoad(go); // EditMode テストでは不要 (呼ぶとエラーになる)
                    _transform = go.transform;
                }
                return _transform;
            }
        }

        public SoundLibrary Library => _library;
        public SoundSettings Settings => _settings;
        /// <summary>Clip の取り出し方。DI で IAudioClipProvider が登録されていなければ DirectAudioClipProvider</summary>
        public IAudioClipProvider Clips { get; }

        /// <param name="clips">null なら DirectAudioClipProvider (SoundEntry.Clips の直接参照)</param>
        public AudioRoot(SoundSettings settings, IAudioClipProvider clips = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Clips = clips ?? DirectAudioClipProvider.Instance;
            if (settings.Library != null)
            {
                _library = settings.Library;
            }
            else
            {
                _library = ScriptableObject.CreateInstance<SoundLibrary>();
                _library.hideFlags = HideFlags.DontSave;
                _ownsLibrary = true;
            }
        }

        /// <summary>
        /// カテゴリ用の AudioSource を作る。SoundSettings.SourceTemplates にひな形があれば複製し、無ければ既定値で生成する。
        /// </summary>
        public AudioSource CreateSource(string name, SoundCategory category)
        {
            var parent = Transform;
            var template = _settings.GetSourceTemplate(category);
            AudioSource source;
            if (template != null)
            {
                var go = Object.Instantiate(template.gameObject, parent, false);
                go.name = name;
                source = go.GetComponent<AudioSource>();
                // ひな形に Clip / PlayOnAwake が残っていても勝手に鳴らさない
                source.Stop();
                source.clip = null;
                go.transform.localPosition = Vector3.zero;
            }
            else
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                source = go.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            if (source.outputAudioMixerGroup == null) source.outputAudioMixerGroup = _settings.GetMixerGroup(category);
            return source;
        }

        public bool TryResolve(SoundCategory category, string key, out SoundEntry entry)
        {
            if (!string.IsNullOrEmpty(key) && _library.TryGet(category, key, out entry) && entry.HasSource) return true;

            var hint = KeySuggestion.Hint(key, _library.GetKeys(category));
            Debug.LogWarning($"[Abubu.Audio] {category} \"{key}\" が SoundLibrary に見つからないか、Clip (Address) が未設定です。{hint}");
            entry = null;
            return false;
        }

        /// <summary>PlayMode では Destroy、EditMode (テスト等) では DestroyImmediate で破棄する</summary>
        internal static void DestroyObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_transform != null) DestroyObject(_transform.gameObject);
            _transform = null;
            if (_ownsLibrary) DestroyObject(_library);
        }
    }
}
