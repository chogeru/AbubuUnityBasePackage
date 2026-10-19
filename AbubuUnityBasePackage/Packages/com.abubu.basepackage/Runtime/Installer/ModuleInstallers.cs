using System;
using Abubu.App;
using Abubu.Audio;
using Abubu.Boot;
using Abubu.Effects;
using Abubu.Events;
using Abubu.Graphics;
using Abubu.Localization;
using Abubu.Pause;
using Abubu.Pool;
using Abubu.Presentation;
using Abubu.Save;
using Abubu.Scene;
using Zenject;

namespace Abubu.Installers
{
    // 機能ごとの Installer。AbubuInstaller はこれらを順に呼ぶだけで、1 つの機能だけを自前の Installer から使うこともできる。
    // <code>
    // SaveInstaller.Install(Container, saveSettings);
    // PauseInstaller.Install(Container, new PauseSettings());
    // </code>
    // どれも、メインのインターフェイスがすでにバインドされていれば登録しない (自前の実装を先にバインドすれば差し替えられる)。
    // モジュール同士の連携 (ポーズ時に音を止める など) は AbubuInstaller の結線 (AbubuBridges) で行う。

    /// <summary>IPauseService</summary>
    public static class PauseInstaller
    {
        public static void Install(DiContainer container, PauseSettings settings)
        {
            container.BindInstance(settings ?? new PauseSettings()).IfNotBound();
            if (!container.HasBinding<IPauseService>()) container.BindInterfacesTo<PauseService>().AsSingle();
        }
    }

    /// <summary>ISaveService (+ 差し替え可能な ISaveStorage / ISaveSerializer)</summary>
    public static class SaveInstaller
    {
        public static void Install(DiContainer container, SaveSettings settings)
        {
            settings ??= new SaveSettings();
            container.BindInstance(settings).IfNotBound();
            container.Bind<ISaveStorage>().FromMethod(() => SaveService.CreateStorage(settings)).AsSingle().IfNotBound();
            container.Bind<ISaveSerializer>().FromMethod(() => new JsonUtilitySaveSerializer(settings.PrettyPrint)).AsSingle().IfNotBound();
            if (settings.Compress && !container.HasBinding<ISaveTransform>()) container.Bind<ISaveTransform>().To<GZipSaveTransform>().AsSingle();
            // ISaveService + IAsyncSaveService + ISaveFailureNotifier
            if (!container.HasBinding<ISaveService>()) container.BindInterfacesTo<SaveService>().AsSingle();
            container.BindInstance(settings.Slots ?? new SaveSlotOptions()).IfNotBound();
            if (!container.HasBinding<ISaveSlotService>()) container.Bind<ISaveSlotService>().To<SaveSlotService>().AsSingle();
        }
    }

    /// <summary>ISoundService (BGM / SE / 環境音 / ボイス / 音量)。ISaveService が必要</summary>
    public static class SoundInstaller
    {
        public static void Install(DiContainer container, SoundSettings settings)
        {
            settings ??= new SoundSettings();
            container.BindInstance(settings).IfNotBound();
            if (container.HasBinding<ISoundService>()) return;

            container.Bind<AudioVolumeModel>().AsSingle().WithArguments(settings.DefaultVolumes, settings.VolumeSave);
            container.BindInterfacesAndSelfTo<AudioMixerController>().AsSingle();
            container.BindInterfacesAndSelfTo<AudioRoot>().AsSingle();
            container.BindInterfacesTo<BgmPlayer>().AsSingle();
            container.BindInterfacesTo<SePlayer>().AsSingle();
            container.BindInterfacesTo<AmbientPlayer>().AsSingle();
            container.BindInterfacesTo<VoicePlayer>().AsSingle();
            container.Bind<IAudioMixerSnapshotService>().To<AudioMixerSnapshotService>().AsSingle().IfNotBound();
            container.Bind<ISoundService>().To<SoundService>().AsSingle();
        }
    }

    /// <summary>IPoolService と IEffectService</summary>
    public static class EffectsInstaller
    {
        public static void Install(DiContainer container, EffectSettings settings)
        {
            container.BindInstance(settings ?? new EffectSettings()).IfNotBound();
            if (!container.HasBinding<IPoolService>()) container.BindInterfacesTo<PoolService>().AsSingle();
            if (!container.HasBinding<IEffectService>()) container.BindInterfacesTo<EffectService>().AsSingle();
        }
    }

    /// <summary>ISceneService (+ 差し替え可能な ISceneTransition) とロード画面</summary>
    public static class SceneInstaller
    {
        public static void Install(DiContainer container, SceneSettings settings)
        {
            container.BindInstance(settings ?? new SceneSettings()).IfNotBound();
            // FadeTransition は最初のフェードまで GameObject を作らないので、差し替えられていなければ IDisposable としても登録して確実に破棄する
            if (!container.HasBinding<ISceneTransition>()) container.Bind(typeof(ISceneTransition), typeof(IDisposable)).To<FadeTransition>().AsSingle();
            if (container.HasBinding<ISceneService>()) return;
            container.BindInterfacesTo<SceneService>().AsSingle();
            container.BindInterfacesTo<SceneLoadingScreen>().AsSingle();
        }
    }

    /// <summary>IBootService (起動処理)。ISceneService が必要</summary>
    public static class BootInstaller
    {
        public static void Install(DiContainer container, BootSettings settings)
        {
            container.BindInstance(settings ?? new BootSettings()).IfNotBound();
            if (container.HasBinding<IBootService>()) return;
            container.BindInterfacesTo<BootService>().AsSingle().NonLazy();
            // バインド順ではなく実行順を明示する: 他サービスの Initialize がすべて終わってから起動処理を走らせる
            container.BindInitializableExecutionOrder<BootService>(int.MaxValue);
        }
    }

    /// <summary>画面設定 (解像度・画質・FPS など) の Model / 反映 / 保存。ISaveService が必要</summary>
    public static class GraphicsInstaller
    {
        public static void Install(DiContainer container, GraphicsSettingsOptions options = null) =>
            GraphicsSettingsInstaller.Install(container, options);
    }

    /// <summary>IEventBus</summary>
    public static class EventsInstaller
    {
        public static void Install(DiContainer container)
        {
            if (!container.HasBinding<IEventBus>()) container.BindInterfacesTo<EventBus>().AsSingle();
        }
    }

    /// <summary>IAppLifecycle (中断・復帰・終了の通知)</summary>
    public static class AppInstaller
    {
        public static void Install(DiContainer container, AppSettings settings)
        {
            container.BindInstance(settings ?? new AppSettings()).IfNotBound();
            if (!container.HasBinding<IAppLifecycle>()) container.BindInterfacesTo<AppLifecycleService>().AsSingle().NonLazy();
        }
    }

    /// <summary>ILocalizationService (+ 差し替え可能な ILocalizationSource)。ISaveService があれば選んだ言語を保存する</summary>
    public static class LocalizationInstaller
    {
        public static void Install(DiContainer container, LocalizationSettings settings)
        {
            settings ??= new LocalizationSettings();
            container.BindInstance(settings).IfNotBound();
            if (!container.HasBinding<ILocalizationSource>())
            {
                container.Bind<ILocalizationSource>().FromMethod(() => new TableLocalizationSource(settings.Table)).AsSingle();
                container.BindInstance(new DefaultLocalizationSourceMarker());
            }
            if (!container.HasBinding<ILocalizationService>()) container.BindInterfacesTo<LocalizationService>().AsSingle();
            if (!container.HasBinding<ISubtitleService>()) container.BindInterfacesTo<SubtitleService>().AsSingle();
        }
    }
}
