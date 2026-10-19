using UnityEngine;

namespace Abubu.Effects
{
    /// <summary>
    /// エフェクト (パーティクル等) をプールから出し、終わったら自動で返却する。SE も同時に鳴らせる。
    /// <code>
    /// _effect.Play("explosion", enemy.position);
    /// _effect.Play(hitPrefab, hitPoint, Quaternion.LookRotation(normal));
    /// </code>
    /// </summary>
    /// <remarks>
    /// 自動返却までの秒数は次の順で決まる。
    /// 1. 引数 / EffectEntry の Lifetime (0 より大きければ)
    /// 2. プレハブ内の ParticleSystem から計算した長さ (ループを含む場合は自動返却しない → <see cref="Stop"/> で返す)
    /// 3. ParticleSystem が無いプレハブ (VFX Graph / Animator など) は EffectSettings.DefaultLifetime
    /// </remarks>
    public interface IEffectService
    {
        /// <summary>EffectLibrary のキーで再生する。見つからなければ警告を出して null</summary>
        GameObject Play(string key, Vector3 position, Quaternion? rotation = null, Transform parent = null);

        /// <summary>プレハブを直接指定して再生する</summary>
        /// <param name="lifetime">自動返却までの秒数。0 なら自動計算</param>
        GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null, Transform parent = null, float lifetime = 0f);

        /// <summary>再生中のエフェクトを止めてプールに返す</summary>
        void Stop(GameObject instance);
    }

    /// <summary>
    /// エフェクト再生時に割り込む処理 (SE を鳴らす、カメラを揺らす、など)。
    /// Container.Bind&lt;IEffectPlayHook&gt;().To&lt;MyHook&gt;().AsSingle() で追加できる。
    /// </summary>
    public interface IEffectPlayHook
    {
        void OnPlay(EffectEntry entry, GameObject instance, Vector3 position);
    }
}
