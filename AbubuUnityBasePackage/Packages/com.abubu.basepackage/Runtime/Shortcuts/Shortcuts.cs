using System.Threading;
using Abubu.Audio;
using Abubu.Effects;
using Abubu.Events;
using Abubu.Pause;
using Abubu.Pool;
using Abubu.Save;
using Abubu.SceneFlow;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace Abubu
{
    // 共通の方針: どのショートカットも、サービスを取得できない (未セットアップ・ProjectContext に未登録・終了処理中) 場合は
    // 何もせず既定値 (null / false / default) を返す。終了処理中以外は、原因と対処を書いたエラーログを
    // サービスの型ごとに 1 回だけ出す (AbubuServices.ResolveOrLogError)。各クラスの Service プロパティは警告のみ。

    /// <summary>
    /// サウンドの static ショートカット。ゲームジャムで最速で音を鳴らしたい時用。
    /// <code>
    /// Sound.PlaySe("jump");
    /// Sound.PlayBgm("stage1");
    /// var engine = Sound.PlayAttached("engine", transform);
    /// Sound.Snapshot("Paused", 0.3f);
    /// </code>
    /// </summary>
    public static class Sound
    {
        /// <summary>サービス本体。取得できない場合は null (警告を型ごとに 1 回出す)</summary>
        public static ISoundService Service => AbubuServices.TryResolve<ISoundService>();

        /// <summary>ショートカット内部用。取得できない場合は null で、エラーを型ごとに 1 回出す</summary>
        private static ISoundService Svc => AbubuServices.ResolveOrLogError<ISoundService>();

        public static void PlaySe(string key) => Svc?.Se.Play(key);
        public static void PlaySe(string key, Vector3 position) => Svc?.Se.Play(key, position);
        public static void PlaySe(AudioClip clip, float volume = 1f) => Svc?.Se.Play(clip, volume);
        public static void PlayBgm(string key, float? fadeDuration = null) => Svc?.Bgm.PlayAsync(key, fadeDuration).Forget();
        public static void PlayBgm(AudioClip clip, float volume = 1f) => Svc?.Bgm.PlayAsync(clip, volume).Forget();
        public static void StopBgm(float? fadeDuration = null) => Svc?.Bgm.StopAsync(fadeDuration).Forget();
        public static void PlayVoice(string key) => Svc?.Voice.Play(key);
        public static void PlayVoice(string key, Vector3 position) => Svc?.Voice.Play(key, position);
        public static void StopVoice() => Svc?.Voice.Stop();
        public static void PlayAmbient(string key, float? fadeDuration = null) => Svc?.Ambient.PlayAsync(key, fadeDuration).Forget();
        public static void StopAmbient(string key, float? fadeDuration = null) => Svc?.Ambient.StopAsync(key, fadeDuration).Forget();
        public static void StopAllAmbient(float? fadeDuration = null) => Svc?.Ambient.StopAllAsync(fadeDuration).Forget();
        /// <summary>target に追従する SE を鳴らす (target が破棄されたら自動で止まる)。止めるときは StopSe(handle)</summary>
        public static SeHandle PlayAttached(string key, Transform target) => Svc?.Se.PlayAttached(key, target) ?? default;
        public static void StopSe(SeHandle handle) => Svc?.Se.Stop(handle);
        /// <summary>カテゴリの音をすべて止める</summary>
        public static void StopAll(SoundCategory category, float? fadeDuration = null) => Svc?.StopAll(category, fadeDuration);
        /// <summary>AudioMixer のスナップショットを切り替える (ミキサー未設定なら何もしない)</summary>
        public static void Snapshot(string snapshotName, float duration = 0f) => Svc?.Snapshots.TransitionTo(snapshotName, duration);
        /// <summary>カテゴリのサウンドを先読みする (keys が null ならカテゴリ全部)。直接参照 (既定) やサービスが無い場合は即完了</summary>
        public static UniTask PreloadAsync(SoundCategory category, System.Collections.Generic.IReadOnlyList<string> keys = null, CancellationToken ct = default) =>
            Svc?.PreloadAsync(category, keys, ct) ?? UniTask.CompletedTask;
        /// <summary>互換用。<see cref="PreloadAsync"/> を使う</summary>
        [System.Obsolete("Sound.PreloadAsync を使ってください (await できる処理の命名を Async に統一)")]
        public static UniTask Preload(SoundCategory category, System.Collections.Generic.IReadOnlyList<string> keys = null, CancellationToken ct = default) =>
            PreloadAsync(category, keys, ct);
        public static void Unload(SoundCategory category, System.Collections.Generic.IReadOnlyList<string> keys = null) => Svc?.Unload(category, keys);
        /// <summary>ボイスの開始通知 (字幕用)。サービスが無い場合は何も流れない Observable</summary>
        public static Observable<VoiceEvent> OnVoiceStarted => Svc?.Voice?.OnVoiceStarted ?? Observable.Empty<VoiceEvent>();
        /// <summary>ボイスの終了通知 (字幕用)。サービスが無い場合は何も流れない Observable</summary>
        public static Observable<VoiceEvent> OnVoiceEnded => Svc?.Voice?.OnVoiceEnded ?? Observable.Empty<VoiceEvent>();
        /// <summary>
        /// 音量設定。サービスが無い場合は、どこにも反映・保存されない代わりの Model を返す (null にはならない)。
        /// 保存の読み直しは AudioVolumeStore.Reload (または Volume.Reload)
        /// </summary>
        public static AudioVolumeModel Volume => Svc?.Volume ?? FallbackVolume;

        /// <summary>サービスが無いときの Volume。値を変えても音には影響せず、保存もされない</summary>
        private static AudioVolumeModel _fallbackVolume;
        private static AudioVolumeModel FallbackVolume => _fallbackVolume ??= new AudioVolumeModel();
    }

    /// <summary>
    /// シーン遷移の static ショートカット。
    /// <code>
    /// Scenes.Load("Game");
    /// Scenes.LoadWith("Result", new ResultData(score));
    /// if (await Scenes.LoadAsync("Game")) { ... }
    /// </code>
    /// </summary>
    public static class Scenes
    {
        public static ISceneService Service => AbubuServices.TryResolve<ISceneService>();
        private static ISceneService Svc => AbubuServices.ResolveOrLogError<ISceneService>();

        /// <summary>遷移を開始する (完了を待たない)。遷移中などで受け付けられなかった場合はログが出る</summary>
        public static void Load(string sceneName, SceneLoadOptions options = null) => Svc?.LoadAsync(sceneName, options).Forget();
        public static void LoadWith<TPayload>(string sceneName, TPayload payload, SceneLoadOptions options = null) =>
            Svc?.LoadWithPayloadAsync(sceneName, payload, options).Forget();
        public static void Reload(SceneLoadOptions options = null) => Svc?.ReloadAsync(options).Forget();

        /// <summary>遷移して完了まで待つ。戻り値は <see cref="ISceneService.LoadAsync"/> と同じ (サービスが無ければ false)</summary>
        public static UniTask<bool> LoadAsync(string sceneName, SceneLoadOptions options = null, CancellationToken ct = default)
        {
            var service = Svc;
            return service != null ? service.LoadAsync(sceneName, options, ct) : UniTask.FromResult(false);
        }

        public static bool TryGetPayload<T>(out T payload)
        {
            payload = default;
            var service = Svc;
            return service != null && service.TryGetPayload(out payload);
        }
    }

    /// <summary>
    /// エフェクトの static ショートカット。
    /// <code>
    /// Fx.Play("explosion", transform.position);
    /// </code>
    /// </summary>
    public static class Fx
    {
        public static IEffectService Service => AbubuServices.TryResolve<IEffectService>();
        private static IEffectService Svc => AbubuServices.ResolveOrLogError<IEffectService>();

        /// <returns>再生したインスタンス。サービスが無い場合などは null</returns>
        public static GameObject Play(string key, Vector3 position, Quaternion? rotation = null, Transform parent = null) =>
            Svc?.Play(key, position, rotation, parent);
        /// <returns>再生したインスタンス。サービスが無い場合などは null</returns>
        public static GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null, Transform parent = null, float lifetime = 0f) =>
            Svc?.Play(prefab, position, rotation, parent, lifetime);
    }

    /// <summary>
    /// オブジェクトプールの static ショートカット (弾・敵など)。
    /// <code>
    /// var bullet = Pools.Rent(bulletPrefab, pos, rot);
    /// Pools.Return(bullet);
    /// </code>
    /// </summary>
    public static class Pools
    {
        public static IPoolService Service => AbubuServices.TryResolve<IPoolService>();
        private static IPoolService Svc => AbubuServices.ResolveOrLogError<IPoolService>();

        /// <returns>借りたインスタンス。サービスが無い場合は null (エラーログが出る)</returns>
        public static GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null) =>
            Svc?.Rent(prefab, position, rotation, parent);
        /// <returns>借りたインスタンス。サービスが無い場合は null (エラーログが出る)</returns>
        public static T Rent<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component =>
            Svc?.Rent(prefab, position, rotation, parent);
        public static void Return(GameObject instance) => Svc?.Return(instance);
        /// <param name="ignoreTimeScale">true なら Time.timeScale (ポーズ) の影響を受けない</param>
        public static void ReturnAfter(GameObject instance, float seconds, bool ignoreTimeScale = false) =>
            Svc?.ReturnAfter(instance, seconds, ignoreTimeScale);
    }

    /// <summary>
    /// イベントバスの static ショートカット。
    /// <code>
    /// GameEvents.Publish(new EnemyDied(100));
    /// GameEvents.Receive&lt;EnemyDied&gt;().Subscribe(e => ...).AddTo(this);
    /// </code>
    /// </summary>
    public static class GameEvents
    {
        public static IEventBus Service => AbubuServices.TryResolve<IEventBus>();
        private static IEventBus Svc => AbubuServices.ResolveOrLogError<IEventBus>();

        public static void Publish<T>(T message) => Svc?.Publish(message);
        /// <returns>サービスが無い場合は、何も流れずにすぐ完了する Observable</returns>
        public static Observable<T> Receive<T>() => Svc?.Receive<T>() ?? Observable.Empty<T>();
    }

    /// <summary>
    /// セーブの static ショートカット。
    /// <code>
    /// if (!Saves.Save("highscore", 1200)) ShowError();
    /// var best = Saves.Load("highscore", 0);
    /// await Saves.SaveAsync("world", world);       // 大きなデータは非同期で
    /// Saves.Slots.Switch(1); Saves.Slots.Save("progress", progress); // スロットごとのデータ
    /// </code>
    /// </summary>
    public static class Saves
    {
        public static ISaveService Service => AbubuServices.TryResolve<ISaveService>();
        private static ISaveService Svc => AbubuServices.ResolveOrLogError<ISaveService>();

        /// <returns>書き込めたら true。失敗した場合やサービスが無い場合は false (エラーログが出る)</returns>
        public static bool Save<T>(string key, T data)
        {
            var service = Svc;
            return service != null && service.Save(key, data);
        }

        /// <summary>新しいバージョンのデータの保護を無視して保存する (<see cref="ISaveService.ForceSave{T}"/>)</summary>
        public static bool ForceSave<T>(string key, T data)
        {
            var service = Svc;
            return service != null && service.ForceSave(key, data);
        }

        /// <returns>読み込めなかった場合やサービスが無い場合は <paramref name="defaultValue"/></returns>
        public static T Load<T>(string key, T defaultValue = default)
        {
            var service = Svc;
            return service != null ? service.Load(key, defaultValue) : defaultValue;
        }

        public static bool Exists(string key) => Svc?.Exists(key) ?? false;
        public static void Delete(string key) => Svc?.Delete(key);

        /// <summary>
        /// 非同期で保存する (大きなデータ向け。書き込みは別スレッド)。<see cref="IAsyncSaveService.SaveAsync{T}"/>
        /// <code>if (!await Saves.SaveAsync("world", world)) ShowError();</code>
        /// </summary>
        /// <returns>サービスが無い場合は失敗の結果</returns>
        public static UniTask<SaveResult> SaveAsync<T>(string key, T data)
        {
            var service = Svc;
            return service != null
                ? service.SaveUniTaskAsync(key, data)
                : UniTask.FromResult(new SaveResult(new SaveFailure(key, SaveOperation.Save, SaveFailureReason.Unknown, "ISaveService がありません")));
        }

        /// <summary>
        /// 非同期で読み込む。<see cref="IAsyncSaveService.LoadAsync{T}"/>
        /// <code>var world = (await Saves.LoadAsync&lt;World&gt;("world")).GetValueOrDefault(new World());</code>
        /// </summary>
        public static UniTask<SaveLoadResult<T>> LoadAsync<T>(string key)
        {
            var service = Svc;
            return service != null ? service.LoadUniTaskAsync<T>(key) : UniTask.FromResult(SaveLoadResult<T>.Missing());
        }

        /// <summary>まだ終わっていない非同期の書き込みをすべて待つ (終了処理の前など。フレームは止まる)</summary>
        public static void Flush() => Svc?.Flush();

        /// <summary>まだ終わっていない非同期の書き込みを、フレームを止めずに待つ</summary>
        public static UniTask FlushAsync() => Svc?.FlushUniTaskAsync() ?? UniTask.CompletedTask;

        /// <summary>
        /// 保存・読み込みの失敗 (ディスクの空き不足・権限・破損など)。サービスが無い場合は何も流れない。
        /// <code>Saves.OnFailed.Subscribe(f =&gt; ShowError(f.Reason)).AddTo(this);</code>
        /// </summary>
        public static Observable<SaveFailure> OnFailed =>
            AbubuServices.TryResolve<ISaveFailureNotifier>()?.SaveFailedAsObservable() ?? Observable.Empty<SaveFailure>();

        /// <summary>
        /// セーブスロットの static ショートカット (<see cref="ISaveSlotService"/>)。
        /// Saves.Save は共通の領域 (設定など)、Saves.Slots.Save は選択中のスロットに保存する。
        /// <code>
        /// Saves.Slots.Switch(1);                       // スロット 1 を選ぶ (無ければ作る)
        /// Saves.Slots.Save("progress", progress);      // スロット 1 に保存
        /// var progress = Saves.Slots.Load("progress", new Progress());
        /// foreach (var slot in Saves.Slots.List()) Debug.Log(slot.Id);
        /// </code>
        /// </summary>
        public static class Slots
        {
            public static ISaveSlotService Service => AbubuServices.TryResolve<ISaveSlotService>();
            private static ISaveSlotService Svc => AbubuServices.ResolveOrLogError<ISaveSlotService>();

            /// <summary>選択中のスロット ID。未選択・サービスが無い場合は null</summary>
            public static string ActiveId => Svc?.ActiveSlotId;

            /// <summary>選択中のスロットのデータの読み書き (切り替えに追従する)。サービスが無い場合は null</summary>
            public static ISaveScope Active => Svc?.Active;

            /// <summary>スロットを選ぶ (無ければ作る)</summary>
            public static bool Switch(int number) => Svc?.Switch(number) ?? false;
            /// <summary>スロットを選ぶ (無ければ作る)</summary>
            public static bool Switch(string slotId) => Svc?.Switch(slotId) ?? false;

            public static bool Exists(int number) => Svc?.Exists(number) ?? false;
            public static bool Create(int number, string summary = null) => Svc?.Create(number, summary) ?? false;
            public static bool Copy(int source, int destination, bool overwrite = false) => Svc?.Copy(source, destination, overwrite) ?? false;
            /// <summary>スロットとそのデータをすべて削除する</summary>
            public static bool DeleteSlot(int number) => Svc?.Delete(number) ?? false;

            /// <summary>スロットの一覧 (作成順)。サービスが無い場合は空</summary>
            public static System.Collections.Generic.IReadOnlyList<SaveSlotInfo> List() =>
                Svc?.GetSlots() ?? System.Array.Empty<SaveSlotInfo>();

            /// <returns>選択中のスロットに書き込めたら true</returns>
            public static bool Save<T>(string key, T data)
            {
                var service = Svc;
                return service != null && service.Active.Save(key, data);
            }

            /// <returns>読み込めなかった場合やサービスが無い場合は <paramref name="defaultValue"/></returns>
            public static T Load<T>(string key, T defaultValue = default)
            {
                var service = Svc;
                return service != null ? service.Active.Load(key, defaultValue) : defaultValue;
            }

            /// <summary>選択中のスロットの一覧用の概要を設定する</summary>
            public static bool SetSummary(string summary)
            {
                var service = Svc;
                return service?.ActiveSlotId != null && service.SetSummary(service.ActiveSlotId, summary);
            }

            /// <summary>選択中のスロットのプレイ時間を加算する</summary>
            public static bool AddPlayTime(System.TimeSpan delta)
            {
                var service = Svc;
                return service?.ActiveSlotId != null && service.AddPlayTime(service.ActiveSlotId, delta);
            }

            /// <summary>
            /// 選択中のスロットのプレイ時間を秒で加算する。毎フレーム呼んでよい (書き込みは一定時間分たまった時だけ。
            /// <see cref="SaveSlotOptions.PlayTimeSaveInterval"/>)。スロット未選択なら何もしない (false)
            /// <code>void Update() =&gt; Saves.Slots.AddPlayTime(Time.unscaledDeltaTime);</code>
            /// </summary>
            public static bool AddPlayTime(float seconds)
            {
                // 未選択の間も毎フレーム呼ばれうるので、ここではエラーを出さない (サービスが無い場合のエラーは Svc が 1 回だけ出す)
                var service = Svc;
                return service?.ActiveSlotId != null && !float.IsNaN(seconds) && !float.IsInfinity(seconds) &&
                       service.AddPlayTime(service.ActiveSlotId, System.TimeSpan.FromSeconds(seconds));
            }
        }
    }

    /// <summary>
    /// ポーズの static ショートカット。owner には呼び出し元 (this) を渡す。
    /// <code>
    /// GamePause.Pause(this);
    /// GamePause.Resume(this);
    /// </code>
    /// </summary>
    public static class GamePause
    {
        public static IPauseService Service => AbubuServices.TryResolve<IPauseService>();
        private static IPauseService Svc => AbubuServices.ResolveOrLogError<IPauseService>();

        /// <summary>ポーズ中か。サービスが無い場合は false</summary>
        public static bool IsPaused => Svc?.IsPaused.CurrentValue ?? false;
        public static void Pause(object owner) => Svc?.Pause(owner);
        public static void Resume(object owner) => Svc?.Resume(owner);
        public static void Toggle(object owner)
        {
            var service = Svc;
            if (service == null) return;
            if (service.IsPausedBy(owner)) service.Resume(owner);
            else service.Pause(owner);
        }
    }
}
