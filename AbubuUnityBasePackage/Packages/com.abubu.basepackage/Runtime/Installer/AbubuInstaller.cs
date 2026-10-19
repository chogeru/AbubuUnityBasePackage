using Abubu.App;
using Abubu.Audio;
using Abubu.Boot;
using Abubu.Events;
using Abubu.Graphics;
using Abubu.Localization;
using Abubu.Pause;
using Abubu.Effects;
using Abubu.Installers;
using Abubu.Pool;
using Abubu.Presentation;
using Abubu.Save;
using Abubu.Scene;
using Abubu.UI;
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
            if (settings.Ui != null) container.BindInstance(settings.Ui).IfNotBound();
            BindModuleSettings(container, settings.ModuleSettings);

            // --- 機能ごとの登録 (Save / Pause は他の機能が使うので常に登録する) ---
            PauseInstaller.Install(container, settings.Pause);
            SaveInstaller.Install(container, settings.Save);
            if (modules.Sound) SoundInstaller.Install(container, settings.Sound);
            if (modules.Effects) EffectsInstaller.Install(container, settings.Effect);
            if (modules.Scene) SceneInstaller.Install(container, settings.Scene);
            if (modules.AppLifecycle) AppInstaller.Install(container, settings.App);
            if (modules.Localization) LocalizationInstaller.Install(container, settings.Localization);
            if (modules.Graphics) GraphicsInstaller.Install(container, settings.GraphicsOptions);
            if (modules.Events) EventsInstaller.Install(container);

            // --- 機能同士の連携 (両方が有効なときだけ) ---
            if (modules.Sound) container.BindInterfacesTo<AudioPauseBridge>().AsSingle();
            if (modules.Sound && modules.Effects) container.Bind<IEffectPlayHook>().To<EffectSeHook>().AsSingle();
            if (modules.Scene && modules.Effects) container.BindInterfacesTo<PoolSceneBridge>().AsSingle();
            if (modules.Scene && settings.Integration.ResumePauseOnSceneChange) container.BindInterfacesTo<PauseSceneBridge>().AsSingle();
            if (modules.Localization)
            {
                if (modules.Boot && modules.Scene) container.Bind<IBootTask>().To<LocalizationBootTask>().AsSingle();
                if (modules.Graphics && settings.Integration.LocalizePackageUi && !container.HasBinding<IGraphicsSettingsLabels>())
                {
                    container.Bind<IGraphicsSettingsLabels>().To<LocalizedGraphicsSettingsLabels>().AsSingle();
                }
                if (settings.Integration.LocalizePackageUi && !container.HasBinding<IDialogLabels>())
                {
                    container.Bind<IDialogLabels>().To<LocalizedDialogLabels>().AsSingle();
                }
                if (modules.Sound) container.BindInterfacesTo<VoiceSubtitleBridge>().AsSingle().NonLazy();
            }
            if (modules.AppLifecycle)
            {
                container.BindInterfacesTo<AppFocusBridge>().AsSingle();
                if (settings.App.SaveSettingsOnSuspend) container.Bind<IAppSuspendHandler>().To<SettingsSaveOnSuspend>().AsSingle();
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
                // 数百あるアセンブリ全部の属性を読むと起動が遅くなるため、Abubu.* だけを対象にする
                if (assembly.IsDynamic) continue;
                var name = assembly.GetName().Name;
                if (!name.StartsWith("Abubu.", StringComparison.Ordinal) || excludedSet.Contains(name)) continue;
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
    }
}
