using System;
using Abubu.Audio;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace Abubu.Assets
{
    /// <summary>
    /// SoundEntry.Address の AudioClip を Addressables (IAssetService) から必要なときに読み込む IAudioClipProvider。
    /// SoundSettings.UseAddressableClips をオンにすると AddressablesModuleInstaller が登録する (設定 1 つで切り替わる)。
    /// <code>
    /// // AbubuSettings の Sound > Use Addressable Clips をオンにし、SoundEntry の Address にアドレスを書くだけ
    /// await sound.PreloadAsync(SoundCategory.Bgm, new[] { "boss" }); // 任意: 先読み
    /// sound.Bgm.PlayAsync("boss").Forget();
    /// sound.Unload(SoundCategory.Bgm, new[] { "boss" });
    /// </code>
    /// - 参照カウント・読み込みの共有・先読みは <see cref="AddressAudioClipProviderBase"/> の仕様どおり。
    /// - アドレスごとに AssetScope.Global で読み込み、参照が 0 になった時点で IAssetService.Release する。
    ///   IAssetService.ReleaseGlobal を呼ぶと、ここで保持している Clip も解放されるので注意。
    /// - IAssetService.PreloadAsync などで読み込み済みのアドレスも、そのまま (同期的に) 取得できる。
    /// </summary>
    public sealed class AddressablesAudioClipProvider : AddressAudioClipProviderBase
    {
        private readonly IAssetService _assets;

        [Preserve]
        public AddressablesAudioClipProvider(IAssetService assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        }

        protected override UniTask<AudioClip> LoadClipAsync(string address) => _assets.LoadAsync<AudioClip>(address, AssetScope.Global);

        protected override void ReleaseClip(string address, AudioClip clip) => _assets.Release<AudioClip>(address);
    }
}
