using System.Collections.Generic;
using Abubu.App;
using NUnit.Framework;
using R3;

namespace Abubu.Tests
{
    public sealed class AppLifecycleTests
    {
        private sealed class CountingHandler : IAppSuspendHandler
        {
            public int Count;
            public void OnSuspend() => Count++;
        }

        private static AppLifecycleService Create(SuspendTrigger triggers, CountingHandler handler) =>
            new(new AppSettings { SuspendTriggers = triggers }, new List<IAppSuspendHandler> { handler });

        [Test]
        public void Background_SuspendsOnce_AndResumes()
        {
            var handler = new CountingHandler();
            using var app = Create(SuspendTrigger.Background | SuspendTrigger.FocusLost, handler);
            var resumed = 0;
            using var _ = app.OnResume.Subscribe(_ => resumed++);

            // モバイルではフォーカス喪失とバックグラウンド移行が続けて来る
            app.NotifyFocus(false);
            app.NotifyPause(true);
            Assert.That(handler.Count, Is.EqualTo(1));
            Assert.That(app.IsSuspended.CurrentValue, Is.True);

            app.NotifyPause(false);
            app.NotifyFocus(true);
            Assert.That(resumed, Is.EqualTo(1));
            Assert.That(app.IsSuspended.CurrentValue, Is.False);
        }

        [Test]
        public void FocusLost_IsIgnored_WhenNotATrigger()
        {
            var handler = new CountingHandler();
            using var app = Create(SuspendTrigger.Background, handler);

            app.NotifyFocus(false);
            Assert.That(handler.Count, Is.Zero);
            Assert.That(app.IsFocused.CurrentValue, Is.False);
        }

        [Test]
        public void Quit_SuspendsUnlessAlreadySuspended()
        {
            var handler = new CountingHandler();
            using var app = Create(SuspendTrigger.Background | SuspendTrigger.Quit, handler);
            var quit = 0;
            using var _ = app.OnQuit.Subscribe(_ => quit++);

            app.NotifyPause(true);
            app.NotifyQuit();
            app.NotifyQuit();
            Assert.That(handler.Count, Is.EqualTo(1));
            Assert.That(quit, Is.EqualTo(1));
        }

        [Test]
        public void NoTriggers_DisablesSuspend()
        {
            var handler = new CountingHandler();
            using var app = Create(SuspendTrigger.None, handler);

            app.NotifyPause(true);
            app.NotifyFocus(false);
            app.NotifyQuit();
            Assert.That(handler.Count, Is.Zero);
        }

        [Test]
        public void ThrowingHandler_DoesNotStopOthers()
        {
            var handler = new CountingHandler();
            using var app = new AppLifecycleService(new AppSettings { SuspendTriggers = SuspendTrigger.Background },
                new List<IAppSuspendHandler> { new ThrowingHandler(), handler });

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            app.NotifyPause(true);
            Assert.That(handler.Count, Is.EqualTo(1));
        }

        private sealed class ThrowingHandler : IAppSuspendHandler
        {
            public void OnSuspend() => throw new System.InvalidOperationException("boom");
        }
    }
}
