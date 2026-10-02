namespace Abubu.Audio
{
    public sealed class SoundService : ISoundService
    {
        public IBgmPlayer Bgm { get; }
        public ISePlayer Se { get; }
        public IAmbientPlayer Ambient { get; }
        public IVoicePlayer Voice { get; }
        public AudioVolumeModel Volume { get; }

        public SoundService(IBgmPlayer bgm, ISePlayer se, IAmbientPlayer ambient, IVoicePlayer voice, AudioVolumeModel volume)
        {
            Bgm = bgm;
            Se = se;
            Ambient = ambient;
            Voice = voice;
            Volume = volume;
        }
    }
}
