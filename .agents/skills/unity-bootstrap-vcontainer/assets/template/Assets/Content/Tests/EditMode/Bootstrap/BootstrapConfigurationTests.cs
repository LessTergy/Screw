using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectBootstrap.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace ProjectBootstrap.Tests.EditMode
{
    public sealed class BootstrapConfigurationTests
    {
        private const string BootstrapPath = "Assets/Content/Scenes/Bootstrap.unity";
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";

        [Test]
        public void RepeatedInstall_DoesNotOverwriteAssetsOrSettings()
        {
            var paths = new[]
            {
                BootstrapPath, GameplayPath,
                "Assets/Content/Prefabs/AppLifetimeScope.prefab",
                "Assets/Content/Prefabs/SceneNavigator.prefab",
                "Assets/Content/Data/VContainerSettings.asset"
            }.SelectMany(path => new[] { path, path + ".meta" }).ToArray();
            var contents = paths.Select(File.ReadAllBytes).ToArray();
            var preloaded = PlayerSettings.GetPreloadedAssets();
            var scenes = EditorBuildSettings.scenes.Select(scene => (scene.path, scene.enabled)).ToArray();

            BootstrapSceneInstaller.Install();

            for (var index = 0; index < paths.Length; index++)
            {
                Assert.That(File.ReadAllBytes(paths[index]), Is.EqualTo(contents[index]), paths[index]);
            }

            Assert.That(PlayerSettings.GetPreloadedAssets(), Is.EqualTo(preloaded));
            Assert.That(EditorBuildSettings.scenes.Select(scene => (scene.path, scene.enabled)).ToArray(), Is.EqualTo(scenes));
        }

        [Test]
        public void RootSettingsAndNavigatorReferences_AreWired()
        {
            var settings = AssetDatabase.LoadAssetAtPath<VContainerSettings>("Assets/Content/Data/VContainerSettings.asset");
            Assert.That(settings, Is.Not.Null);
            Assert.That(PlayerSettings.GetPreloadedAssets().OfType<VContainerSettings>().ToArray(), Is.EqualTo(new[] { settings }));
            var app = settings.RootLifetimeScope as AppLifetimeScope;
            Assert.That(app, Is.Not.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(app), Is.True);
            Assert.That(app.autoRun, Is.True);
            Assert.That(new SerializedObject(app).FindProperty("parentReference.TypeName").stringValue, Is.Empty);

            var navigator = new SerializedObject(app).FindProperty("_sceneNavigatorPrefab").objectReferenceValue as SceneNavigator;
            Assert.That(navigator, Is.Not.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(navigator), Is.True);
            var serialized = new SerializedObject(navigator);
            AssertSceneReference(serialized.FindProperty("_bootstrapScene"), BootstrapPath);
            AssertSceneReference(serialized.FindProperty("_gameplayScene"), GameplayPath);

            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            Assert.That(scenes.Take(2).ToArray(), Is.EqualTo(new[] { BootstrapPath, GameplayPath }));
            Assert.That(scenes.Count(path => path == BootstrapPath), Is.EqualTo(1));
            Assert.That(scenes.Count(path => path == GameplayPath), Is.EqualTo(1));
        }

        [TestCase(BootstrapPath, 0)]
        [TestCase(GameplayPath, 1)]
        public void Scenes_HaveExpectedScopes(string path, int expectedGameplayScopes)
        {
            var scene = SceneManager.GetSceneByPath(path);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            var previous = SceneManager.GetActiveScene();
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(roots.SelectMany(root => root.GetComponentsInChildren<AppLifetimeScope>(true)), Is.Empty);
                Assert.That(roots.SelectMany(root => root.GetComponentsInChildren<SceneNavigator>(true)), Is.Empty);
                var scopes = roots.SelectMany(root => root.GetComponentsInChildren<GameplayLifetimeScope>(true)).ToArray();
                Assert.That(scopes.Length, Is.EqualTo(expectedGameplayScopes));
                foreach (var scope in scopes)
                {
                    Assert.That(scope.autoRun, Is.True);
                    Assert.That(scope.gameObject.activeInHierarchy, Is.True);
                    Assert.That(new SerializedObject(scope).FindProperty("parentReference.TypeName").stringValue, Is.Empty);
                }
            }
            finally
            {
                if (openedHere)
                {
                    if (scene.isDirty)
                    {
                        throw new System.InvalidOperationException("Scene changed during inspection and remains open: " + path);
                    }

                    EditorSceneManager.CloseScene(scene, true);
                }

                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        private static void AssertSceneReference(SerializedProperty property, string expectedPath)
        {
            Assert.That(AssetDatabase.GetAssetPath(property.FindPropertyRelative("asset").objectReferenceValue), Is.EqualTo(expectedPath));
            Assert.That(property.FindPropertyRelative("guid").stringValue, Is.EqualTo(AssetDatabase.AssetPathToGUID(expectedPath)));
        }
    }
}
