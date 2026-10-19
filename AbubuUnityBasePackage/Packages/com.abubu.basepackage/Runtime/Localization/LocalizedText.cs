using System;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Abubu.Localization
{
    /// <summary>
    /// Text / TextMeshPro に付けると、Key の翻訳を表示し、言語が変わったら自動で書き換える。
    /// それ以外の表示先 (UI Toolkit・独自コンポーネントなど) は On Changed に登録すれば翻訳済みの文字列が渡される。
    /// <code>
    /// GetComponent&lt;LocalizedText&gt;().SetArguments(score);   // "スコア: {0}" の {0} に入る
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Abubu/Localized Text")]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string key;

        [Tooltip("翻訳した文字列を受け取る (Text / TextMeshPro 以外に表示したいとき)")]
        [SerializeField] private UnityEvent<string> onChanged = new();

        private object[] _args;
        private IDisposable _subscription;
        private Text _text;
        private TMP_Text _tmp;

        public string Key
        {
            get => key;
            set
            {
                key = value;
                Refresh();
            }
        }

        /// <summary>書式の {0} {1} ... に入れる値を設定して表示を更新する</summary>
        public void SetArguments(params object[] args)
        {
            _args = args;
            Refresh();
        }

        private void Awake()
        {
            _text = GetComponent<Text>();
            _tmp = GetComponent<TMP_Text>();
            if (_text == null && _tmp == null && onChanged.GetPersistentEventCount() == 0) Debug.LogWarning($"[Abubu.Localization] {name} に Text / TextMeshPro が無く、On Changed も未設定です。", this);
        }

        private void OnEnable()
        {
            var service = Loc.Service;
            if (service == null) return;
            // 購読した時点の値も流れてくるので、最初の表示もここで行われる
            _subscription = service.Language.Subscribe(this, static (_, self) => self.Refresh());
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <summary>現在の言語で表示し直す</summary>
        public void Refresh()
        {
            if (string.IsNullOrEmpty(key)) return;
            var service = Loc.Service;
            if (service == null) return;

            var value = _args is { Length: > 0 } ? service.Get(key, _args) : service.Get(key);
            if (_tmp != null) _tmp.text = value;
            else if (_text != null) _text.text = value;
            onChanged.Invoke(value);
        }
    }
}
