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
        /// <summary>
        /// メッセージを発行する。その時点で <typeparamref name="T"/> を購読している相手にだけ届く。
        /// 購読者が 1 人もいない場合は何もしない (後から購読してもこのメッセージは届かない)。
        /// </summary>
        void Publish<T>(T message);

        /// <summary>
        /// <typeparamref name="T"/> 型のメッセージを受け取る Observable を返す。
        /// 購読の解除 (AddTo など) は呼び出し側で行うこと。
        /// </summary>
        Observable<T> Receive<T>();
    }

    /// <summary>
    /// <see cref="IEventBus"/> の標準実装。メッセージの型ごとに R3 の Subject を 1 つ持つ。
    /// </summary>
    /// <remarks>
    /// スレッドセーフではない。メインスレッドから使う前提。
    /// 送る型の完全一致で配信先を決めるので、基底クラスやインターフェースの型で購読しても派生型のメッセージは届かない。
    /// </remarks>
    public sealed class EventBus : IEventBus, IDisposable
    {
        /// <summary>メッセージ型 → Subject&lt;T&gt; (型引数が異なるため object として保持)</summary>
        private readonly Dictionary<Type, object> _subjects = new();
        private bool _disposed;

        /// <inheritdoc />
        public void Publish<T>(T message)
        {
            if (_disposed) return;
            // Subject は Receive が初めて呼ばれたときに作られる。まだ無い = 購読者がいないので何もしない
            if (_subjects.TryGetValue(typeof(T), out var subject)) ((Subject<T>)subject).OnNext(message);
        }

        /// <inheritdoc />
        /// <remarks>破棄後に呼ばれた場合は、何も流れずにすぐ完了する Observable を返す。</remarks>
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

        /// <summary>
        /// すべての Subject を破棄する。購読者には完了通知 (OnCompleted) が送られる。
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            foreach (var subject in _subjects.Values) ((IDisposable)subject).Dispose();
            _subjects.Clear();
        }
    }
}
