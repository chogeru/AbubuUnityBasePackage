using System;
using Abubu.Save;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Audio
{
    /// <summary>
    /// 音量設定の Model (状態のみ)。値は 0〜1。
    /// 保存と復元は <see cref="AudioVolumeStore"/>、AudioMixer への反映は <see cref="AudioMixerController"/>、
    /// AudioSource への反映は各プレイヤーが担当する (<see cref="GetEffective"/> を購読する)。
    /// <code>
    /// [Inject] private IAudioVolumeModel _volume;
    /// _volume.Set(VolumeChannel.Bgm, 0.5f);
    /// _volume.GetEffective(SoundCategory.Bgm).Subscribe(v => Debug.Log(v));
    /// </code>
    /// </summary>
    /// <remarks>独自の音量管理 (サーバー保存のプロフィールと連動など) に差し替える場合はこれを実装してバインドする</remarks>
    public interface IAudioVolumeModel
    {
        ReadOnlyReactiveProperty<bool> Mute { get; }

        /// <summary>チャンネル単体の音量 (Master・ミュートは掛かっていない)</summary>
        ReadOnlyReactiveProperty<float> Get(VolumeChannel channel);

        /// <summary>音量を設定する。値は 0〜1 に丸められる</summary>
        void Set(VolumeChannel channel, float value);

        void SetMute(bool mute);

        /// <summary>Master・カテゴリ音量・ミュートを掛け合わせた最終音量</summary>
        ReadOnlyReactiveProperty<float> GetEffective(SoundCategory category);

        /// <summary>いずれかの値 (音量・ミュート) が変わったときの通知。購読時に現在値は流れない</summary>
        Observable<Unit> Changed { get; }

        /// <summary>現在の値を保存用のデータにする</summary>
        AudioVolumeData ToData();

        /// <summary>保存データの値に差し替える (範囲外の値は 0〜1 に丸める)</summary>
        void Restore(AudioVolumeData data);

        /// <summary>既定値 (<see cref="AudioVolumeDefaults"/>) に戻す</summary>
        void ResetToDefaults();
    }

    /// <summary>
    /// <see cref="IAudioVolumeModel"/> の既定の実装。
    /// 値の変更は Set / SetMute から行う (外部からは読み取り専用)。
    /// </summary>
    /// <remarks>
    /// <para>DI では <see cref="AudioVolumeInstaller"/> が Model と <see cref="AudioVolumeStore"/> (保存) をまとめて登録する。</para>
    /// <para>
    /// DI を使わない場合は ISaveService を受け取るコンストラクタを使うと、保存込み (Store を内部に持つ) で動く。
    /// <code>
    /// using var volume = new AudioVolumeModel(saveService);       // 読み込み済み・変更は自動保存
    /// using var memoryOnly = new AudioVolumeModel();              // 保存しない (テスト・プレビュー用)
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class AudioVolumeModel : IAudioVolumeModel, IDisposable
    {
        /// <summary>既定の保存キー (AudioVolumeSaveOptions.SaveKey の既定値)</summary>
        public const string DefaultSaveKey = "abubu.audio-volume";

        /// <summary>互換用。既定の保存キー。実際に使われるキーは <see cref="CurrentSaveKey"/></summary>
        public const string SaveKey = DefaultSaveKey;

        private readonly ReactiveProperty<float> _master;
        private readonly ReactiveProperty<float> _bgm;
        private readonly ReactiveProperty<float> _se;
        private readonly ReactiveProperty<float> _ambient;
        private readonly ReactiveProperty<float> _voice;
        private readonly ReactiveProperty<bool> _mute;
        private readonly AudioVolumeDefaults _defaults;
        /// <summary>ISaveService を受け取るコンストラクタで作った Store (Dispose で一緒に破棄する)</summary>
        private readonly AudioVolumeStore _ownedStore;
        /// <summary>この Model を保存している Store (Save / Reload の転送先)。無ければ null</summary>
        private AudioVolumeStore _store;
        private bool _disposed;

        public ReadOnlyReactiveProperty<float> Master => _master;
        public ReadOnlyReactiveProperty<float> Bgm => _bgm;
        public ReadOnlyReactiveProperty<float> Se => _se;
        public ReadOnlyReactiveProperty<float> Ambient => _ambient;
        public ReadOnlyReactiveProperty<float> Voice => _voice;
        public ReadOnlyReactiveProperty<bool> Mute => _mute;

        /// <summary>Master・カテゴリ音量・ミュートを掛け合わせた最終音量</summary>
        public ReadOnlyReactiveProperty<float> EffectiveBgm { get; }
        public ReadOnlyReactiveProperty<float> EffectiveSe { get; }
        public ReadOnlyReactiveProperty<float> EffectiveAmbient { get; }
        public ReadOnlyReactiveProperty<float> EffectiveVoice { get; }

        public Observable<Unit> Changed { get; }

        /// <summary>セーブが無いときの初期音量</summary>
        public AudioVolumeDefaults Defaults => _defaults;

        /// <summary>実際に使われている保存キー (保存していなければ <see cref="DefaultSaveKey"/>)</summary>
        public string CurrentSaveKey => _store != null ? _store.SaveKey : DefaultSaveKey;

        /// <summary>保存しない Model を作る (保存は <see cref="AudioVolumeStore"/> を別に用意する)</summary>
        /// <param name="defaults">初期音量 (省略時は AudioVolumeDefaults の既定値)</param>
        [Inject]
        public AudioVolumeModel([InjectOptional] AudioVolumeDefaults defaults = null)
        {
            _defaults = defaults ?? new AudioVolumeDefaults();

            _master = new ReactiveProperty<float>(Mathf.Clamp01(_defaults.Master));
            _bgm = new ReactiveProperty<float>(Mathf.Clamp01(_defaults.Bgm));
            _se = new ReactiveProperty<float>(Mathf.Clamp01(_defaults.Se));
            _ambient = new ReactiveProperty<float>(Mathf.Clamp01(_defaults.Ambient));
            _voice = new ReactiveProperty<float>(Mathf.Clamp01(_defaults.Voice));
            _mute = new ReactiveProperty<bool>(false);

            EffectiveBgm = Combine(_bgm);
            EffectiveSe = Combine(_se);
            EffectiveAmbient = Combine(_ambient);
            EffectiveVoice = Combine(_voice);

            // 購読時に流れる現在値は各ストリームで Skip(1) して、変更だけを拾う
            Changed = Observable.Merge(
                _master.Skip(1).AsUnitObservable(), _bgm.Skip(1).AsUnitObservable(), _se.Skip(1).AsUnitObservable(),
                _ambient.Skip(1).AsUnitObservable(), _voice.Skip(1).AsUnitObservable(), _mute.Skip(1).AsUnitObservable());
        }

        /// <summary>
        /// 保存込みの Model を作る (DI を使わない場合の簡易用)。生成時に保存先から読み込み、以降の変更は自動で保存される。
        /// Dispose で保存待ちの変更を書き出す。
        /// </summary>
        /// <param name="save">保存先</param>
        /// <param name="defaults">セーブが無いときの初期音量 (省略時は AudioVolumeDefaults の既定値)</param>
        /// <param name="saveOptions">保存キーや保存までの待ち時間 (省略時は既定値)</param>
        public AudioVolumeModel(ISaveService save, AudioVolumeDefaults defaults = null, AudioVolumeSaveOptions saveOptions = null)
            : this(defaults)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            _ownedStore = new AudioVolumeStore(this, save, saveOptions);
            _ownedStore.Initialize();
        }

        public ReadOnlyReactiveProperty<float> Get(VolumeChannel channel) => GetProperty(channel);

        /// <summary>音量を設定する。値は 0〜1 に丸められる。Dispose 後は何もしない</summary>
        public void Set(VolumeChannel channel, float value)
        {
            if (_disposed) return;
            GetProperty(channel).Value = Mathf.Clamp01(value);
        }

        /// <summary>ミュートを設定する。Dispose 後は何もしない</summary>
        public void SetMute(bool mute)
        {
            if (_disposed) return;
            _mute.Value = mute;
        }

        public ReadOnlyReactiveProperty<float> GetEffective(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => EffectiveBgm,
            SoundCategory.Se => EffectiveSe,
            SoundCategory.Ambient => EffectiveAmbient,
            SoundCategory.Voice => EffectiveVoice,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        /// <summary>カテゴリ単体の音量 (Master・ミュートは掛かっていない)</summary>
        public ReadOnlyReactiveProperty<float> GetCategoryVolume(SoundCategory category) => Get(ToChannel(category));

        public static VolumeChannel ToChannel(SoundCategory category) => category switch
        {
            SoundCategory.Bgm => VolumeChannel.Bgm,
            SoundCategory.Se => VolumeChannel.Se,
            SoundCategory.Ambient => VolumeChannel.Ambient,
            SoundCategory.Voice => VolumeChannel.Voice,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        public AudioVolumeData ToData() => new()
        {
            Master = _master.Value, Bgm = _bgm.Value, Se = _se.Value, Ambient = _ambient.Value, Voice = _voice.Value, Mute = _mute.Value,
        };

        public void Restore(AudioVolumeData data)
        {
            if (_disposed) return;
            data ??= new AudioVolumeData();
            _master.Value = Mathf.Clamp01(data.Master);
            _bgm.Value = Mathf.Clamp01(data.Bgm);
            _se.Value = Mathf.Clamp01(data.Se);
            _ambient.Value = Mathf.Clamp01(data.Ambient);
            _voice.Value = Mathf.Clamp01(data.Voice);
            _mute.Value = data.Mute;
        }

        public void ResetToDefaults() => Restore(new AudioVolumeData
        {
            Master = _defaults.Master, Bgm = _defaults.Bgm, Se = _defaults.Se, Ambient = _defaults.Ambient, Voice = _defaults.Voice,
        });

        /// <summary>
        /// 保存先から読み直して値を差し替える。セーブが無ければ既定値に戻す。
        /// セーブスロットの切り替えやクラウドセーブの取得後 (IBootTask など) に呼ぶ。
        /// 保存担当 (<see cref="AudioVolumeStore"/>) が無ければ何もしない。
        /// </summary>
        /// <remarks><see cref="AudioVolumeStore.Reload"/> と同じ</remarks>
        public void Reload()
        {
            if (!_disposed) _store?.Reload();
        }

        /// <summary>今の値をすぐに保存する。保存担当 (<see cref="AudioVolumeStore"/>) が無ければ何もしない</summary>
        /// <remarks><see cref="AudioVolumeStore.Save"/> と同じ</remarks>
        public void Save()
        {
            if (!_disposed) _store?.Save();
        }

        /// <summary>Store が自分を保存担当として登録する (最初の 1 つだけ。Save / Reload の転送先になる)</summary>
        internal void AttachStore(AudioVolumeStore store)
        {
            if (_store == null && !_disposed) _store = store;
        }

        internal void DetachStore(AudioVolumeStore store)
        {
            if (_store == store) _store = null;
        }

        private ReactiveProperty<float> GetProperty(VolumeChannel channel) => channel switch
        {
            VolumeChannel.Master => _master,
            VolumeChannel.Bgm => _bgm,
            VolumeChannel.Se => _se,
            VolumeChannel.Ambient => _ambient,
            VolumeChannel.Voice => _voice,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
        };

        private ReadOnlyReactiveProperty<float> Combine(ReactiveProperty<float> category) =>
            Observable.CombineLatest(_master, category, _mute, static (m, c, mute) => mute ? 0f : Mathf.Clamp01(m) * Mathf.Clamp01(c))
                .ToReadOnlyReactiveProperty();

        public void Dispose()
        {
            if (_disposed) return;
            // 値を破棄する前に、保存待ちの変更を書き出す (Store の Dispose が後になっても失われないように)
            _store?.Flush();
            _ownedStore?.Dispose();
            _disposed = true;
            _store = null;

            EffectiveBgm.Dispose();
            EffectiveSe.Dispose();
            EffectiveAmbient.Dispose();
            EffectiveVoice.Dispose();
            _master.Dispose();
            _bgm.Dispose();
            _se.Dispose();
            _ambient.Dispose();
            _voice.Dispose();
            _mute.Dispose();
        }
    }

    [Serializable]
    public sealed class AudioVolumeData
    {
        public float Master = 1f;
        public float Bgm = 1f;
        public float Se = 1f;
        public float Ambient = 1f;
        public float Voice = 1f;
        public bool Mute;
    }

    [Serializable]
    public sealed class AudioVolumeDefaults
    {
        [Range(0f, 1f)] public float Master = 1f;
        [Range(0f, 1f)] public float Bgm = 0.7f;
        [Range(0f, 1f)] public float Se = 1f;
        [Range(0f, 1f)] public float Ambient = 0.8f;
        [Range(0f, 1f)] public float Voice = 1f;
    }

    /// <summary>音量設定の保存方法</summary>
    [Serializable]
    public sealed class AudioVolumeSaveOptions
    {
        [Tooltip("ISaveService に保存するキー。複数の音量設定を持つ場合や既存セーブと揃えたい場合に変更する")]
        public string SaveKey = AudioVolumeModel.DefaultSaveKey;

        [Tooltip("最後の変更からこの秒数 (timeScale の影響なし) 待って保存する。0 なら変更のたびに即保存")]
        [Min(0f)] public float SaveDebounceSeconds = 0.5f;
    }
}
