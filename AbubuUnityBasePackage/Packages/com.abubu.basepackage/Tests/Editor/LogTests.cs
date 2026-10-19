using Abubu.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abubu.Tests
{
    public sealed class LogTests
    {
        private const string Category = "Abubu.Tests.Log";

        [TearDown]
        public void TearDown()
        {
            AbubuLog.MinLevel = LogLevel.Debug;
            AbubuLog.SetCategoryEnabled(Category, true);
        }

        [Test]
        public void Error_IsPrefixedWithCategory()
        {
            LogAssert.Expect(LogType.Error, $"[{Category}] broken");
            AbubuLog.Error(Category, "broken");
        }

        [Test]
        public void DisabledCategory_IsNotLogged()
        {
            AbubuLog.SetCategoryEnabled(Category, false);
            AbubuLog.Error(Category, "should not appear");
            LogAssert.NoUnexpectedReceived();
            Assert.That(AbubuLog.IsEnabled(LogLevel.Error, Category), Is.False);
            Assert.That(AbubuLog.IsEnabled(LogLevel.Error, "Other"), Is.True);
        }

        [Test]
        public void BelowMinLevel_IsNotLogged()
        {
            AbubuLog.MinLevel = LogLevel.Error;
            AbubuLog.Warning(Category, "should not appear");
            AbubuLog.Info(Category, "should not appear");
            LogAssert.NoUnexpectedReceived();
            Assert.That(AbubuLog.IsEnabled(LogLevel.Warning, Category), Is.False);
        }

        [Test]
        public void Channel_UsesItsCategory()
        {
            var channel = AbubuLog.For(Category);
            LogAssert.Expect(LogType.Warning, $"[{Category}] careful");
            channel.Warning("careful");
        }

        [Test]
        public void Configure_AppliesSettings()
        {
            AbubuLog.Configure(new LogSettings { MinLevel = LogLevel.Warning, DisabledCategories = new[] { Category } });
            Assert.That(AbubuLog.MinLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(AbubuLog.IsEnabled(LogLevel.Error, Category), Is.False);
        }
    }
}
