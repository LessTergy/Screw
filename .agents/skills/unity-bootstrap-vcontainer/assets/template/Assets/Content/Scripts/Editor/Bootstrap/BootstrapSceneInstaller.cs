using System;
using System.IO;
using System.Linq;
using Eflatun.SceneReference.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace ProjectBootstrap.Editor
{
    public static class BootstrapSceneInstaller
    {
        private const string BootstrapPath = "Assets/Content/Scenes/Bootstrap.unity";
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";
        private const string AppPath = "Assets/Content/Prefabs/AppLifetimeScope.prefab";
        private const string NavigatorPath = "Assets/Content/Prefabs/SceneNavigator.prefab";
        private const string SettingsPath = "Assets/Content/Data/VContainerSettings.asset";

        [MenuItem("Tools/Project Bootstrap/Install Bootstrap and Gameplay")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException("Stop Play Mode and wait for compilation before installing.");
            }

            var paths = new[] { BootstrapPath, GameplayPath, AppPath, NavigatorPath, SettingsPath };
            var settings = AssetDatabase.FindAssets("t:VContainerSettings")
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            if (settings.Any(path => path != SettingsPath))
            {
                throw new InvalidOperationException("Existing VContainerSettings conflict: " + string.Join(", ", settings));
            }

            var existing = paths.Where(path => File.Exists(path) || File.Exists(path + ".meta")).ToArray();
            if (existing.Length == paths.Length && paths.All(path => AssetDatabase.LoadMainAssetAtPath(path) != null))
            {
                Debug.Log("[Bootstrap] Assets already exist; nothing overwritten. Verify configuration with BootstrapConfigurationTests.");
                return;
            }

            if (existing.Length != 0)
            {
                throw new InvalidOperationException("Partial or conflicting installation; inspect before continuing: " + string.Join(", ", existing));
            }

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                {
                    throw new InvalidOperationException("Save or discard modified scenes yourself before installing.");
                }
            }

            var preloaded = PlayerSettings.GetPreloadedAssets();
            if (preloaded.Any(asset => asset is VContainerSettings))
            {
                throw new InvalidOperationException("A preloaded VContainerSettings already exists. Inspect it before installing.");
            }

            var buildScenes = EditorBuildSettings.scenes;
            var activeScene = SceneManager.GetActiveScene();
            var bootstrap = default(Scene);
            var gameplay = default(Scene);
            try
            {
                EnsureFolder("Assets/Content/Scenes");
                EnsureFolder("Assets/Content/Prefabs");
                EnsureFolder("Assets/Content/Data");

                bootstrap = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
                SceneManager.SetActiveScene(bootstrap);
                SaveScene(bootstrap, BootstrapPath);

                gameplay = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
                SceneManager.SetActiveScene(gameplay);
                var gameplayScope = new GameObject(nameof(GameplayLifetimeScope)).AddComponent<GameplayLifetimeScope>();
                gameplayScope.autoRun = true;
                SaveScene(gameplay, GameplayPath);

                // Refresh maps after saving scenes so runtime references can resolve their paths.
                SceneDataMapsGenerator.Run(true);

                SceneManager.SetActiveScene(bootstrap);
                var navigatorObject = new GameObject(nameof(SceneNavigator));
                try
                {
                    var navigator = navigatorObject.AddComponent<SceneNavigator>();
                    var serialized = new SerializedObject(navigator);
                    AssignScene(serialized.FindProperty("_bootstrapScene"), BootstrapPath);
                    AssignScene(serialized.FindProperty("_gameplayScene"), GameplayPath);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    SavePrefab(navigatorObject, NavigatorPath);
                }
                finally
                {
                    Object.DestroyImmediate(navigatorObject);
                }

                var appObject = new GameObject(nameof(AppLifetimeScope));
                try
                {
                    var app = appObject.AddComponent<AppLifetimeScope>();
                    app.autoRun = true;
                    var serialized = new SerializedObject(app);
                    serialized.FindProperty("_sceneNavigatorPrefab").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<GameObject>(NavigatorPath).GetComponent<SceneNavigator>();
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    SavePrefab(appObject, AppPath);
                }
                finally
                {
                    Object.DestroyImmediate(appObject);
                }

                SaveScene(bootstrap, BootstrapPath);
                var containerSettings = ScriptableObject.CreateInstance<VContainerSettings>();
                containerSettings.RootLifetimeScope = AssetDatabase.LoadAssetAtPath<GameObject>(AppPath).GetComponent<AppLifetimeScope>();
                AssetDatabase.CreateAsset(containerSettings, SettingsPath);
                AssetDatabase.SaveAssets();

                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(BootstrapPath, true),
                    new EditorBuildSettingsScene(GameplayPath, true)
                }.Concat(buildScenes.Where(scene => scene.path != BootstrapPath && scene.path != GameplayPath)).ToArray();
                PlayerSettings.SetPreloadedAssets(preloaded.Concat(new Object[] { containerSettings }).ToArray());
                Debug.Log("[Bootstrap] Created Bootstrap, Gameplay, two prefabs and VContainerSettings; configured preload and build scenes. Check the active Build Profile and run the supplied tests when authorized.");
            }
            catch
            {
                EditorBuildSettings.scenes = buildScenes;
                PlayerSettings.SetPreloadedAssets(preloaded);
                Debug.LogError("[Bootstrap] Installation failed. Build/preload lists restored; inspect newly created assets before retrying.");
                throw;
            }
            finally
            {
                if (activeScene.IsValid() && activeScene.isLoaded)
                {
                    SceneManager.SetActiveScene(activeScene);
                }

                if (gameplay.IsValid() && gameplay.isLoaded)
                {
                    EditorSceneManager.CloseScene(gameplay, true);
                }

                if (bootstrap.IsValid() && bootstrap.isLoaded)
                {
                    EditorSceneManager.CloseScene(bootstrap, true);
                }
            }
        }

        private static void AssignScene(SerializedProperty property, string path)
        {
            property.FindPropertyRelative("asset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            property.FindPropertyRelative("guid").stringValue = AssetDatabase.AssetPathToGUID(path);
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var parent = path.Substring(0, path.LastIndexOf('/'));
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
            }
        }

        private static void SaveScene(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new IOException("Could not save scene: " + path);
            }
        }

        private static void SavePrefab(GameObject target, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(target, path, out var success);
            if (!success)
            {
                throw new IOException("Could not save prefab: " + path);
            }
        }
    }
}
