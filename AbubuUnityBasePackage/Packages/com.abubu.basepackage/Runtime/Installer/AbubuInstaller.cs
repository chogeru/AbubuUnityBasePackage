using Abubu.App;
using Abubu.Audio;
using Abubu.Boot;
using Abubu.Events;
using Abubu.DisplaySettings;
using Abubu.Localization;
using Abubu.Pause;
using Abubu.Effects;
using Abubu.Installers;
using Abubu.Pool;
using Abubu.Save;
using Abubu.SceneFlow;
using System;
using UnityEngine;
using Zenject;

namespace Abubu
{
    /// <summary>
    /// ProjectContext に置く Installer。AbubuSettings.Modules で有効な機能を、機能ごとの Installer (Abubu.Installers) で順にバインドする。
    /// 独自の Installer から使う場合は AbubuInstaller.Install(Container, settings) を呼ぶ。
    /// 1 つの機能だけを使いたい場合は SaveInstaller.Install(Container, saveSettings) のように個別に呼べる。
    /// 主なサービス (ISaveService / ISceneService / IPauseService / ISoundService / ILocalizationService など) と
    /// 差し替え可能な部品 (ISaveStorage / ISaveSerializer / ISceneTransition / ILocalizationSource) は、すでにバインドされていれば登録しない。
    /// 差し替えたい場合は AbubuInstaller より前に実行される Installer で先にバインドする。
    /// </summary>
    public sealed class AbubuInstaller : MonoInstaller, IAbubuProjectInstaller
    {
        [SerializeField] private AbubuSettings settings;

        public override void InstallBindings() => Install(Container, settings);

        public static void Install(DiContainer container, AbubuSettings settings)
        {
            if (settings == null)
            {
                Debug.LogWarning("[Abubu] AbubuSettings が未設定のため既定値で起動します。Tools > Abubu > Setup Project を実行してください。");
                settings = ScriptableObject.CreateInstance<AbubuSettings>();
                settings.hideFlags = HideFlags.DontSave;
            }

            // ログは他のサービスの生成中に出るものも対象にしたいので最初に反映する
            AbubuLog.Configure(settings.Log);

            var modules = settings.Modules ?? new AbubuModules();
            container.BindInstance(settings).IfNotBound();
            // 任意モジュールがインストール中に AbubuSettings を Resolve せずに判定できるよう、目印として登録する
            if (settings.Integration.LocalizePackageUi) container.BindInstance(new PackageUiLocalizationEnabled());
            BindModuleSettings(container, settings.ModuleSettings);

            // --- 機能ごとの登録 (Save / Pause は他の機能が使うので常に登録する) ---
            PauseInstaller.Install(container, settings.Pause);
            SaveInstaller.Install(container, settings.Save);
            if (modules.Sound) SoundInstaller.Install(container, settings.Sound);
            if (modules.Pool) PoolInstaller.Install(container);
            if (modules.Pool && modules.Effects) EffectsInstaller.Install(container, settings.Effects);
            if (modules.Scene) SceneInstaller.Install(container, settings.Scene);
            if (modules.AppLifecycle) AppInstaller.Install(container, settings.AppLifecycle);
            if (modules.Localization) LocalizationInstaller.Install(container, settings.Localization);
            if (modules.Graphics) GraphicsInstaller.Install(container, settings.Graphics);
            if (modules.Events) EventsInstaller.Install(container);

            // --- 機能同士の連携 (両方が有効なときだけ) ---
            if (modules.Sound) container.BindInterfacesTo<AudioPauseBridge>().AsSingle();
            if (modules.Sound && modules.Pool && modules.Effects) container.Bind<IEffectPlayHook>().To<EffectSeHook>().AsSingle();
            if (modules.Scene && modules.Pool) container.BindInterfacesTo<PoolSceneBridge>().AsSingle();
            if (modules.Scene && settings.Integration.ResumePauseOnSceneChange) container.BindInterfacesTo<PauseSceneBridge>().AsSingle();
            if (modules.Localization)
            {
                if (modules.Boot && modules.Scene) container.Bind<IBootTask>().To<LocalizationBootTask>().AsSingle();
                // パッケージ自身の文言 (画面設定・ダイアログ・キー割り当て) の翻訳は、各連携モジュール (Abubu.*.Localization) が
                // PackageUiLocalizationEnabled を見て登録する
                if (modules.Sound) container.BindInterfacesTo<VoiceSubtitleBridge>().AsSingle().NonLazy();
            }
            if (modules.AppLifecycle)
            {
                container.BindInterfacesTo<AppFocusBridge>().AsSingle();
                if (settings.AppLifecycle.SaveSettingsOnSuspend) container.Bind<IAppSuspendHandler>().To<SettingsSaveOnSuspend>().AsSingle();
                // 中断時の保存がすべて終わってから、バックグラウンドの書き込みを終わらせる
                container.Bind<IAppSuspendFinalizer>().To<SaveFlushOnSuspend>().AsSingle();
            }

            // --- 任意モジュール (UI / Input / MessagePipe / Addressables など。導入されていれば自動で有効) ---
            if (modules.AutoInstallModules) InstallModules(container, modules.ExcludedModules);

            // --- Boot (他サービスの Initialize がすべて終わってから起動処理を走らせる) ---
            if (modules.Boot && modules.Scene) BootInstaller.Install(container, settings.Boot);
        }

        /// <summary>
        /// 任意モジュールの設定アセットを型ごとに登録する。Installer が任意モジュールの型を知らなくても渡せるようにするため
        /// (同じ型がすでにバインドされていれば、先に登録されたものを優先する)
        /// </summary>
        private static void BindModuleSettings(DiContainer container, System.Collections.Generic.List<ScriptableObject> moduleSettings)
        {
            if (moduleSettings == null) return;
            foreach (var asset in moduleSettings)
            {
                if (asset == null || container.HasBinding(asset.GetType())) continue;
                container.Bind(asset.GetType()).FromInstance(asset);
            }
        }

        private static void InstallModules(DiContainer container, string[] excluded)
        {
            var excludedSet = new System.Collections.Generic.HashSet<string>(excluded ?? Array.Empty<string>());
            // 同じ属性が 2 か所に書かれていても、Installer は 1 回だけ実行する (AsSingle の二重登録を防ぐ)
            var installed = new System.Collections.Generic.HashSet<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // 数百あるアセンブリ全部の属性を読むと起動が遅くなるため、Abubu.Core を参照しているもの (= Abubu のモジュールか、
                // 利用者が作ったモジュール) だけを対象にする。参照の一覧はメタデータなので読むのは軽い
                if (assembly.IsDynamic) continue;
                var name = assembly.GetName().Name;
                if (excludedSet.Contains(name) || !IsModuleCandidate(assembly, name)) continue;
                foreach (AbubuModuleInstallerAttribute attribute in assembly.GetCustomAttributes(typeof(AbubuModuleInstallerAttribute), false))
                {
                    if (!installed.Add(attribute.InstallerType)) continue;
                    if (Activator.CreateInstance(attribute.InstallerType) is IAbubuModuleInstaller installer)
                    {
                        installer.Install(container);
                    }
                }
            }
        }

        private const string CoreAssemblyName = "Abubu.Core";

        private static bool IsModuleCandidate(System.Reflection.Assembly assembly, string name)
        {
            if (name.StartsWith("Abubu.", StringComparison.Ordinal)) return true;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name == CoreAssemblyName) return true;
            }
            return false;
        }
    }
}
