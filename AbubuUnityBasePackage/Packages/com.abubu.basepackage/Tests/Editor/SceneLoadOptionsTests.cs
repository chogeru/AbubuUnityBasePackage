using Abubu.Scene;
using NUnit.Framework;
using UnityEngine;

namespace Abubu.Tests
{
    public sealed class SceneLoadOptionsTests
    {
        private static SceneSettings Settings() => new()
        {
            FadeOutDuration = 0.3f,
            FadeInDuration = 0.4f,
            FadeColor = Color.black,
            MinimumLoadingTime = 1f,
        };

        [Test]
        public void Null_UsesSettings()
        {
            var r = SceneLoadOptions.Resolve(null, Settings());
            Assert.That(r.FadeOutDuration, Is.EqualTo(0.3f));
            Assert.That(r.FadeInDuration, Is.EqualTo(0.4f));
            Assert.That(r.FadeColor, Is.EqualTo(Color.black));
            Assert.That(r.MinimumLoadingTime, Is.EqualTo(1f));
            Assert.That(r.ShowLoadingView, Is.True);
        }

        [Test]
        public void SpecifiedValues_Override_OthersFallBack()
        {
            var r = SceneLoadOptions.Resolve(new SceneLoadOptions { FadeOutDuration = 0f, FadeColor = Color.white }, Settings());
            Assert.That(r.FadeOutDuration, Is.EqualTo(0f));
            Assert.That(r.FadeColor, Is.EqualTo(Color.white));
            Assert.That(r.FadeInDuration, Is.EqualTo(0.4f));
            Assert.That(r.MinimumLoadingTime, Is.EqualTo(1f));
        }

        [Test]
        public void Instant_DisablesFadeAndLoadingView()
        {
            var r = SceneLoadOptions.Resolve(SceneLoadOptions.Instant, Settings());
            Assert.That(r.FadeOutDuration, Is.Zero);
            Assert.That(r.FadeInDuration, Is.Zero);
            Assert.That(r.ShowLoadingView, Is.False);
        }
    }
}
