using Abubu.Audio;
using Abubu.Effects;
using Abubu.Events;
using Abubu.Pool;
using Abubu.Save;
using Abubu.Scene;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace Abubu
{
    /// <summary>
    /// サウンドの static ショートカット。ゲームジャムで最速で音を鳴らしたい時用。
    /// <code>
    /// Sound.PlaySe("jump");
    /// Sound.PlayBgm("stage1");
    /// </code>
    /// </summary>
    public static class Sound
    {
        public static ISoundService Service => AbubuServices.TryResolve<ISoundService>();

        public static void PlaySe(string key) => Service?.Se.Play(key);
        public static void PlaySe(string key, Vector3 position) => Service?.Se.Play(key, position);
        public static void PlaySe(AudioClip clip, float volume = 1f) => Service?.Se.Play(clip, volume);
        public static void PlayBgm(string key, float? fadeDuration = null) => Service?.Bgm.PlayAsync(key, fadeDuration).Forget();
        public static void PlayBgm(AudioClip clip, float volume = 1f) => Service?.Bgm.PlayAsync(clip, volume).Forget();
        public static void StopBgm(float? fadeDuration = null) => Service?.Bgm.StopAsync(fadeDuration).Forget();
        public static void PlayAmbient(string key, float? fadeDuration = null) => Service?.Ambient.PlayAsync(key, fadeDuration).Forget();
        public static void StopAmbient(string key, float? fadeDuration = null) => Service?.Ambient.StopAsync(key, fadeDuration).Forget();
        public static void StopAllAmbient(float? fadeDuration = null) => Service?.Ambient.StopAllAsync(fadeDuration).Forget();
        public static AudioVolumeModel Volume => Service?.Volume;
    }

    /// <summary>
    /// シーン遷移の static ショートカット。
    /// <code>
    /// Scenes.Load("Game");
    /// Scenes.LoadWith("Result", new ResultData(score));
    /// </code>
    /// </summary>
    public static class Scenes
    {
        public static ISceneService Service => AbubuServices.TryResolve<ISceneService>();

        public static void Load(string sceneName, SceneLoadOptions options = null) => Service?.LoadAsync(sceneName, options).Forget();
        public static void LoadWith<TPayload>(string sceneName, TPayload payload, SceneLoadOptions options = null) =>
            Service?.LoadWithPayloadAsync(sceneName, payload, options).Forget();
        public static void Reload(SceneLoadOptions options = null) => Service?.ReloadAsync(options).Forget();
        public static bool TryGetPayload<T>(out T payload)
        {
            payload = default;
            return Service?.TryGetPayload(out payload) ?? false;
        }
    }

    /// <summary>
    /// エフェクトの static ショートカット。
    /// <code>
    /// Effects.Play("explosion", transform.position);
    /// </code>
    /// </summary>
    public static class Effects
    {
        public static IEffectService Service => AbubuServices.TryResolve<IEffectService>();

        public static GameObject Play(string key, Vector3 position, Quaternion? rotation = null, Transform parent = null) =>
            Service?.Play(key, position, rotation, parent);
        public static GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null, Transform parent = null, float lifetime = 0f) =>
            Service?.Play(prefab, position, rotation, parent, lifetime);
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

        public static GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null) =>
            Service?.Rent(prefab, position, rotation, parent);
        public static T Rent<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component =>
            Service?.Rent(prefab, position, rotation, parent);
        public static void Return(GameObject instance) => Service?.Return(instance);
        public static void ReturnAfter(GameObject instance, float seconds) => Service?.ReturnAfter(instance, seconds);
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

        public static void Publish<T>(T message) => Service?.Publish(message);
        public static Observable<T> Receive<T>() => Service?.Receive<T>() ?? Observable.Empty<T>();
    }

    /// <summary>
    /// セーブの static ショートカット。
    /// <code>
    /// Saves.Save("highscore", 1200);
    /// var best = Saves.Load("highscore", 0);
    /// </code>
    /// </summary>
    public static class Saves
    {
        public static ISaveService Service => AbubuServices.TryResolve<ISaveService>();

        public static void Save<T>(string key, T data) => Service?.Save(key, data);
        public static T Load<T>(string key, T defaultValue = default) => Service != null ? Service.Load(key, defaultValue) : defaultValue;
        public static bool Exists(string key) => Service?.Exists(key) ?? false;
        public static void Delete(string key) => Service?.Delete(key);
    }
}
