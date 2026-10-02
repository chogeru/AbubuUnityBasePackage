using System.Linq;
using Abubu.Editor;
using NUnit.Framework;

namespace Abubu.Tests
{
    public sealed class ScriptBindingTests
    {
        [Test]
        public void AllScriptableObjectsAndMonoBehaviours_LiveInFileWithSameName()
        {
            var unbound = AbubuSetupValidator.FindUnboundScriptTypes();
            Assert.That(unbound, Is.Empty,
                "ファイル名と一致しないため Missing Script になる型: " + string.Join(", ", unbound.Select(t => t.FullName)));
        }
    }
}
