using System.Collections.Generic;
using System.Linq;

namespace Abubu.Localization
{
    /// <summary>
    /// 翻訳文字列の取得元。既定は <see cref="LocalizationTable"/> (ScriptableObject + CSV) だが、
    /// Unity Localization・サーバー配信・独自形式などに差し替えられる。
    /// <code>
    /// // AbubuInstaller より前の Installer で
    /// Container.Bind&lt;ILocalizationSource&gt;().To&lt;MyLocalizationSource&gt;().AsSingle();
    /// </code>
    /// </summary>
    public interface ILocalizationSource
    {
        /// <summary>選べる言語コード。先頭は「どの言語にも合わないとき」の既定として使われる</summary>
        IReadOnlyList<string> Languages { get; }

        /// <summary>指定言語のキー → 文字列。言語の切り替え時に 1 回呼ばれる。無ければ空の辞書を返す</summary>
        IReadOnlyDictionary<string, string> Load(string language);
    }

    /// <summary><see cref="LocalizationTable"/> を取得元にする既定の実装</summary>
    public sealed class TableLocalizationSource : ILocalizationSource
    {
        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

        private readonly Dictionary<string, Dictionary<string, string>> _tables;

        public IReadOnlyList<string> Languages { get; }

        public TableLocalizationSource(LocalizationTable table)
        {
            if (table == null)
            {
                _tables = new Dictionary<string, Dictionary<string, string>>();
                Languages = new string[0];
                return;
            }

            _tables = table.Build();
            Languages = table.GetAllLanguages().ToArray();
        }

        public IReadOnlyDictionary<string, string> Load(string language) =>
            language != null && _tables.TryGetValue(language, out var values) ? values : Empty;
    }
}
