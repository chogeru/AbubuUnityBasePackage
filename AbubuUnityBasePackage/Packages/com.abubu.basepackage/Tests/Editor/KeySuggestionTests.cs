using NUnit.Framework;

namespace Abubu.Tests
{
    public sealed class KeySuggestionTests
    {
        private static readonly string[] Keys = { "jump", "coin", "explosion", "explosion_big", "land" };

        [Test]
        public void Typo_ReturnsClosestKey()
        {
            Assert.That(KeySuggestion.Closest("jmup", Keys), Is.EqualTo(new[] { "jump" }));
        }

        [Test]
        public void CaseDifference_IsIgnored()
        {
            Assert.That(KeySuggestion.Closest("COIN", Keys), Does.Contain("coin"));
        }

        [Test]
        public void PartialMatch_ListsShorterDifferenceFirst()
        {
            var result = KeySuggestion.Closest("explosion", Keys);
            Assert.That(result[0], Is.EqualTo("explosion"));
            Assert.That(result, Does.Contain("explosion_big"));
        }

        [Test]
        public void Unrelated_ReturnsEmpty()
        {
            Assert.That(KeySuggestion.Closest("zzzzzzzz", Keys), Is.Empty);
            Assert.That(KeySuggestion.Hint("zzzzzzzz", Keys), Is.Empty);
        }

        [Test]
        public void NullOrEmpty_ReturnsEmpty()
        {
            Assert.That(KeySuggestion.Closest(null, Keys), Is.Empty);
            Assert.That(KeySuggestion.Closest("", Keys), Is.Empty);
            Assert.That(KeySuggestion.Closest("jump", null), Is.Empty);
        }

        [Test]
        public void Hint_FormatsCandidates()
        {
            Assert.That(KeySuggestion.Hint("jmup", Keys), Does.Contain("\"jump\""));
        }

        [Test]
        public void Max_LimitsCount()
        {
            Assert.That(KeySuggestion.Closest("explosion", Keys, max: 1).Count, Is.EqualTo(1));
        }
    }
}
