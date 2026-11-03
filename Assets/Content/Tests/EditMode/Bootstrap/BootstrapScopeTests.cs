using System.Linq;
using NUnit.Framework;
using Screw.Bootstrap;
using Screw.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Screw.Tests.Bootstrap
{
    public sealed class BootstrapScopeTests
    {
        private const string BootstrapPath = "Assets/Content/Scenes/Bootstrap.unity";
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";

        [Test]
        public void ProjectRootAndNavigatorReferencesAreConfigured()
        {
            var settings = AssetDatabase.LoadAssetAtPath<VContainerSettings>("Assets/Content/Settings/VContainerSettings.asset");
            Assert.That(settings, Is.Not.Null);
            Assert.That(PlayerSettings.GetPreloadedAssets().OfType<VContainerSettings>().ToArray(), Is.EqualTo(new[] { settings }));
            var app = settings.RootLifetimeScope as AppLifetimeScope;
            Assert.That(app, Is.Not.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(app), Is.True);
            Assert.That(app.GetComponentsInChildren<LifetimeScope>(true).Length, Is.EqualTo(1));
            AssertAutomaticScope(app);
            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>("Assets/Content/Settings/LevelContentCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(app.ContentCatalog, Is.SameAs(catalog));

            var navigator = new SerializedObject(app).FindProperty("_sceneNavigatorPrefab").objectReferenceValue as SceneNavigator;
            Assert.That(navigator, Is.Not.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(navigator), Is.True);
            Assert.That(navigator.transform.parent, Is.Null);
            Assert.That(navigator.enabled && navigator.gameObject.activeSelf, Is.True);
            Assert.That(navigator.GetComponentsInChildren<SceneNavigator>(true).Length, Is.EqualTo(1));
            Assert.That(navigator.GetComponentsInChildren<LifetimeScope>(true), Is.Empty);
            var serialized = new SerializedObject(navigator);
            AssertSceneReference(serialized.FindProperty("_bootstrapScene"), BootstrapPath);
            AssertSceneReference(serialized.FindProperty("_gameplayScene"), GameplayPath);
        }

        [Test]
        public void BuildSettingsStartWithBootstrapAndGameplay()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Where(scene => scene.enabled).Take(2).Select(scene => scene.path).ToArray(),
                Is.EqualTo(new[] { BootstrapPath, GameplayPath }));
            Assert.That(scenes.Count(scene => scene.path == BootstrapPath), Is.EqualTo(1));
            Assert.That(scenes.Count(scene => scene.path == GameplayPath), Is.EqualTo(1));
        }

        [TestCase(BootstrapPath, 0)]
        [TestCase(GameplayPath, 1)]
        public void SavedScenesUseAutomaticScopes(string path, int expectedGameplayScopes)
        {
            var scene = SceneManager.GetSceneByPath(path);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            var previous = SceneManager.GetActiveScene();
            try
            {
                if (openedHere)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }

                Assert.That(scene.isDirty, Is.False, "Save scenes before checking their saved configuration.");
                var roots = scene.GetRootGameObjects();
                var scopes = roots.SelectMany(root => root.GetComponentsInChildren<LifetimeScope>(true)).ToArray();
                Assert.That(scopes.Length, Is.EqualTo(expectedGameplayScopes));
                Assert.That(roots.SelectMany(root => root.GetComponentsInChildren<SceneNavigator>(true)), Is.Empty);
                foreach (var scope in scopes)
                {
                    Assert.That(scope, Is.TypeOf<GameplayLifetimeScope>());
                    AssertAutomaticScope(scope);
                    var gameplay = (GameplayLifetimeScope)scope;
                    Assert.That(gameplay.LevelAnchor, Is.Not.Null);
                    Assert.That(gameplay.SceneCamera, Is.Not.Null);
                    Assert.That(gameplay.SceneLight, Is.Not.Null);
                    Assert.That(gameplay.Ground, Is.Not.Null);
                    Assert.That(gameplay.LevelAnchor.gameObject.scene, Is.EqualTo(scene));
                    Assert.That(gameplay.SceneCamera.gameObject.scene, Is.EqualTo(scene));
                    Assert.That(gameplay.SceneLight.gameObject.scene, Is.EqualTo(scene));
                    Assert.That(gameplay.Ground.gameObject.scene, Is.EqualTo(scene));
                    Assert.That(gameplay.Ground, Is.Not.SameAs(gameplay.LevelAnchor));
                    Assert.That(gameplay.Ground.IsChildOf(gameplay.LevelAnchor), Is.False);
                    var view = gameplay.LevelView;
                    Assert.That(view, Is.Not.Null);
                    Assert.That(view.transform.parent, Is.SameAs(gameplay.LevelAnchor));
                    var settings = AssetDatabase.LoadAssetAtPath<VContainerSettings>("Assets/Content/Settings/VContainerSettings.asset");
                    Assert.That(view.ContentCatalog, Is.SameAs(((AppLifetimeScope)settings.RootLifetimeScope).ContentCatalog));
                    Assert.That(view.ValidateBindings().Number, Is.EqualTo(1));
                    Assert.That(view.Parts.Count, Is.EqualTo(4));
                    Assert.That(view.Bolts.Count, Is.EqualTo(9));
                    Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(view.LevelRoot.gameObject),
                        Is.SameAs(view.ContentCatalog.GetLevel(1).Prefab));
                    var orbit = gameplay.SceneCamera.GetComponent<OrbitCamera>();
                    Assert.That(orbit, Is.Not.Null);
                    Assert.That(orbit.LevelRoot, Is.SameAs(view.LevelRoot));
                    Assert.That(roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Count(), Is.EqualTo(1));
                }
            }
            finally
            {
                if (openedHere && scene.IsValid())
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

        private static void AssertAutomaticScope(LifetimeScope scope)
        {
            Assert.That(scope.autoRun, Is.True);
            Assert.That(scope.enabled && scope.gameObject.activeSelf, Is.True);
            Assert.That(scope.transform.parent, Is.Null);
            Assert.That(scope.parentReference.Object, Is.Null);
            Assert.That(new SerializedObject(scope).FindProperty("parentReference.TypeName").stringValue, Is.Empty);
        }

        private static void AssertSceneReference(SerializedProperty property, string expectedPath)
        {
            Assert.That(AssetDatabase.GetAssetPath(property.FindPropertyRelative("asset").objectReferenceValue), Is.EqualTo(expectedPath));
            Assert.That(property.FindPropertyRelative("guid").stringValue, Is.EqualTo(AssetDatabase.AssetPathToGUID(expectedPath)));
        }
    }
}
