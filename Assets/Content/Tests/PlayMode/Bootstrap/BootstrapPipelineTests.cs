#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Screw.Bootstrap;
using Screw.Levels;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Screw.Tests.Bootstrap
{
    public sealed class BootstrapPipelineTests
    {
        private const string BootstrapPath = "Assets/Content/Scenes/Bootstrap.unity";
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";

        [UnityTest]
        public IEnumerator AutomaticStartupRestartAndDirectGameplayPreserveRootAndDisposeSceneServices()
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
                var app = VContainerSettings.Instance.GetOrCreateRootLifetimeScopeInstance() as AppLifetimeScope;
                Assert.That(app, Is.Not.Null);
                var navigator = app.Container.Resolve<SceneNavigator>();
                var entryPoint = app.Container.Resolve<IReadOnlyList<IStartable>>().OfType<BootstrapEntryPoint>().Single();
                if (SceneManager.GetActiveScene().path == BootstrapPath)
                {
                    entryPoint.Start();
                    yield return WaitFor(() => SceneManager.GetSceneByPath(GameplayPath).isLoaded);
                    yield return null;
                }

                gameplayLoads = 0;
                using (LifetimeScope.Enqueue(builder => builder.Register<DisposalTracker>(Lifetime.Scoped)))
                {
                    yield return SceneManager.LoadSceneAsync(BootstrapPath, LoadSceneMode.Single);
                    entryPoint.Start();
                    navigator.ToGameplay();
                    yield return WaitFor(() => SceneManager.GetSceneByPath(GameplayPath).isLoaded);
                    yield return null;

                    var gameplay = Object.FindFirstObjectByType<GameplayLifetimeScope>();
                    AssertComposition(app, navigator, gameplay);
                    Assert.That(gameplayLoads, Is.EqualTo(1), "Repeated startup requests must load Gameplay once.");
                    Assert.That(SceneManager.GetSceneByPath(BootstrapPath).isLoaded, Is.False);
                    Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
                    Assert.Throws<VContainerException>(() => app.Container.Resolve<DisposalTracker>());

                    var tracker = gameplay.Container.Resolve<DisposalTracker>();
                    var loadCount = gameplayLoads;
                    navigator.RestartGameplay();
                    navigator.RestartGameplay();
                    yield return WaitFor(() => gameplay == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null);
                    yield return null;

                    var restarted = Object.FindFirstObjectByType<GameplayLifetimeScope>();
                    Assert.That(gameplayLoads, Is.EqualTo(loadCount + 1), "Repeated restart requests must load Gameplay once.");
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
                    Assert.That(gameplayLoads, Is.EqualTo(loadCount), "Direct Gameplay startup must not trigger another load.");
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
            Assert.That(gameplay.autoRun, Is.True);
            Assert.That(gameplay.Container, Is.Not.Null);
            Assert.That(gameplay.Parent, Is.SameAs(app));
            Assert.That(gameplay.Container.Resolve<SceneNavigator>(), Is.SameAs(navigator));
            Assert.That(gameplay.Container.Resolve<LevelContentCatalog>(), Is.SameAs(app.ContentCatalog));
            var view = gameplay.Container.Resolve<LevelView>();
            Assert.That(view, Is.SameAs(gameplay.LevelView));
            Assert.That(view.ContentCatalog, Is.SameAs(app.Container.Resolve<LevelContentCatalog>()));
            Assert.That(view.transform.parent, Is.SameAs(gameplay.LevelAnchor));
            Assert.That(view.ValidateBindings().Number, Is.EqualTo(1));
            Assert.That(view.Parts.Count, Is.EqualTo(4));
            Assert.That(view.Bolts.Count, Is.EqualTo(9));
            Assert.That(gameplay.Container.Resolve<Camera>(), Is.SameAs(gameplay.SceneCamera));
            var selection = gameplay.Container.Resolve<BoltSelection>();
            Assert.That(gameplay.Container.Resolve<IReadOnlyList<IStartable>>().OfType<BoltSelection>().Single(),
                Is.SameAs(selection));
            gameplay.GameplaySession.enabled = false;
            try
            {
                AssertSelectionViews(selection, view, gameplay.SceneCamera);
            }
            finally
            {
                gameplay.GameplaySession.enabled = true;
            }
            Assert.That(gameplay.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(GameplayPath));
            Assert.That(Object.FindObjectsByType<AppLifetimeScope>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<SceneNavigator>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GameplayLifetimeScope>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        private static void AssertSelectionViews(BoltSelection selection, LevelView view, Camera camera)
        {
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var frontIds = new[] { 0, 1, 3, 5, 7 };
            try
            {
                foreach (var bolt in view.Bolts)
                {
                    var target = bolt.Node.GetComponent<Collider>().bounds.center;
                    var cameraZ = frontIds.Contains(bolt.Id) ? -24f : 24f;
                    camera.transform.position = view.LevelRoot.TransformPoint(new Vector3(0f, 2f, cameraZ));
                    camera.transform.LookAt(target, view.LevelRoot.up);
                    Assert.That(selection.TrySelect(camera.WorldToScreenPoint(target), out var selected),
                        Is.True, "Visible head " + bolt.Id);
                    Assert.That(selected.Id, Is.EqualTo(bolt.Id));

                    camera.transform.position = view.LevelRoot.TransformPoint(new Vector3(0f, 2f, -cameraZ));
                    camera.transform.LookAt(target, view.LevelRoot.up);
                    if (selection.TrySelect(camera.WorldToScreenPoint(target), out var opposite))
                    {
                        Assert.That(opposite.Id, Is.Not.EqualTo(bolt.Id), "Hidden head " + bolt.Id);
                    }
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
            }
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
#endif
