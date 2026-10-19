using Zenject;

namespace Abubu.Audio
{
    /// <summary>
    /// 音量設定 (Model / Store) をまとめてコンテナに登録する。
    /// <code>
    /// AudioVolumeInstaller.Install(Container, settings.DefaultVolumes, settings.VolumeSave);
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 登録されるもの: <see cref="AudioVolumeModel"/> (<see cref="IAudioVolumeModel"/> と自身の型で引ける) と
    /// <see cref="AudioVolumeStore"/> (保存。ISaveService が必要)。
    /// AudioMixer / AudioSource への反映は SoundInstaller が登録する各プレイヤーと <see cref="AudioMixerController"/> が行う。
    /// </para>
    /// <para>すでに同じ型がバインド済みなら、その項目はスキップする (独自実装への差し替え用)。</para>
    /// <para>保存したくない場合 (体験版・テストなど) は <paramref name="persist"/> を false にする。</para>
    /// </remarks>
    public static class AudioVolumeInstaller
    {
        /// <param name="container">登録先のコンテナ</param>
        /// <param name="defaults">セーブが無いときの初期音量。null なら既定値</param>
        /// <param name="saveOptions">保存キーや保存までの待ち時間。null なら既定値</param>
        /// <param name="persist">false なら <see cref="AudioVolumeStore"/> を登録しない (保存しない)</param>
        public static void Install(DiContainer container, AudioVolumeDefaults defaults = null, AudioVolumeSaveOptions saveOptions = null,
            bool persist = true)
        {
            if (!container.HasBinding<IAudioVolumeModel>() && !container.HasBinding<AudioVolumeModel>())
            {
                // IAudioVolumeModel / AudioVolumeModel / IDisposable で引ける 1 つのインスタンス
                container.BindInterfacesAndSelfTo<AudioVolumeModel>().AsSingle().WithArguments(defaults ?? new AudioVolumeDefaults());
            }
            if (persist && !container.HasBinding<AudioVolumeStore>())
            {
                container.BindInterfacesAndSelfTo<AudioVolumeStore>().AsSingle().WithArguments(saveOptions ?? new AudioVolumeSaveOptions()).NonLazy();
                // 保存データの復元を、音量を使う側 (AudioMixerController など) の Initialize より先に行う
                container.BindInitializableExecutionOrder<AudioVolumeStore>(-100);
            }
        }
    }
}
