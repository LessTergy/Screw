using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Screw.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Screw.Editor.Levels
{
    public static class PreparedLevelUtility
    {
        public static IEnumerable<int> PreparedNumbers
        {
            get
            {
                var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
                return catalog.Levels.Where(level => level.Number > 1 && File.Exists(ViewPath(AssetDatabase.GetAssetPath(level.Prefab))))
                    .Select(level => level.Number);
            }
        }

        public static string ViewPath(int number)
        {
            return ViewPath(LevelPrefabPath(number));
        }

        internal static string ViewPath(string prefabPath)
        {
            return Path.ChangeExtension(prefabPath, null) + "View.prefab";
        }

        public static string RoutePath(int number)
        {
            return LevelFolder(number) + "/Level_Route.json";
        }

        internal static string LevelFolder(int number)
        {
            return Path.GetDirectoryName(LevelPrefabPath(number)).Replace('\\', '/');
        }

        private static string LevelPrefabPath(int number)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
            return AssetDatabase.GetAssetPath(catalog.GetLevel(number).Prefab);
        }

        public static LevelRouteData Prepare(int number)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
            return Prepare(catalog, number, ViewPath(number), RoutePath(number), requireCampaignPaths: true);
        }

        internal static LevelRouteData Prepare(LevelContentCatalog catalog, int number, string viewPath, string routePath,
            bool requireCampaignPaths)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Prepare levels outside Play Mode.");
            }

            var asset = catalog.GetLevel(number);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject(Path.GetFileNameWithoutExtension(viewPath));
                SceneManager.MoveGameObjectToScene(root, scene);
                // shortcut: Shared Editor physics uses this offset; isolate physics if loaded content reaches it.
                root.transform.position = Vector3.right * 1000f;
                var view = root.AddComponent<LevelView>();
                var level = (GameObject)PrefabUtility.InstantiatePrefab(asset.Prefab, root.transform);
                using (var serialized = new SerializedObject(view))
                {
                    serialized.FindProperty("_contentCatalog").objectReferenceValue = catalog;
                    serialized.FindProperty("_levelNumber").intValue = number;
                    serialized.FindProperty("_levelRoot").objectReferenceValue = level.transform;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                LevelViewBindingUtility.Bind(view, requireCampaignPaths);
                var cameraObject = new GameObject("PreparationCamera", typeof(Camera), typeof(OrbitCamera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var light = new GameObject("PreparationLight", typeof(Light));
                SceneManager.MoveGameObjectToScene(light, scene);
                light.GetComponent<Light>().type = LightType.Directional;
                var camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.farClipPlane = 2000f;
                var input = cameraObject.GetComponent<OrbitCamera>();
                using (var serialized = new SerializedObject(input))
                {
                    serialized.FindProperty("_levelRoot").objectReferenceValue = root.transform;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                var route = BuildRoute(view, camera);
                Replay(view, camera, route);
                root.transform.position = Vector3.zero;
                PrefabUtility.SaveAsPrefabAsset(root, viewPath, out var saved);
                if (!saved)
                {
                    throw new InvalidOperationException("Unity did not save " + viewPath);
                }

                File.WriteAllText(routePath, JsonConvert.SerializeObject(route, Formatting.Indented));
                AssetDatabase.ImportAsset(routePath, ImportAssetOptions.ForceSynchronousImport);
                return route;
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static LevelRouteData BuildRoute(LevelView view, Camera camera)
        {
            var source = view.ValidateBindings();
            if (view.Parts.Any(part => !part.Node.gameObject.activeSelf)
                || view.Bolts.Any(bolt => !bolt.Node.gameObject.activeSelf))
            {
                throw new InvalidOperationException("Start preparation with a complete level.");
            }

            var input = camera.GetComponent<OrbitCamera>();
            var angles = new Vector2(input.Yaw, input.Pitch);
            var fieldOfView = camera.fieldOfView;
            var aspect = camera.aspect;
            var cameraPosition = camera.transform.position;
            var cameraRotation = camera.transform.rotation;
            try
            {
                camera.fieldOfView = 60f;
                camera.aspect = 9f / 16f;
                input.FrameLevel();
                input.Rotate(new Vector2(-input.Yaw, -input.Pitch));
                var normals = view.Bolts.ToDictionary(bolt => bolt.Id, bolt => bolt.Node.forward);
                var remaining = new HashSet<int>(source.Bolts.Select(bolt => bolt.Id));
                var steps = new List<RouteStepData>();
                using (var selection = new BoltSelection(view, camera))
                {
                    while (remaining.Count > 0)
                    {
                        RouteStepData step = null;
                        foreach (var bolt in view.Bolts.Where(bolt => remaining.Contains(bolt.Id)))
                        {
                            var normal = normals[bolt.Id];
                            var pitch = Mathf.Atan2(normal.y, normal.z) * Mathf.Rad2Deg;
                            var yaw = Mathf.Atan2(normal.x, -new Vector2(normal.y, normal.z).magnitude) * Mathf.Rad2Deg;
                            foreach (var offset in new[] { Vector2.zero, new Vector2(30f, 0f), new Vector2(-30f, 0f),
                                new Vector2(0f, 30f), new Vector2(0f, -30f), new Vector2(60f, 0f), new Vector2(-60f, 0f),
                                new Vector2(0f, 60f), new Vector2(0f, -60f) })
                            {
                                input.Rotate(new Vector2(yaw + offset.x - input.Yaw, pitch + offset.y - input.Pitch));
                                step = TryHead(bolt, camera, input, selection);
                                if (step != null)
                                {
                                    break;
                                }
                            }

                            if (step != null)
                            {
                                break;
                            }
                        }

                        if (step == null)
                        {
                            for (var pitch = 0; pitch < 360 && step == null; pitch += 15)
                            {
                                for (var yaw = 0; yaw < 360 && step == null; yaw += 15)
                                {
                                    input.Rotate(new Vector2(yaw - input.Yaw, pitch - input.Pitch));
                                    foreach (var bolt in view.Bolts.Where(bolt => remaining.Contains(bolt.Id)))
                                    {
                                        step = TryHead(bolt, camera, input, selection);
                                        if (step != null)
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (step == null)
                        {
                            throw new InvalidOperationException("No selectable head found for level " + view.LevelNumber
                                + ". Remaining bolt IDs: " + string.Join(",", remaining.OrderBy(id => id)));
                        }

                        steps.Add(step);
                        remaining.Remove(step.BoltId);
                        var selected = view.GetBolt(step.BoltId);
                        selected.Node.gameObject.SetActive(false);
                        if (source.Parts.Single(part => part.Id == selected.OwnerId).BoltIds.All(id => !remaining.Contains(id)))
                        {
                            view.GetPart(selected.OwnerId).Node.gameObject.SetActive(false);
                        }
                    }
                }

                var route = new LevelRouteData { SchemaVersion = 2, LevelNumber = view.LevelNumber,
                    Camera = new RouteCameraData { FieldOfViewDegrees = 60f, Aspect = 9f / 16f }, Steps = steps.ToArray() };
                route.Validate(source);
                return route;
            }
            finally
            {
                foreach (var part in view.Parts)
                {
                    part.Node.gameObject.SetActive(true);
                }

                foreach (var bolt in view.Bolts)
                {
                    bolt.Node.gameObject.SetActive(true);
                }

                camera.fieldOfView = fieldOfView;
                camera.aspect = aspect;
                input.ResetOrbit();
                input.Rotate(angles - new Vector2(input.Yaw, input.Pitch));
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            }
        }

        public static void Replay(LevelView view, Camera camera, LevelRouteData route)
        {
            using (var selection = new BoltSelection(view, camera))
            {
                selection.Start();
                using (var session = new LevelRouteSession(view, camera, selection, route))
                {
                    foreach (var step in route.Steps)
                    {
                        session.ReplayNext();
                    }
                }
            }

            view.ValidateBindings();
        }

        private static RouteStepData TryHead(LevelBoltBinding bolt, Camera camera, OrbitCamera input, BoltSelection selection)
        {
            var head = bolt.Node.GetComponent<BoxCollider>();
            foreach (var offset in new[] { Vector2.zero, new Vector2(0.25f, 0f), new Vector2(-0.25f, 0f),
                new Vector2(0f, 0.25f), new Vector2(0f, -0.25f) })
            {
                var point = camera.WorldToViewportPoint(bolt.Node.TransformPoint(head.center
                    + new Vector3(offset.x * head.size.x, offset.y * head.size.y, 0f)));
                if (point.x < 0f || point.x > 1f || point.y < 0f || point.y > 1f || point.z <= 0f)
                {
                    continue;
                }

                if (selection.TrySelect(camera.ViewportToScreenPoint(point), out var selected) && selected.Id == bolt.Id)
                {
                    return new RouteStepData { BoltId = bolt.Id, LevelEulerDegrees = new[] { input.Pitch, input.Yaw, 0f },
                        ViewportPoint = new[] { point.x, point.y } };
                }
            }

            return null;
        }
    }
}
