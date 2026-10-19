using Abubu.App;
using Abubu.Audio;
using Abubu.Localization;
using Abubu.Pause;
using Abubu.Save;
using Abubu.Scene;
using NUnit.Framework;
using UnityEngine;
using Zenject;

namespace Abubu.Tests
{
    public sealed class AbubuInstallerTests
    {
        private AbubuSettings _settings;

        [SetUp]
        public void SetUp() => _settings = ScriptableObject.CreateInstance<AbubuSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        [Test]
        public void DisabledModules_AreNotBound()
        {
            _settings.Modules.Sound = false;
            _settings.Modules.Localization = false;
            _settings.Modules.AppLifecycle = false;
            _settings.Modules.AutoInstallModules = false;
            var container = new DiContainer();

            AbubuInstaller.Install(container, _settings);

            Assert.That(container.HasBinding<ISoundService>(), Is.False);
            Assert.That(container.HasBinding<ILocalizationService>(), Is.False);
            Assert.That(container.HasBinding<IAppLifecycle>(), Is.False);
            // Save / Pause は常に登録される
            Assert.That(container.HasBinding<ISaveService>(), Is.True);
            Assert.That(container.HasBinding<IPauseService>(), Is.True);
        }

        [Test]
        public void SceneDisabled_AlsoDisablesBoot()
        {
            _settings.Modules.Scene = false;
            _settings.Modules.AutoInstallModules = false;
            var container = new DiContainer();

            AbubuInstaller.Install(container, _settings);

            Assert.That(container.HasBinding<ISceneService>(), Is.False);
            Assert.That(container.HasBinding<Abubu.Boot.IBootService>(), Is.False);
        }

        [Test]
        public void ExistingBinding_IsKept()
        {
            _settings.Modules.AutoInstallModules = false;
            var container = new DiContainer();
            var mySave = new SaveService(new MemorySaveStorage(), new JsonUtilitySaveSerializer());
            container.Bind<ISaveService>().FromInstance(mySave);

            AbubuInstaller.Install(container, _settings);

            Assert.That(container.Resolve<ISaveService>(), Is.SameAs(mySave));
        }

        [Test]
        public void SingleModuleInstaller_WorksAlone()
        {
            var container = new DiContainer();
            Installers.PauseInstaller.Install(container, new PauseSettings());

            Assert.That(container.Resolve<IPauseService>(), Is.Not.Null);
        }
    }
}
