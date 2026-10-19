using System;
using Abubu.Save;
using R3;
using Zenject;

namespace Abubu.Audio
{
    /// <summary>
    /// <see cref="IAudioVolumeModel"/> の保存と復元 (<see cref="ISaveService"/> 経由)。
    /// <code>
    /// [Inject] private AudioVolumeStore _volumeStore;
    /// _volumeStore.Reload(); // セーブスロット切り替え・クラウド同期の後に読み直す
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Initialize で保存データを Model に復元し (無ければ既定値のまま)、以降の変更を
    /// <see cref="AudioVolumeSaveOptions.SaveDebounceSeconds"/> 秒まとめてから保存する。
    /// 待ち時間は Time.timeScale の影響を受けない (ポーズ中に変えた音量も保存される)。
    /// </para>
    /// <para>Dispose 時に保存待ちの変更があれば書き出す。</para>
    /// <para>
    /// Model が <see cref="AudioVolumeModel"/> なら、<see cref="AudioVolumeModel.Save"/> / <see cref="AudioVolumeModel.Reload"/> はこの Store に転送される。
    /// </para>
    /// </remarks>
    public class AudioVolumeStore : IInitializable, IDisposable
    {
        private readonly IAudioVolumeModel _model;
        private readonly ISaveService _save;
        private readonly float _debounceSeconds;
        private IDisposable _subscription;
        private bool _initialized;
        private bool _restoring;
        private bool _dirty;
        private bool _disposed;

        /// <param name="model">保存する Model</param>
        /// <param name="save">保存先</param>
        /// <param name="options">保存キーや保存までの待ち時間。null なら既定値</param>
        public AudioVolumeStore(IAudioVolumeModel model, ISaveService save, [InjectOptional] AudioVolumeSaveOptions options = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            options ??= new AudioVolumeSaveOptions();
            SaveKey = string.IsNullOrEmpty(options.SaveKey) ? AudioVolumeModel.DefaultSaveKey : options.SaveKey;
            _debounceSeconds = Math.Max(0f, options.SaveDebounceSeconds);
            if (model is AudioVolumeModel concrete) concrete.AttachStore(this);
        }

        /// <summary>実際に使われている保存キー</summary>
        public string SaveKey { get; }

        /// <summary>保存待ちの変更があるか</summary>
        public bool IsDirty => _dirty;

        /// <summary>デバウンスに使う時間。既定はタイムスケールを無視する Update タイミング</summary>
        protected virtual TimeProvider DebounceTimeProvider => UnityTimeProvider.UpdateIgnoreTimeScale;

        public void Initialize()
        {
            if (_initialized || _disposed) return;
            _initialized = true;
            Load();

            // 復元による変更は _restoring で除外し、ユーザーの変更だけを拾う
            var changes = _model.Changed
                .Where(this, static (_, self) => !self._restoring)
                .Do(this, static (_, self) => self._dirty = true);
            _subscription = (_debounceSeconds > 0f
                    ? changes.Debounce(TimeSpan.FromSeconds(_debounceSeconds), DebounceTimeProvider)
                    : changes)
                .Subscribe(this, static (_, self) => self.Save());
        }

        /// <summary>
        /// 保存先から読み直して Model の値を差し替える。セーブが無ければ既定値に戻す。
        /// セーブスロットの切り替えやクラウドセーブの取得後 (IBootTask など) に呼ぶ。保存待ちの変更は破棄される
        /// </summary>
        public void Reload()
        {
            if (_disposed) return;
            _dirty = false;
            Load();
        }

        /// <summary>今の値をすぐに保存する</summary>
        public void Save()
        {
            if (_disposed) return;
            _dirty = false;
            _save.Save(SaveKey, _model.ToData());
        }

        /// <summary>保存待ちの変更があれば今すぐ保存する (中断・終了時用)</summary>
        public void Flush()
        {
            if (_dirty) Save();
        }

        private void Load()
        {
            _restoring = true;
            try
            {
                if (_save.TryLoad<AudioVolumeData>(SaveKey, out var data) && data != null) _model.Restore(data);
                else _model.ResetToDefaults();
            }
            finally
            {
                _restoring = false;
            }
        }

        public virtual void Dispose()
        {
            if (_disposed) return;
            _subscription?.Dispose();
            _subscription = null;
            // デバウンス待ちの変更があれば書き出しておく
            Flush();
            _disposed = true;
            if (_model is AudioVolumeModel concrete) concrete.DetachStore(this);
        }
    }
}
