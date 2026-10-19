using System;
using Abubu.Save;
using R3;
using Zenject;

namespace Abubu.DisplaySettings
{
    /// <summary>
    /// <see cref="GraphicsSettingsModel"/> の保存と復元 (<see cref="ISaveService"/> 経由)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Initialize で保存データを Model に復元し、以降の変更を <see cref="GraphicsSettingsOptions.SaveDebounceSeconds"/> 秒まとめてから保存する。
    /// 待ち時間は Time.timeScale の影響を受けない (ポーズ中に変えた設定も保存される)。
    /// </para>
    /// <para>保存されるのは明示的に設定された項目だけ (<see cref="GraphicsSettingsData.ExplicitMask"/>)。</para>
    /// </remarks>
    public class GraphicsSettingsStore : IInitializable, IDisposable
    {
        private readonly GraphicsSettingsModel _model;
        private readonly ISaveService _save;
        private readonly GraphicsSettingsOptions _options;
        private readonly CompositeDisposable _disposables = new();
        private bool _initialized;
        private bool _dirty;

        /// <param name="options">保存キーなど。null なら Model の構成 (<see cref="GraphicsSettingsModel.Options"/>) を使う</param>
        public GraphicsSettingsStore(GraphicsSettingsModel model, ISaveService save, GraphicsSettingsOptions options = null)
        {
            _model = model;
            _save = save;
            _options = options ?? model.Options;
        }

        /// <summary>デバウンスに使う時間。既定はタイムスケールを無視する Update タイミング</summary>
        protected virtual TimeProvider DebounceTimeProvider => UnityTimeProvider.UpdateIgnoreTimeScale;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            if (!_options.Persist) return;

            if (_save.TryLoad<GraphicsSettingsData>(_options.SaveKey, out var data)) _model.Restore(data);

            // 復元による通知は購読前に済んでいるので、ここからはユーザーの変更だけを拾う
            _model.Changed
                .Do(this, static (_, self) => self._dirty = true)
                .Debounce(TimeSpan.FromSeconds(Math.Max(0f, _options.SaveDebounceSeconds)), DebounceTimeProvider)
                .Subscribe(this, static (_, self) => self.Save())
                .AddTo(_disposables);

            _model.SaveRequested.Subscribe(this, static (_, self) => self.Save()).AddTo(_disposables);
        }

        /// <summary>今の状態をすぐに保存する。何も明示的に設定されていなければ保存しない</summary>
        public void Save()
        {
            if (!_options.Persist) return;
            var data = _model.ToData();
            if (data.ExplicitMask == 0) return;
            _dirty = false;
            _save.Save(_options.SaveKey, data);
        }

        public virtual void Dispose()
        {
            _disposables.Dispose();
            // デバウンス待ちの変更があれば書き出しておく
            if (_dirty) Save();
        }
    }
}
