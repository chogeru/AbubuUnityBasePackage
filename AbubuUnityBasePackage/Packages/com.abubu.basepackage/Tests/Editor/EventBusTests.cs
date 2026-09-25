using System.Collections.Generic;
using Abubu.Events;
using NUnit.Framework;
using R3;

namespace Abubu.Tests
{
    public sealed class EventBusTests
    {
        private readonly struct A
        {
            public readonly int Value;
            public A(int value) => Value = value;
        }

        private readonly struct B { }

        [Test]
        public void Publish_DeliversOnlyToSameType()
        {
            using var bus = new EventBus();
            var received = new List<int>();
            var bCount = 0;
            using var s1 = bus.Receive<A>().Subscribe(a => received.Add(a.Value));
            using var s2 = bus.Receive<B>().Subscribe(_ => bCount++);

            bus.Publish(new A(1));
            bus.Publish(new A(2));

            Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(bCount, Is.Zero);
        }

        [Test]
        public void Publish_WithoutSubscribers_DoesNothing()
        {
            using var bus = new EventBus();
            Assert.DoesNotThrow(() => bus.Publish(new A(1)));
        }

        [Test]
        public void DisposedSubscription_StopsReceiving()
        {
            using var bus = new EventBus();
            var count = 0;
            var subscription = bus.Receive<A>().Subscribe(_ => count++);
            bus.Publish(new A(1));
            subscription.Dispose();
            bus.Publish(new A(2));

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void AfterDispose_PublishAndReceive_AreSafe()
        {
            var bus = new EventBus();
            bus.Dispose();
            Assert.DoesNotThrow(() => bus.Publish(new A(1)));
            Assert.DoesNotThrow(() => bus.Receive<A>().Subscribe(_ => { }).Dispose());
        }
    }
}
