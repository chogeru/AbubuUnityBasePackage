using System;
using System.Collections.Generic;
using R3;

namespace Abubu.Events
{
    /// <summary>
    /// 型をキーにした軽量イベントバス。事前登録が不要なのでゲームジャムで手早く使える。
    /// <code>
    /// public readonly struct EnemyDied { public readonly int Score; public EnemyDied(int s) => Score = s; }
    ///
    /// _events.Publish(new EnemyDied(100));
    /// _events.Receive&lt;EnemyDied&gt;().Subscribe(e => score += e.Score).AddTo(this);
    /// </code>
    /// 大規模になってきたら MessagePipe (IPublisher / ISubscriber) への移行を推奨。
    /// </summary>
    public interface IEventBus
    {
        void Publish<T>(T message);
        Observable<T> Receive<T>();
    }

    public sealed class EventBus : IEventBus, IDisposable
    {
        private readonly Dictionary<Type, object> _subjects = new();
        private bool _disposed;

        public void Publish<T>(T message)
        {
            if (_disposed) return;
            if (_subjects.TryGetValue(typeof(T), out var subject)) ((Subject<T>)subject).OnNext(message);
        }

        public Observable<T> Receive<T>()
        {
            if (_disposed) return Observable.Empty<T>();
            if (!_subjects.TryGetValue(typeof(T), out var subject))
            {
                subject = new Subject<T>();
                _subjects.Add(typeof(T), subject);
            }
            return (Subject<T>)subject;
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var subject in _subjects.Values) ((IDisposable)subject).Dispose();
            _subjects.Clear();
        }
    }
}
