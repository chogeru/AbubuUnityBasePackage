using Abubu.Audio;
using Abubu.Boot;
using Abubu.Events;
using Abubu.Graphics;
using Abubu.Effects;
using Abubu.Pool;
using Abubu.Presentation;
using Abubu.Save;
using Abubu.Scene;
using System;
using UnityEngine;
using Zenject;

namespace Abubu
{
    /// <summary>
    /// ProjectContext に置く Installer。パッケージの全サービスをバインドする。
    /// 独自の Installer から使う場合は AbubuInstaller.Install(Container, settings) を呼ぶ。
    /// 差し替え可能な既定実装 (ISaveStorage / ISaveSerializer / ISceneTransition) は IfNotBound で登録しているので、
    /// 差し替えたい場合は AbubuInstaller より前に実行される Installer で先にバインドする。
    /// </summary>
    public sealed class AbubuInstaller : MonoInstaller
    {
        [SerializeField] private AbubuSettings settings;

        public override void InstallBindings() => Install(Container, settings);

        public static void Install(DiContainer container, AbubuSettings settings)
        {
            if (settings == null)
            {
                Debug.LogWarning("[Abubu] AbubuSettings が未設定のため既定値で起動します。Tools > Abubu > Setup Project を実行してください。");
                settings = ScriptableObject.CreateInstance<AbubuSettings>();
            }

            container.BindInstance(settings);
            container.BindInstance(settings.Boot);
            container.BindInstance(settings.Sound);
            container.BindInstance(settings.Scene);
            container.BindInstance(settings.Effect);
            container.BindInstance(settings.Save);

            // --- Save ---
            container.Bind<ISaveStorage>().FromMethod(() => SaveService.CreateStorage(settings.Save)).AsSingle().IfNotBound();
            container.Bind<ISaveSerializer>().FromMethod(() => new JsonUtilitySaveSerializer(settings.Save.PrettyPrint)).AsSingle().IfNotBound();
            container.Bind<ISaveService>().To<SaveService>().AsSingle();

            // --- Sound ---
            container.Bind<AudioVolumeModel>().AsSingle().WithArguments(settings.Sound.DefaultVolumes);
            container.BindInterfacesAndSelfTo<AudioMixerController>().AsSingle();
            container.BindInterfacesAndSelfTo<AudioRoot>().AsSingle();
            container.BindInterfacesTo<BgmPlayer>().AsSingle();
            container.BindInterfacesTo<SePlayer>().AsSingle();
            container.BindInterfacesTo<AmbientPlayer>().AsSingle();
            container.Bind<ISoundService>().To<SoundService>().AsSingle();

            // --- Pool / Effect ---
            container.BindInterfacesTo<PoolService>().AsSingle();
            container.BindInterfacesTo<EffectService>().AsSingle();
            container.Bind<IEffectPlayHook>().To<EffectSeHook>().AsSingle();
            container.BindInterfacesTo<PoolSceneBridge>().AsSingle();

            // --- Scene ---
            // IDisposable として登録しない: 差し替えられた場合に未使用の FadeTransition が生成されないように
            container.Bind<ISceneTransition>().To<FadeTransition>().AsSingle().IfNotBound();
            container.BindInterfacesTo<SceneService>().AsSingle();
            container.BindInterfacesTo<SceneLoadingScreen>().AsSingle();

            // --- Graphics ---
            container.BindInterfacesAndSelfTo<GraphicsSettingsModel>().AsSingle().NonLazy();

            // --- Events ---
            container.BindInterfacesTo<EventBus>().AsSingle();

            // --- 任意モジュール (MessagePipe / Addressables など。導入されていれば自動で有効) ---
            InstallModules(container);

            // --- Boot ---
            container.BindInterfacesTo<BootService>().AsSingle().NonLazy();
            // バインド順ではなく実行順を明示する: 他サービスの Initialize がすべて終わってから起動処理を走らせる
            container.BindInitializableExecutionOrder<BootService>(int.MaxValue);
        }

        private static void InstallModules(DiContainer container)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // 数百あるアセンブリ全部の属性を読むと起動が遅くなるため、Abubu.* だけを対象にする
                if (assembly.IsDynamic || !assembly.GetName().Name.StartsWith("Abubu.", StringComparison.Ordinal)) continue;
                foreach (AbubuModuleInstallerAttribute attribute in assembly.GetCustomAttributes(typeof(AbubuModuleInstallerAttribute), false))
                {
                    if (Activator.CreateInstance(attribute.InstallerType) is IAbubuModuleInstaller installer)
                    {
                        installer.Install(container);
                    }
                }
            }
        }
    }
}
