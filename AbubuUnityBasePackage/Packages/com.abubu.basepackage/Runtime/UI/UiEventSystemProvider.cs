using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Zenject;
using Object = UnityEngine.Object;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace Abubu.UI
{
    /// <summary>
    /// EventSystem の作り方。Input System を使う場合は Abubu.UI.Input が InputSystemUIInputModule 付きのものを作る実装をバインドする
    /// </summary>
    public interface IUiEventSystemFactory
    {
        EventSystem Create();
    }

    /// <summary>
    /// ポップアップ・画面・トーストを出すときに EventSystem が無ければ、シーンをまたいで残る EventSystem を用意する (AbubuUiSettings.EnsureEventSystem)。
    /// シーンに EventSystem があればそちらを優先し、用意したものは止める (2 つ同時に動かない)。
    /// </summary>
    public sealed class UiEventSystemProvider : IDisposable
    {
        private readonly AbubuUiSettings _settings;
        private readonly IUiEventSystemFactory _factory;
        private EventSystem _owned;
        private bool _warned;

        public UiEventSystemProvider([InjectOptional] AbubuUiSettings settings = null, [InjectOptional] IUiEventSystemFactory factory = null)
        {
            _settings = settings != null ? settings : AbubuUiSettings.Default;
            _factory = factory;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        /// <summary>動いている EventSystem が無ければ用意する</summary>
        public void Ensure()
        {
            if (HasOtherEventSystem()) return;
            if (_owned != null)
            {
                _owned.gameObject.SetActive(true);
                return;
            }

            if (!_settings.EnsureEventSystem)
            {
                WarnOnce("シーンに EventSystem が無いため、UI のボタンは押せません。GameObject > UI > Event System を追加するか、AbubuUiSettings > Ensure Event System をオンにしてください。");
                return;
            }

            _owned = _factory != null ? _factory.Create() : CreateDefault();
            if (_owned == null) return;
            _owned.gameObject.name = "[Abubu.EventSystem]";
            Object.DontDestroyOnLoad(_owned.gameObject);
        }

        /// <summary>Input System が無いプロジェクト向け (旧 Input Manager の StandaloneInputModule)</summary>
        private EventSystem CreateDefault()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return go.GetComponent<EventSystem>();
#else
            WarnOnce("EventSystem を用意できません (旧 Input Manager が無効で、Input System 用の連携 Abubu.UI.Input も見つかりません)。シーンに EventSystem を置いてください。");
            return null;
#endif
        }

        private bool HasOtherEventSystem()
        {
            foreach (var eventSystem in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (eventSystem != _owned && eventSystem.isActiveAndEnabled) return true;
            }
            return false;
        }

        // シーンに EventSystem があるなら、用意したものは止める (2 つ動いていると Unity が警告し、入力が二重に処理される)
        private void OnSceneLoaded(UnityScene scene, LoadSceneMode mode)
        {
            if (_owned != null && HasOtherEventSystem()) _owned.gameObject.SetActive(false);
        }

        // シーンの EventSystem が無くなったら、用意したものを動かし直す
        private void OnSceneUnloaded(UnityScene scene)
        {
            if (_owned != null && !_owned.gameObject.activeSelf && !HasOtherEventSystem()) _owned.gameObject.SetActive(true);
        }

        private void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("[Abubu.UI] " + message);
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (_owned != null) Object.Destroy(_owned.gameObject);
        }
    }
}
