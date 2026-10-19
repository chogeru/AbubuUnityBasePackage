namespace Abubu
{
    /// <summary>
    /// 「パッケージ自身の文言 (画面設定の選択肢・ダイアログのボタン・キー割り当て画面) をローカライズする」の目印。
    /// AbubuInstaller が AbubuSettings.Integration.LocalizePackageUi に従って登録する。
    /// 連携モジュールの Installer は、インストール中に設定を Resolve せず HasBinding でこれを見る
    /// </summary>
    public sealed class PackageUiLocalizationEnabled
    {
    }
}
