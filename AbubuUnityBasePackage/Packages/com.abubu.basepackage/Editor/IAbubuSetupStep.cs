namespace Abubu.Editor
{
    /// <summary>
    /// Tools > Abubu > Setup Project の追加の処理。実装したクラス (引数なしのコンストラクタ) は自動で見つかり、Setup Project の最後に実行される。
    /// 任意モジュール (Input など) の設定アセットを、そのモジュールが入っているときだけ作るために使う。
    /// <code>
    /// public sealed class MySetupStep : IAbubuSetupStep
    /// {
    ///     public int Order => 0;
    ///     public void Run(AbubuSetupContext context) => context.AddModuleSettings(CreateMySettings(context.Folder));
    /// }
    /// </code>
    /// </summary>
    /// <remarks>何度実行しても結果が変わらない (冪等) ように書くこと。既存のアセットは上書きしない。</remarks>
    public interface IAbubuSetupStep
    {
        /// <summary>小さい順に実行される</summary>
        int Order { get; }

        void Run(AbubuSetupContext context);
    }

    /// <summary>Setup Project の情報 (<see cref="IAbubuSetupStep"/> に渡される)</summary>
    public sealed class AbubuSetupContext
    {
        /// <summary>設定アセットを作るフォルダ (Project Settings > Abubu > Setup Folder)</summary>
        public string Folder { get; }

        public AbubuSettings Settings { get; }

        public AbubuSetupContext(string folder, AbubuSettings settings)
        {
            Folder = folder;
            Settings = settings;
        }

        /// <summary>AbubuSettings > Module Settings に追加する (すでにあれば何もしない)</summary>
        public void AddModuleSettings(UnityEngine.ScriptableObject asset)
        {
            if (asset == null || Settings.ModuleSettings.Contains(asset)) return;
            Settings.ModuleSettings.Add(asset);
            UnityEditor.EditorUtility.SetDirty(Settings);
        }
    }
}
