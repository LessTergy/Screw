using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Screw.Editor.Levels
{
    public static class MeshRepairUtility
    {
        public const string MenuPath = "Assets/Screw/Analyze Level Meshes...";

        [MenuItem(MenuPath, false, 1999)]
        public static void AnalyzeSelected()
        {
            try
            {
                var review = Analyze((GameObject)Selection.activeObject);
                if (review == null)
                {
                    EditorUtility.DisplayDialog("Анализ мешей", "Проблем в мешах уровня не обнаружено.", "OK");
                    return;
                }

                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(ComparisonPath(review));
                }
                MeshRepairReviewWindow.Open(review);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Анализ мешей", exception.Message, "OK");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanAnalyze()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && Selection.objects.Length == 1
                && Selection.activeObject is GameObject
                && AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        public static MeshRepairReview Analyze(GameObject prefab)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Analyze meshes outside Play Mode.");
            }

            var path = ColliderBatchUtility.ResolveModelPath(prefab);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var before = File.ReadAllBytes(path);
            try
            {
                var filters = source.GetComponentsInChildren<MeshFilter>(true);
                var parts = new List<MeshRepairReview.Part>();
                for (var i = 0; i < filters.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("Анализ мешей", filters[i].name, (float)i / filters.Length);
                    try
                    {
                        PrimitiveColliderBaker.ValidateTopology(filters[i].sharedMesh);
                    }
                    catch (InvalidOperationException exception)
                    {
                        parts.Add(new MeshRepairReview.Part
                        {
                            NodePath = AnimationUtility.CalculateTransformPath(filters[i].transform, source.transform),
                            Problem = exception.Message, Before = filters[i].sharedMesh,
                            BeforeHash = MeshHash(filters[i].sharedMesh)
                        });
                    }
                }

                if (parts.Count == 0)
                {
                    return null;
                }

                return PrepareReview(source, parts);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!before.SequenceEqual(File.ReadAllBytes(path)))
                {
                    throw new InvalidOperationException("Source prefab changed during mesh analysis.");
                }
            }
        }

        private static MeshRepairReview PrepareReview(GameObject source, List<MeshRepairReview.Part> parts)
        {
            var directory = ColliderBatchUtility.CreateReviewDirectory(AssetDatabase.GetAssetPath(source));
            var review = ScriptableObject.CreateInstance<MeshRepairReview>();
            review.Initialize(source, parts);
            AssetDatabase.CreateAsset(review, directory + "/Review.asset");
            var inputs = new JArray(parts.Where(part => part.Before != null).Select(part => new JObject
            {
                ["part"] = part.NodePath,
                ["vertices"] = JArray.FromObject(part.Before.vertices.Select(v => new[] { v.x, v.y, v.z })),
                ["triangles"] = JArray.FromObject(part.Before.triangles)
            }));
            var working = ".tools/colliders/repair/" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(review));
            Directory.CreateDirectory(working);
            File.WriteAllText(working + "/input.json", inputs.ToString(Formatting.None));
            var repairs = new Dictionary<string, JToken>();
            try
            {
                ColliderBatchUtility.RunRepair(working + "/input.json", working + "/output.json");
                repairs = JArray.Parse(File.ReadAllText(working + "/output.json"))
                    .ToDictionary(item => (string)item["part"]);
            }
            catch (Exception exception)
            {
                foreach (var part in parts)
                {
                    part.Error = exception.Message;
                }
            }

            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                for (var i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    EditorUtility.DisplayProgressBar("Подготовка проблемных деталей", part.NodePath, (float)i / parts.Count);
                    try
                    {
                        if (!string.IsNullOrEmpty(part.Error))
                        {
                            continue;
                        }
                        if (!repairs.TryGetValue(part.NodePath, out var repair))
                        {
                            throw new InvalidOperationException("No repair result for this mesh.");
                        }
                        if (repair["error"] != null)
                        {
                            throw new InvalidOperationException((string)repair["error"]);
                        }

                        part.Mode = (string)repair["mode"] ?? "cap-only";
                        part.FallbackReason = (string)repair["fallbackReason"];
                        part.AddedTriangles = (int?)repair["addedTriangles"] ?? 0;
                        part.TriangleCountBefore = (int?)repair["triangleCountBefore"] ?? part.Before.triangles.Length / 3;
                        part.TriangleCountAfter = (int?)repair["triangleCountAfter"] ?? 0;
                        part.ComponentCountBefore = (int?)repair["componentCountBefore"] ?? 0;
                        part.ComponentCountAfter = (int?)repair["componentCountAfter"] ?? 0;
                        var mesh = BuildRepairedMesh(part.Before, repair);
                        AssetDatabase.CreateAsset(mesh, directory + "/Mesh_" + i + ".asset");
                        part.After = mesh;
                        part.AfterHash = MeshHash(mesh);
                        var node = CreatePart(Filter(source, part.NodePath), mesh, "After");
                        var previewPath = directory + "/Part_" + i + ".prefab";
                        try
                        {
                            PrefabUtility.SaveAsPrefabAsset(node, previewPath, out var saved);
                            if (!saved)
                            {
                                throw new InvalidOperationException("Could not save the part preview.");
                            }
                        }
                        finally
                        {
                            Object.DestroyImmediate(node);
                        }

                        part.Preview = AssetDatabase.LoadAssetAtPath<GameObject>(previewPath);
                        PrimitiveColliderBaker.BakePrefab(previewPath);
                    }
                    catch (Exception exception)
                    {
                        part.Error = exception.Message;
                    }
                }

                BuildComparison(review, scene);
                if (!EditorSceneManager.SaveScene(scene, ComparisonPath(review)))
                {
                    throw new InvalidOperationException("Could not save the comparison scene.");
                }
                EditorUtility.SetDirty(review);
                AssetDatabase.SaveAssetIfDirty(review);
                SaveReport(review);
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }

            return review;
        }

        public static Mesh BuildRepairedMesh(Mesh original, JToken repair)
        {
            var mode = (string)repair["mode"] ?? "cap-only";
            if (mode != "full" && mode != "cap-only")
            {
                throw new InvalidOperationException("Unknown mesh repair mode: " + mode);
            }
            var vertices = repair["vertices"].Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
            var sources = repair["attributeSources"].ToObject<int[]>();
            if (sources.Length != vertices.Length || sources.Any(i => i < 0 || i >= original.vertexCount))
            {
                throw new InvalidOperationException("Invalid source attribute mapping in the repaired mesh.");
            }

            var submeshes = Enumerable.Range(0, original.subMeshCount)
                .Select(index => original.GetTriangles(index).ToList()).ToArray();
            if (mode == "full")
            {
                var triangles = repair["triangles"].ToObject<int[]>();
                var triangleSources = repair["triangleSources"].ToObject<int[]>();
                var sourceSubmeshes = submeshes.SelectMany((indices, index) => Enumerable.Repeat(index, indices.Count / 3)).ToArray();
                if (triangles.Length != triangleSources.Length * 3
                    || triangleSources.Any(index => index < 0 || index >= sourceSubmeshes.Length))
                {
                    throw new InvalidOperationException("Invalid source material mapping in the repaired mesh.");
                }
                foreach (var indices in submeshes)
                {
                    indices.Clear();
                }
                for (var i = 0; i < triangleSources.Length; i++)
                {
                    var indices = submeshes[sourceSubmeshes[triangleSources[i]]];
                    indices.Add(triangles[i * 3]);
                    indices.Add(triangles[i * 3 + 1]);
                    indices.Add(triangles[i * 3 + 2]);
                }
            }
            else
            {
                submeshes[0].AddRange(repair["capTriangles"].ToObject<int[]>());
            }
            if (submeshes.Any(indices => indices.Count % 3 != 0 || indices.Any(i => i < 0 || i >= vertices.Length)))
            {
                throw new InvalidOperationException("Invalid triangle indices in the repaired mesh.");
            }

            var mesh = Object.Instantiate(original);
            try
            {
                mesh.name = original.name + "_Repaired";
                mesh.Clear();
                mesh.indexFormat = vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : original.indexFormat;
                mesh.vertices = vertices;
                var normals = original.normals;
                var tangents = original.tangents;
                var colors = original.colors32;
                if (normals.Length != 0)
                {
                    mesh.normals = sources.Select(i => normals[i]).ToArray();
                }
                if (tangents.Length != 0)
                {
                    mesh.tangents = sources.Select(i => tangents[i]).ToArray();
                }
                if (colors.Length != 0)
                {
                    mesh.colors32 = sources.Select(i => colors[i]).ToArray();
                }
                for (var channel = 0; channel < 8; channel++)
                {
                    var uv = new List<Vector4>();
                    original.GetUVs(channel, uv);
                    if (uv.Count != 0)
                    {
                        mesh.SetUVs(channel, sources.Select(i => uv[i]).ToList());
                    }
                }

                mesh.subMeshCount = submeshes.Length;
                for (var i = 0; i < submeshes.Length; i++)
                {
                    mesh.SetTriangles(submeshes[i], i);
                }
                mesh.RecalculateBounds();
                PrimitiveColliderBaker.ValidateTopology(mesh);
                return mesh;
            }
            catch
            {
                Object.DestroyImmediate(mesh);
                throw;
            }
        }

        private static GameObject CreatePart(MeshFilter source, Mesh mesh, string name)
        {
            var root = new GameObject(name);
            var ancestors = new Stack<Transform>();
            for (var current = source.transform; current != null; current = current.parent)
            {
                ancestors.Push(current);
            }

            var parent = root.transform;
            foreach (var ancestor in ancestors)
            {
                var node = new GameObject(ancestor.name).transform;
                node.SetParent(parent, false);
                node.localPosition = ancestor.localPosition;
                node.localRotation = ancestor.localRotation;
                node.localScale = ancestor.localScale;
                parent = node;
            }
            parent.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = parent.gameObject.AddComponent<MeshRenderer>();
            var original = source.GetComponent<MeshRenderer>();
            if (original != null)
            {
                renderer.sharedMaterials = original.sharedMaterials;
            }
            return root;
        }

        private static void BuildComparison(MeshRepairReview review, Scene scene)
        {
            var offset = 0f;
            var bounds = new Bounds();
            var hasBounds = false;
            for (var i = 0; i < review.Parts.Count; i++)
            {
                var part = review.Parts[i];
                var pair = new GameObject("Pair_" + i.ToString("D3"));
                var before = CreatePart(Filter(review.Source, part.NodePath), part.Before, "Before");
                var renderer = before.GetComponentInChildren<MeshRenderer>();
                var size = Mathf.Max(0.1f, renderer.bounds.size.magnitude);
                before.transform.position += Vector3.left * size * 0.65f - renderer.bounds.center;
                before.transform.SetParent(pair.transform, true);
                if (part.Preview != null)
                {
                    var after = (GameObject)PrefabUtility.InstantiatePrefab(part.Preview, scene);
                    after.name = "After";
                    after.transform.position += Vector3.right * size * 0.65f - after.GetComponentInChildren<MeshRenderer>().bounds.center;
                    after.transform.SetParent(pair.transform, true);
                }
                pair.transform.position = Vector3.down * (offset + size * 0.5f);
                offset += size * 1.5f;
                foreach (var item in pair.GetComponentsInChildren<Renderer>())
                {
                    if (!hasBounds)
                    {
                        bounds = item.bounds;
                        hasBounds = true;
                    }
                    bounds.Encapsulate(item.bounds);
                }
            }

            var camera = new GameObject("ComparisonCamera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(0.1f, Mathf.Max(bounds.extents.y, bounds.extents.x / camera.aspect) * 1.2f);
            var distance = Mathf.Max(5f, bounds.size.magnitude * 2f);
            camera.transform.position = bounds.center + Vector3.back * distance;
            camera.farClipPlane = distance * 4f;
            var light = new GameObject("ComparisonLight", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        }

        public static void Apply(MeshRepairReview review, int index)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Apply mesh repairs outside Play Mode.");
            }

            var part = review.Parts[index];
            if (part.Applied || part.After == null || part.Preview == null || !string.IsNullOrEmpty(part.Error)
                || part.Preview.GetComponentsInChildren<BoxCollider>(true).Length == 0
                || part.Preview.GetComponentsInChildren<MeshFilter>(true).Length != 1
                || part.Preview.GetComponentInChildren<MeshFilter>(true).sharedMesh != part.After)
            {
                throw new InvalidOperationException("This repair is already applied or is not ready for approval.");
            }
            if (MeshHash(part.Before) != part.BeforeHash || MeshHash(part.After) != part.AfterHash)
            {
                throw new InvalidOperationException("Mesh changed since analysis. Run mesh analysis again.");
            }
            PrimitiveColliderBaker.ValidateTopology(part.After);
            var path = AssetDatabase.GetAssetPath(review.Source);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var filter = Filter(root, part.NodePath);
                if (filter.sharedMesh != part.Before)
                {
                    throw new InvalidOperationException("This part no longer uses the analyzed mesh. Run analysis again.");
                }
                filter.sharedMesh = part.After;
                var generated = filter.transform.Find(PrimitiveColliderBaker.GeneratedRootName);
                if (generated != null)
                {
                    Object.DestroyImmediate(generated.gameObject);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
                if (!saved)
                {
                    throw new InvalidOperationException("Unity did not save the repaired part.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            part.Applied = true;
            EditorUtility.SetDirty(review);
            AssetDatabase.SaveAssetIfDirty(review);
            SaveReport(review);
        }

        private static MeshFilter Filter(GameObject root, string path)
        {
            var node = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
            if (node == null || node.GetComponent<MeshFilter>() == null)
            {
                throw new InvalidOperationException("Part no longer exists: " + path);
            }
            return node.GetComponent<MeshFilter>();
        }

        public static string MeshHash(Mesh mesh)
        {
            return mesh == null ? string.Empty : AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(mesh)).ToString();
        }

        public static string ComparisonPath(MeshRepairReview review)
        {
            return Path.GetDirectoryName(AssetDatabase.GetAssetPath(review)).Replace('\\', '/') + "/Comparison.unity";
        }

        public static void SaveReport(MeshRepairReview review)
        {
            var report = new JObject
            {
                ["source"] = AssetDatabase.GetAssetPath(review.Source),
                ["problemParts"] = JArray.FromObject(review.Parts.Select(part => new
                {
                    part.NodePath, part.Problem, part.Error, part.Mode, part.FallbackReason, part.AddedTriangles, part.Applied,
                    part.TriangleCountBefore, part.TriangleCountAfter, part.ComponentCountBefore, part.ComponentCountAfter,
                    repairedMesh = AssetDatabase.GetAssetPath(part.After),
                    preview = AssetDatabase.GetAssetPath(part.Preview),
                    qualityReport = part.Preview == null ? null : PrimitiveColliderBaker.ReportPath(AssetDatabase.GetAssetPath(part.Preview))
                }))
            };
            var path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(review)) + "/Report.json";
            File.WriteAllText(path, report.ToString());
            AssetDatabase.ImportAsset(path);
        }
    }
}
