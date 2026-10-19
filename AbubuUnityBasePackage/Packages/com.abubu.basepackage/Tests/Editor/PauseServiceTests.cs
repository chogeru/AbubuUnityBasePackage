using Abubu.Pause;
using NUnit.Framework;
using UnityEngine;

namespace Abubu.Tests
{
    public sealed class PauseServiceTests
    {
        private float _timeScale;

        [SetUp]
        public void SetUp() => _timeScale = Time.timeScale;

        [TearDown]
        public void TearDown() => Time.timeScale = _timeScale;

        [Test]
        public void MultipleOwners_ResumeOnlyWhenAllReleased()
        {
            using var pause = new PauseService();
            object menu = new(), dialog = new();

            pause.Pause(menu);
            pause.Pause(dialog);
            pause.Resume(dialog);
            Assert.That(pause.IsPaused.CurrentValue, Is.True);

            pause.Resume(menu);
            Assert.That(pause.IsPaused.CurrentValue, Is.False);
        }

        [Test]
        public void SameOwnerTwice_IsCountedOnce()
        {
            using var pause = new PauseService();
            var owner = new object();

            pause.Pause(owner);
            pause.Pause(owner);
            pause.Resume(owner);
            Assert.That(pause.IsPaused.CurrentValue, Is.False);
        }

        [Test]
        public void TimeScale_IsRestored()
        {
            Time.timeScale = 0.5f;
            using var pause = new PauseService();
            var owner = new object();

            pause.Pause(owner);
            Assert.That(Time.timeScale, Is.Zero);
            pause.Resume(owner);
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
        }

        [Test]
        public void StopTimeScaleDisabled_LeavesTimeScale()
        {
            Time.timeScale = 1f;
            using var pause = new PauseService(new PauseSettings { StopTimeScale = false });
            pause.Pause(new object());
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        /// <summary>Time.timeScale を触らない、テスト用の時間管理</summary>
        private sealed class FakeTimeScale : ITimeScaleController
        {
            public float TimeScale { get; set; } = 1f;
        }

        [Test]
        public void CustomController_IsUsed_InsteadOfTimeScale()
        {
            Time.timeScale = 1f;
            var clock = new FakeTimeScale { TimeScale = 0.8f };
            using var pause = new PauseService(null, clock);
            var owner = new object();

            pause.Pause(owner);
            Assert.That(clock.TimeScale, Is.Zero);
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            pause.Resume(owner);
            Assert.That(clock.TimeScale, Is.EqualTo(0.8f));
        }

        [Test]
        public void TimeScaleChangedDuringPause_IsRespectedOnResume()
        {
            var clock = new FakeTimeScale { TimeScale = 1f };
            using var pause = new PauseService(null, clock);
            var owner = new object();

            pause.Pause(owner);
            clock.TimeScale = 0.3f; // ポーズ中に他のコードが変更 (スロー演出など)
            pause.Resume(owner);

            // 古い値 (1) で上書きしない
            Assert.That(clock.TimeScale, Is.EqualTo(0.3f));
        }

        [Test]
        public void ResumeAll_And_UnknownOwner()
        {
            using var pause = new PauseService();
            pause.Pause(new object());
            pause.Pause(new object());
            pause.Resume(new object()); // 知らない owner は無視
            Assert.That(pause.IsPaused.CurrentValue, Is.True);

            pause.ResumeAll();
            Assert.That(pause.IsPaused.CurrentValue, Is.False);
        }
    }
}
