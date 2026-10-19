using System;
using System.Collections.Generic;
using Abubu.App;
using Abubu.Audio;
using Abubu.Boot;
using Abubu.Effects;
using Abubu.Graphics;
using Abubu.Localization;
using Abubu.Logging;
using Abubu.Pause;
using Abubu.Save;
using Abubu.Scene;
using Abubu.UI;
using UnityEngine;

namespace Abubu
{
    /// <summary>
    /// パッケージ全体の設定。Tools > Abubu > Setup Project で自動生成される。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/Settings", fileName = "AbubuSettings")]
    public sealed class AbubuSettings : ScriptableObject
    {
        [Tooltip("使う機能。使わない機能をオフにすると、サービスもオブジェクトも作られない")]
        public AbubuModules Modules = new();

        [Tooltip("機能同士の連携")]
        public AbubuIntegrationSettings Integration = new();

        public BootSettings Boot = new();
        public SoundSettings Sound = new();
        public SceneSettings Scene = new();
        public EffectSettings Effect = new();
        public SaveSettings Save = new();
        public PauseSettings Pause = new();
        public LocalizationSettings Localization = new();
        public AppSettings App = new();
        public LogSettings Log = new();

        [Tooltip("画面設定の選択肢・保存キー")]
        public GraphicsSettingsOptions GraphicsOptions = new();

        [Tooltip("ポップアップ・画面遷移の設定 (任意)。未設定なら Resources/AbubuUiSettings、それも無ければ既定値を使う")]
        public AbubuUiSettings Ui;

        [Tooltip("任意モジュールの設定アセット (AbubuInputSettings など)。型ごとに DI コンテナへ登録され、各モジュールが受け取る。未設定のモジュールは Resources か既定値を使う")]
        public List<ScriptableObject> ModuleSettings = new();
    }

    /// <summary>
    /// 機能ごとの有効・無効。Save / Pause は他の機能が使うため常に有効。
    /// </summary>
    [Serializable]
    public sealed class AbubuModules
    {
        [Tooltip("サウンド (BGM / SE / 環境音 / ボイス / 音量)")]
        public bool Sound = true;

        [Tooltip("オブジェクトプールとエフェクト")]
        public bool Effects = true;

        [Tooltip("シーン遷移 (フェード / ロード画面)。オフにすると Boot も無効になる")]
        public bool Scene = true;

        [Tooltip("起動処理 (IBootTask の実行と Boot シーン経由の起動)")]
        public bool Boot = true;

        [Tooltip("画面設定 (解像度 / 画質 / FPS / VSync) の Model。保存済みの設定が起動時に反映される")]
        public bool Graphics = true;

        [Tooltip("ローカライズ")]
        public bool Localization = true;

        [Tooltip("アプリの中断・復帰・終了の通知と自動保存")]
        public bool AppLifecycle = true;

        [Tooltip("型登録不要のイベントバス (IEventBus)")]
        public bool Events = true;

        [Tooltip("導入されている任意モジュール (UI / Input / MessagePipe / Addressables など) を自動でインストールする")]
        public bool AutoInstallModules = true;

        [Tooltip("自動インストールしないモジュールのアセンブリ名 (例: Abubu.UI, Abubu.MessagePipe)")]
        public string[] ExcludedModules = Array.Empty<string>();
    }

    /// <summary>
    /// 「パッケージ自身の文言をローカライズする (Integration.LocalizePackageUi)」の目印。
    /// 任意モジュールの Installer は、インストール中に AbubuSettings を Resolve せず HasBinding で判定する
    /// </summary>
    public sealed class PackageUiLocalizationEnabled
    {
    }

    /// <summary>機能同士の連携の設定</summary>
    [Serializable]
    public sealed class AbubuIntegrationSettings
    {
        [Tooltip("シーン遷移時にすべてのポーズを解除する (ポーズメニューごとシーンが破棄されて Resume されないのを防ぐ)。DontDestroyOnLoad 側でポーズを持ち越したい場合はオフにする")]
        public bool ResumePauseOnSceneChange = true;

        [Tooltip("画面設定 UI の選択肢 (表示モード・FPS など) をローカライズの翻訳テーブルから取る (Localization が有効なとき)。View の Labels を自分で編集している場合はオフにする")]
        public bool LocalizePackageUi = true;
    }
}
