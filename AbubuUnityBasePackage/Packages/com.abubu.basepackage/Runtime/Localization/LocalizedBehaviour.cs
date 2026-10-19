using System;
using R3;
using UnityEngine;
using Zenject;

namespace Abubu.Localization
{
    /// <summary>
    /// 言語の切り替えに合わせて表示を更新するコンポーネントの基底。
    /// DI コンテナから生成されれば注入された <see cref="ILocalizationService"/> を、そうでなければ <see cref="Loc.Service"/> を使う。
    /// </summary>
    public abstract class LocalizedBehaviour : MonoBehaviour
    {
        private ILocalizationService _service;
        private IDisposable _subscription;

        /// <summary>使うサービス (未設定なら Loc.Service)</summary>
        protected ILocalizationService Service => _service ?? Loc.Service;

        /// <summary>サービスを明示的に渡す (DI コンテナ経由で生成された場合は自動で呼ばれる)</summary>
        [Inject]
        public void SetService([InjectOptional] ILocalizationService service)
        {
            if (ReferenceEquals(_service, service)) return;
            _service = service;
            if (isActiveAndEnabled) Subscribe();
        }

        protected virtual void OnEnable() => Subscribe();

        protected virtual void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Subscribe()
        {
            _subscription?.Dispose();
            _subscription = null;
            var service = Service;
            if (service == null) return;

            _subscription = service.Changed.Subscribe(this, static (_, self) => self.Refresh());
            Refresh();
        }

        /// <summary>現在の言語で表示し直す</summary>
        public abstract void Refresh();
    }
}
