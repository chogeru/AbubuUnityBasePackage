using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace Abubu.Localization
{
    /// <summary>
    /// 言語ごとのアセットへの参照。Inspector でキーを指定し、現在の言語のアセットを取り出す。
    /// <code>
    /// [SerializeField] private LocalizedAssetKey&lt;AudioClip&gt; narration;
    /// _audio.PlayOneShot(narration.Get());
    /// </code>
    /// </summary>
    [Serializable]
    public struct LocalizedAssetKey<T> where T : Object
    {
        public string Key;

        public LocalizedAssetKey(string key) => Key = key;

        /// <summary>現在の言語のアセット (無ければ null)</summary>
        public T Get() => Get(Loc.Service);

        public T Get(ILocalizationService service) => service?.GetAsset<T>(Key);

        /// <summary>非同期で読み込む取得元 (Addressables など) 用</summary>
        public UniTask<T> GetAsync(ILocalizationService service, CancellationToken ct = default) =>
            service != null ? service.GetAssetAsync<T>(Key, ct) : UniTask.FromResult<T>(null);
    }
}
