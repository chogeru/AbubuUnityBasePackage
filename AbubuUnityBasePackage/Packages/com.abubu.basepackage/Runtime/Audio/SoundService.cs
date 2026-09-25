namespace Abubu.Audio
{
    public sealed class SoundService : ISoundService
    {
        public IBgmPlayer Bgm { get; }
        public ISePlayer Se { get; }
        public IAmbientPlayer Ambient { get; }
        public AudioVolumeModel Volume { get; }

        public SoundService(IBgmPlayer bgm, ISePlayer se, IAmbientPlayer ambient, AudioVolumeModel volume)
        {
            Bgm = bgm;
            Se = se;
            Ambient = ambient;
            Volume = volume;
        }
    }
}
