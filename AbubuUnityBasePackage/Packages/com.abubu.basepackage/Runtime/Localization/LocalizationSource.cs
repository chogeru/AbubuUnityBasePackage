using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abubu.Localization
{
    /// <summary>
    /// 翻訳文字列の取得元。既定は <see cref="LocalizationTable"/> (ScriptableObject + CSV) だが、
    /// Unity Localization・サーバー配信・独自形式などに差し替えられる。
    /// <code>
    /// // AbubuInstaller より前の Installer で
    /// Container.Bind&lt;ILocalizationSource&gt;().To&lt;MyLocalizationSource&gt;().AsSingle();
    /// </code>
    /// 読み込みに時間がかかる取得元 (サーバー・Addressables) は <see cref="IAsyncLocalizationSource"/> も実装する。
    /// </summary>
    public interface ILocalizationSource
    {
        /// <summary>選べる言語コード。先頭は「どの言語にも合わないとき」の既定として使われる</summary>
        IReadOnlyList<string> Languages { get; }

        /// <summary>指定言語のキー → 文字列。言語の切り替え時に 1 回呼ばれる。無ければ空の辞書を返す</summary>
        IReadOnlyDictionary<string, string> Load(string language);
    }

    /// <summary>
    /// 非同期で読み込む取得元。実装されていれば <see cref="ILocalizationService"/> は Load の代わりにこちらを使い、
    /// 読み込みが終わってから言語を切り替える (その間 IsLoading が true)。
    /// </summary>
    public interface IAsyncLocalizationSource : ILocalizationSource
    {
        UniTask<IReadOnlyDictionary<string, string>> LoadAsync(string language, CancellationToken ct);
    }

    /// <summary>
    /// 言語ごとのアセット (画像・音声など) の取得元。<see cref="ILocalizationSource"/> と同じオブジェクトが実装していれば使われる。
    /// </summary>
    public interface ILocalizedAssetSource
    {
        /// <summary>指定言語・キーのアセット。無ければ false</summary>
        bool TryGetAsset(string language, string key, out Object asset);
    }

    /// <summary>非同期で読み込むアセットの取得元 (Addressables など)</summary>
    public interface IAsyncLocalizedAssetSource
    {
        /// <summary>指定言語・キーのアセット。無ければ null</summary>
        UniTask<Object> LoadAssetAsync(string language, string key, CancellationToken ct);
    }

    /// <summary>
    /// 「ILocalizationSource は既定の AbubuLocalizationTable のもの」という目印。
    /// 任意モジュール (Unity Localization 連携など) は、これがバインドされているときだけ取得元を差し替える (利用者が自前でバインドしたものは差し替えない)
    /// </summary>
    public sealed class DefaultLocalizationSourceMarker
    {
    }

    /// <summary><see cref="LocalizationTable"/> を取得元にする既定の実装 (文字列とアセットの両方)</summary>
    public sealed class TableLocalizationSource : ILocalizationSource, ILocalizedAssetSource
    {
        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

        private readonly Dictionary<string, Dictionary<string, string>> _tables;
        private readonly Dictionary<string, Dictionary<string, Object>> _assets;

        public IReadOnlyList<string> Languages { get; }

        public TableLocalizationSource(AbubuLocalizationTable table)
        {
            if (table == null)
            {
                _tables = new Dictionary<string, Dictionary<string, string>>();
                _assets = new Dictionary<string, Dictionary<string, Object>>();
                Languages = new string[0];
                return;
            }

            _tables = table.Build();
            _assets = table.BuildAssets();
            Languages = table.GetAllLanguages().ToArray();
        }

        public IReadOnlyDictionary<string, string> Load(string language) =>
            language != null && _tables.TryGetValue(language, out var values) ? values : Empty;

        public bool TryGetAsset(string language, string key, out Object asset)
        {
            asset = null;
            return language != null && key != null && _assets.TryGetValue(language, out var values) && values.TryGetValue(key, out asset) && asset != null;
        }
    }
}
