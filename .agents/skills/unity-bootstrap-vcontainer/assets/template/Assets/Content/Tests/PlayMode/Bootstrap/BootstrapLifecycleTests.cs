using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace ProjectBootstrap.Tests.PlayMode
{
    public sealed class BootstrapLifecycleTests
    {
        private const string BootstrapPath = "Assets/Content/Scenes/Bootstrap.unity";
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";

        [UnityTest]
        public IEnumerator BootstrapRestartAndDirectGameplay_PreserveRootAndDisposeSceneServices()
        {
            var previousTimeScale = Time.timeScale;
            var gameplayLoads = 0;
            void CountLoads(Scene scene, LoadSceneMode mode)
            {
                if (scene.path == GameplayPath)
                {
                    gameplayLoads++;
                }
            }

            SceneManager.sceneLoaded += CountLoads;
            try
            {
                Time.timeScale = 1f;
                using (LifetimeScope.Enqueue(builder => builder.Register<DisposalTracker>(Lifetime.Scoped)))
                {
                    yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);
                    yield return WaitFor(() => Object.FindFirstObjectByType<AppLifetimeScope>() != null);
                    var app = Object.FindFirstObjectByType<AppLifetimeScope>();
                    var navigator = app.Container.Resolve<SceneNavigator>();
                    var entryPoint = app.Container.Resolve<IReadOnlyList<IStartable>>()
                        .Single(startable => startable.GetType().Name == "BootstrapEntryPoint");
                    entryPoint.Start();
                    navigator.ToGameplay();
                    yield return WaitFor(() => Object.FindFirstObjectByType<GameplayLifetimeScope>() != null);
                    yield return null;

                    var gameplay = Object.FindFirstObjectByType<GameplayLifetimeScope>();
                    AssertComposition(app, navigator, gameplay);
                    Assert.That(gameplayLoads, Is.EqualTo(1), "Repeated transition requests must load Gameplay only once.");
                    var tracker = gameplay.Container.Resolve<DisposalTracker>();
                    var loadCount = gameplayLoads;
                    navigator.RestartGameplay();
                    navigator.RestartGameplay();
                    yield return WaitFor(() => gameplay == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null);
                    yield return null;

                    var restarted = Object.FindFirstObjectByType<GameplayLifetimeScope>();
                    Assert.That(gameplayLoads, Is.EqualTo(loadCount + 1));
                    Assert.That(gameplay.Container, Is.Null);
                    Assert.That(tracker.IsDisposed, Is.True);
                    Assert.That(restarted.Container.Resolve<DisposalTracker>(), Is.Not.SameAs(tracker));
                    AssertComposition(app, navigator, restarted);

                    var restartedTracker = restarted.Container.Resolve<DisposalTracker>();
                    yield return SceneManager.LoadSceneAsync(GameplayPath, LoadSceneMode.Single);
                    yield return null;
                    var direct = Object.FindFirstObjectByType<GameplayLifetimeScope>();
                    Assert.That(restarted == null, Is.True);
                    Assert.That(restartedTracker.IsDisposed, Is.True);
                    AssertComposition(app, navigator, direct);
                    loadCount = gameplayLoads;
                    entryPoint.Start();
                    yield return null;
                    yield return null;
                    Assert.That(gameplayLoads, Is.EqualTo(loadCount), "Root entry point must not redirect direct Gameplay startup.");
                    Assert.That(Object.FindFirstObjectByType<GameplayLifetimeScope>(), Is.SameAs(direct));
                }
            }
            finally
            {
                SceneManager.sceneLoaded -= CountLoads;
                Time.timeScale = previousTimeScale;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);
        }

        private static void AssertComposition(AppLifetimeScope app, SceneNavigator navigator, GameplayLifetimeScope gameplay)
        {
            Assert.That(gameplay, Is.Not.Null);
            Assert.That(gameplay.Container, Is.Not.Null);
            Assert.That(gameplay.Parent, Is.SameAs(app));
            Assert.That(gameplay.Container.Resolve<SceneNavigator>(), Is.SameAs(navigator));
            Assert.That(gameplay.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(GameplayPath));
            Assert.That(Object.FindObjectsByType<AppLifetimeScope>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<SceneNavigator>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GameplayLifetimeScope>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        private static IEnumerator WaitFor(Func<bool> ready)
        {
            var deadline = Time.realtimeSinceStartup + 10f;
            while (!ready() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(ready(), Is.True, "Scene transition did not complete within 10 seconds.");
        }

        public sealed class DisposalTracker : IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }
}
