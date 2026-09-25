using System;
using Abubu.Audio;
using Abubu.Effects;
using Abubu.Pool;
using Abubu.Scene;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu
{
    /// <summary>
    /// モジュール間の連携をまとめる場所 (Composition Root)。
    /// 各モジュールは互いを直接参照せず、ここで結線する。
    /// </summary>
    internal sealed class PoolSceneBridge : IInitializable, IDisposable
    {
        private readonly ISceneService _scene;
        private readonly IPoolService _pool;
        private IDisposable _subscription;

        public PoolSceneBridge(ISceneService scene, IPoolService pool)
        {
            _scene = scene;
            _pool = pool;
        }

        // 旧シーンの親と一緒に破棄される前に、貸し出し中のオブジェクトをプールへ回収する
        public void Initialize() =>
            _subscription = _scene.OnBeforeSceneUnload.Subscribe(_pool, static (_, pool) => pool.ReturnAll());

        public void Dispose() => _subscription?.Dispose();
    }

    /// <summary>EffectLibrary の SeKey をエフェクトの位置で鳴らす</summary>
    internal sealed class EffectSeHook : IEffectPlayHook
    {
        private readonly ISePlayer _se;

        public EffectSeHook(ISePlayer se) => _se = se;

        public void OnPlay(EffectEntry entry, GameObject instance, Vector3 position)
        {
            if (!string.IsNullOrEmpty(entry.SeKey)) _se.Play(entry.SeKey, position);
        }
    }
}
