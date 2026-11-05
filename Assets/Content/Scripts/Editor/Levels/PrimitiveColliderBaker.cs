using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Screw.Editor.Levels
{
    public static class PrimitiveColliderBaker
    {
        public const string GeneratedRootName = "_GeneratedPrimitiveColliders";
        public const int DefaultMaxBoxes = 15;
        public const float DefaultMinimumCoverage = 0.9f;
        public const float DefaultSurfaceInset = 0.005f;
        public const string PhysicsMaterialPath = "Assets/Content/Settings/LevelPartsPhysics.physicMaterial";
        private const float Guard = 0.000002f;
        private const float WorldClearance = 0.0001f;

        [MenuItem("Tools/Screw/Levels/Bake Selected Model Colliders")]
        public static void BakeSelected()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            Debug.Log("Baked " + BakePrefab(path) + " primitive colliders in " + path);
        }

        public static int BakePrefab(string path, int resolution = 24, int maxBoxes = DefaultMaxBoxes,
            float minimumCoverage = DefaultMinimumCoverage, bool replaceExisting = false,
            float surfaceInset = DefaultSurfaceInset)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || string.IsNullOrEmpty(path)
                || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                throw new ArgumentException("Select a model prefab asset outside Play Mode.", nameof(path));
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var plans = new List<(MeshFilter Filter, PrimitiveColliderBakeResult Result)>();
                var errors = new List<string>();
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var generated = filter.transform.Find(GeneratedRootName);
                    if (!replaceExisting && filter.GetComponentsInChildren<Collider>(true).Any(collider => generated == null
                        || !collider.transform.IsChildOf(generated))
                        || filter.transform.parent != null && filter.transform.parent.GetComponentsInParent<Collider>(true).Length != 0)
                    {
                        continue;
                    }

                    try
                    {
                        var result = Analyze(filter.sharedMesh, resolution, maxBoxes, minimumCoverage, surfaceInset,
                            reduceCoverageToFit: true);
                        var matrix = filter.transform.localToWorldMatrix;
                        var x = (Vector3)matrix.GetColumn(0);
                        var y = (Vector3)matrix.GetColumn(1);
                        var z = (Vector3)matrix.GetColumn(2);
                        if (Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > Guard
                            || Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > Guard
                            || Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > Guard
                            || Quaternion.Angle(result.Rotation, Quaternion.identity) > 0.01f
                            && (Mathf.Abs(x.magnitude - y.magnitude) > Guard || Mathf.Abs(x.magnitude - z.magnitude) > Guard))
                        {
                            throw new InvalidOperationException("Oriented collider would be sheared by the prefab transforms.");
                        }

                        plans.Add((filter, result));
                    }
                    catch (InvalidOperationException exception)
                    {
                        errors.Add(filter.name + ": " + exception.Message);
                    }
                }

                if (errors.Count != 0)
                {
                    throw new InvalidOperationException("Prefab was not changed:\n" + string.Join("\n", errors));
                }

                if (plans.Count == 0)
                {
                    return 0;
                }

                var material = GetPhysicsMaterial();
                var count = 0;
                var accepted = new List<BoxCollider>();
                foreach (var plan in plans)
                {
                    var previous = plan.Filter.transform.Find(GeneratedRootName);
                    if (previous != null)
                    {
                        Object.DestroyImmediate(previous.gameObject);
                    }

                    if (replaceExisting)
                    {
                        foreach (var collider in plan.Filter.GetComponentsInChildren<Collider>(true))
                        {
                            if (collider.transform != plan.Filter.transform && collider.transform.childCount == 0
                                && collider.GetComponents<Component>().Length == 2)
                            {
                                Object.DestroyImmediate(collider.gameObject);
                            }
                            else
                            {
                                Object.DestroyImmediate(collider);
                            }
                        }
                    }

                    var generated = new GameObject(GeneratedRootName).transform;
                    generated.SetParent(plan.Filter.transform, false);
                    generated.localRotation = plan.Result.Rotation;
                    foreach (var box in plan.Result.Boxes)
                    {
                        var node = new GameObject("Box_" + count++).transform;
                        node.SetParent(generated, false);
                        var collider = node.gameObject.AddComponent<BoxCollider>();
                        collider.sharedMaterial = material;
                        collider.center = box.center;
                        var scale = node.lossyScale;
                        var inset = new Vector3(WorldClearance / Mathf.Abs(scale.x), WorldClearance / Mathf.Abs(scale.y),
                            WorldClearance / Mathf.Abs(scale.z));
                        var colliderSize = box.size - inset * 2f;
                        if (colliderSize.x <= 0f || colliderSize.y <= 0f || colliderSize.z <= 0f)
                        {
                            throw new InvalidOperationException("A generated box is too thin for physics clearance. Prefab was not changed.");
                        }

                        collider.size = colliderSize;
                        foreach (var obstacle in accepted)
                        {
                            ClipContact(collider, obstacle);
                        }

                        accepted.Add(collider);
                    }
                }

                foreach (var plan in plans)
                {
                    var actual = plan.Filter.transform.Find(GeneratedRootName).GetComponentsInChildren<BoxCollider>()
                        .Select(collider => new Bounds(collider.center, collider.size)).ToArray();
                    var covered = plan.Result.CountCovered(actual);
                    var target = SelectCoverageTarget(covered, plan.Result.InteriorCells, minimumCoverage, true);
                    if (target <= 0f || covered < Mathf.CeilToInt(plan.Result.InteriorCells * target))
                    {
                        throw new InvalidOperationException("Cross-part clipping left insufficient coverage for "
                            + plan.Filter.name + ". Prefab was not changed.");
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
                if (!saved)
                {
                    throw new InvalidOperationException("Unity did not save the collider prefab: " + path);
                }

                return count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static PhysicMaterial GetPhysicsMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(PhysicsMaterialPath);
            if (material == null)
            {
                material = new PhysicMaterial("LevelPartsPhysics")
                {
                    staticFriction = 0f,
                    dynamicFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicMaterialCombine.Multiply,
                    bounceCombine = PhysicMaterialCombine.Multiply
                };
                AssetDatabase.CreateAsset(material, PhysicsMaterialPath);
            }

            return material;
        }

        public static PrimitiveColliderBakeResult Analyze(Mesh mesh, int resolution = 24, int maxBoxes = DefaultMaxBoxes,
            float minimumCoverage = DefaultMinimumCoverage, float surfaceInset = DefaultSurfaceInset,
            bool reduceCoverageToFit = false)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (resolution < 4 || resolution > 64 || maxBoxes < 1 || maxBoxes > 256
                || !(minimumCoverage > 0f && minimumCoverage <= 1f)
                || !(surfaceInset >= 0f && surfaceInset < 0.5f))
            {
                throw new ArgumentOutOfRangeException("Invalid resolution, collider budget, interior coverage, or surface inset.");
            }

            var vertices = mesh.vertices;
            var indices = mesh.triangles;
            if (vertices.Length < 4 || indices.Length < 12 || vertices.Any(vertex => !Finite(vertex)))
            {
                throw new InvalidOperationException("Mesh has no finite closed volume.");
            }

            var rotation = FindFrame(vertices, indices);
            var inverse = Quaternion.Inverse(rotation);
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = inverse * vertices[i];
            }

            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var vertex in vertices)
            {
                bounds.Encapsulate(vertex);
            }

            if (!Finite(bounds.size) || bounds.size.x <= 0f || bounds.size.y <= 0f || bounds.size.z <= 0f)
            {
                throw new InvalidOperationException("Mesh has no three-dimensional volume.");
            }

            var size = bounds.size;
            var insetDistance = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * surfaceInset;
            var padding = new Vector3(insetDistance / size.x, insetDistance / size.y, insetDistance / size.z);
            for (var i = 0; i < vertices.Length; i++)
            {
                var point = vertices[i] - bounds.min;
                vertices[i] = new Vector3(point.x / size.x, point.y / size.y, point.z / size.z);
            }

            RequireClosed(vertices, indices);
            var triangles = new List<Triangle>();
            for (var i = 0; i < indices.Length; i += 3)
            {
                var triangle = new Triangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                if (Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).sqrMagnitude > 1e-20f)
                {
                    triangles.Add(triangle);
                }
            }

            var grid = new Grid(resolution);
            foreach (var triangle in triangles)
            {
                var min = Vector3.Min(triangle.A, Vector3.Min(triangle.B, triangle.C)) - Vector3.one * Guard - padding;
                var max = Vector3.Max(triangle.A, Vector3.Max(triangle.B, triangle.C)) + Vector3.one * Guard + padding;
                var first = grid.Cell(min);
                var last = grid.Cell(max);
                for (var z = first.z; z <= last.z; z++)
                {
                    for (var y = first.y; y <= last.y; y++)
                    {
                        for (var x = first.x; x <= last.x; x++)
                        {
                            var index = grid.Index(x, y, z);
                            if (!grid.Surface[index] && Intersects(triangle, grid.Center(x, y, z),
                                Vector3.one * (0.5f / resolution + Guard) + padding))
                            {
                                grid.Surface[index] = true;
                            }

                        }
                    }
                }
            }

            grid.FillInterior(point => Inside(point, triangles));
            var total = grid.Solid.Count(value => value);
            if (total == 0)
            {
                throw new InvalidOperationException("No wholly internal cells. Increase resolution or inspect thin/open geometry.");
            }

            var boxes = new List<Bounds>();
            var covered = 0;
            foreach (var threshold in new[] { 0.05, 0.1, 0.2 })
            {
                var proposals = new List<Bounds>();
                foreach (var raw in RunCoacd(vertices, indices, maxBoxes, threshold).OrderByDescending(Volume))
                {
                    if (!Inside(raw.center, triangles))
                    {
                        continue;
                    }

                    var fitted = FitInternal(raw, triangles, padding);
                    if (fitted.size.x > Guard && fitted.size.y > Guard && fitted.size.z > Guard)
                    {
                        proposals.Add(fitted);
                    }
                }

                var candidates = new List<Bounds>();
                var candidateCoverage = 0;
                while (candidates.Count < maxBoxes && candidateCoverage < Mathf.CeilToInt(total * minimumCoverage))
                {
                    var gridCandidate = grid.LargestFreeBox(candidates);
                    var best = gridCandidate;
                    var bestCoverage = grid.CountCovered(candidates.Concat(new[] { best }).ToArray());
                    foreach (var proposal in proposals)
                    {
                        var fitted = proposal;
                        foreach (var previous in candidates)
                        {
                            fitted = RemoveOverlap(fitted, previous);
                        }

                        var score = grid.CountCovered(candidates.Concat(new[] { fitted }).ToArray());
                        if (score > bestCoverage || score == bestCoverage && Volume(fitted) > Volume(best))
                        {
                            best = fitted;
                            bestCoverage = score;
                        }
                    }

                    if (bestCoverage <= candidateCoverage || Volume(best) <= 0f)
                    {
                        break;
                    }

                    if (!ClearOfSurface(best, triangles, padding))
                    {
                        throw new InvalidOperationException("Internal filling failed the source surface check ("
                            + (best == gridCandidate ? "grid" : "CoACD") + "): " + best + ". Prefab was not changed.");
                    }

                    candidates.Add(best);
                    candidateCoverage = bestCoverage;
                }

                if (candidateCoverage > covered)
                {
                    covered = candidateCoverage;
                    boxes = candidates;
                }

                if (covered >= Mathf.CeilToInt(total * minimumCoverage))
                {
                    break;
                }
            }

            var targetCoverage = SelectCoverageTarget(covered, total, minimumCoverage, reduceCoverageToFit);
            if (targetCoverage <= 0f || covered < Mathf.CeilToInt(total * targetCoverage))
            {
                throw new InvalidOperationException("CoACD budget/coverage rejected: " + covered + "/" + total
                    + " internal samples covered with " + boxes.Count + "/" + maxBoxes + " boxes. Prefab was not changed.");
            }

            while (boxes.Count > 1 && grid.CountCovered(boxes.Take(boxes.Count - 1).ToArray())
                >= Mathf.CeilToInt(total * targetCoverage))
            {
                boxes.RemoveAt(boxes.Count - 1);
            }

            covered = grid.CountCovered(boxes);
            var localBoxes = boxes.Select(box => new Bounds(bounds.min + Vector3.Scale(box.center, size),
                Vector3.Scale(box.size, size))).ToArray();
            var samples = grid.InteriorPoints().Select(point => bounds.min + Vector3.Scale(point, size)).ToArray();
            return new PrimitiveColliderBakeResult(Array.AsReadOnly(localBoxes), rotation, samples, covered, targetCoverage);
        }

        private static float SelectCoverageTarget(int covered, int total, float requested, bool reduceCoverageToFit)
        {
            var target = requested;
            for (var step = 1; reduceCoverageToFit && target > 0f && covered < Mathf.CeilToInt(total * target); step++)
            {
                target = (float)Math.Round(requested - step * 0.05d, 5);
            }

            return target;
        }

        private static bool Penetrates(BoxCollider a, BoxCollider b)
        {
            return Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                b, b.transform.position, b.transform.rotation, out _, out var distance) && distance > 0.00001f;
        }

        private static void ClipContact(BoxCollider collider, BoxCollider obstacle)
        {
            if (!Penetrates(collider, obstacle))
            {
                return;
            }

            var original = new Bounds(collider.center, collider.size);
            var best = new Bounds(original.center, Vector3.zero);
            for (var axis = 0; axis < 3; axis++)
            {
                foreach (var side in new[] { -1, 1 })
                {
                    var low = 0f;
                    var high = 1f - Guard;
                    var candidate = CutFace(original, axis, side, high);
                    collider.center = candidate.center;
                    collider.size = candidate.size;
                    if (Penetrates(collider, obstacle))
                    {
                        continue;
                    }

                    for (var iteration = 0; iteration < 24; iteration++)
                    {
                        var amount = (low + high) * 0.5f;
                        candidate = CutFace(original, axis, side, amount);
                        collider.center = candidate.center;
                        collider.size = candidate.size;
                        if (Penetrates(collider, obstacle))
                        {
                            low = amount;
                        }
                        else
                        {
                            high = amount;
                        }
                    }

                    var localAxis = Vector3.zero;
                    localAxis[axis] = original.size[axis];
                    var worldLength = collider.transform.TransformVector(localAxis).magnitude;
                    // Leave clearance for floating-point rounding when the level moves.
                    var cut = Mathf.Min(1f - Guard, high + WorldClearance / worldLength);
                    candidate = CutFace(original, axis, side, cut);
                    if (Volume(candidate) > Volume(best))
                    {
                        best = candidate;
                    }
                }
            }

            if (Volume(best) <= 0f)
            {
                throw new InvalidOperationException("A generated box is entirely inside another part. Prefab was not changed.");
            }

            collider.center = best.center;
            collider.size = best.size;
        }

        private static Bounds CutFace(Bounds bounds, int axis, int side, float amount)
        {
            var min = bounds.min;
            var max = bounds.max;
            if (side < 0)
            {
                max[axis] = Mathf.Lerp(max[axis], min[axis], amount);
            }
            else
            {
                min[axis] = Mathf.Lerp(min[axis], max[axis], amount);
            }

            var result = new Bounds();
            result.SetMinMax(min, max);
            return result;
        }

        private static Bounds[] RunCoacd(Vector3[] vertices, int[] indices, int maxBoxes, double threshold)
        {
            var root = Path.GetDirectoryName(Application.dataPath);
            var python = Path.Combine(root, ".tools/coacd/Scripts/python.exe");
            var worker = Path.Combine(root, "Tools/Colliders/coacd_boxes.py");
            if (!File.Exists(python))
            {
                throw new InvalidOperationException("Install CoACD first: run Tools/Colliders/Install.ps1.");
            }

            var json = JsonConvert.SerializeObject(new { vertices = vertices.Select(point => new[] { point.x, point.y, point.z }),
                triangles = indices, maxBoxes, threshold, worker = File.ReadAllText(worker) });
            string key;
            using (var hash = SHA256.Create())
            {
                key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
            }

            var directory = Path.Combine(root, ".tools/coacd/cache");
            Directory.CreateDirectory(directory);
            var request = Path.Combine(directory, key + ".input.json");
            var response = Path.Combine(directory, key + ".json");
            if (!File.Exists(response))
            {
                File.WriteAllText(request, json);
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + worker + "\" \"" + request + "\" \"" + response + "\"",
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }))
                {
                    var error = process.StandardError.ReadToEndAsync();
                    var output = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(120000))
                    {
                        process.Kill();
                        throw new InvalidOperationException("CoACD exceeded its two-minute preparation timeout.");
                    }

                    if (process.ExitCode != 0 || !File.Exists(response))
                    {
                        throw new InvalidOperationException("CoACD failed: " + error.GetAwaiter().GetResult()
                            + output.GetAwaiter().GetResult());
                    }
                }
            }

            var data = JObject.Parse(File.ReadAllText(response));
            var boxes = data["boxes"].Select(item =>
            {
                var min = item["min"].ToObject<float[]>();
                var max = item["max"].ToObject<float[]>();
                if (min.Length != 3 || max.Length != 3)
                {
                    throw new InvalidOperationException("CoACD returned invalid box coordinates.");
                }

                var box = new Bounds();
                box.SetMinMax(new Vector3(min[0], min[1], min[2]), new Vector3(max[0], max[1], max[2]));
                if (!Finite(box.center) || !Finite(box.size) || box.size.x <= 0f || box.size.y <= 0f || box.size.z <= 0f)
                {
                    throw new InvalidOperationException("CoACD returned an invalid box volume.");
                }

                return box;
            }).ToArray();
            if (boxes.Length == 0 || boxes.Length > maxBoxes)
            {
                throw new InvalidOperationException("CoACD returned an invalid collider count.");
            }

            return boxes;
        }

        private static Bounds FitInternal(Bounds raw, List<Triangle> triangles, Vector3 padding)
        {
            var low = 0f;
            var high = 1f;
            for (var i = 0; i < 24; i++)
            {
                var scale = (low + high) * 0.5f;
                if (ClearOfSurface(new Bounds(raw.center, raw.size * scale), triangles, padding))
                {
                    low = scale;
                }
                else
                {
                    high = scale;
                }
            }

            var result = new Bounds(raw.center, raw.size * low);
            for (var axis = 0; axis < 3; axis++)
            {
                foreach (var side in new[] { -1, 1 })
                {
                    var origin = side < 0 ? result.min[axis] : result.max[axis];
                    var target = side < 0 ? raw.min[axis] : raw.max[axis];
                    low = 0f;
                    high = 1f;
                    for (var i = 0; i < 20; i++)
                    {
                        var amount = (low + high) * 0.5f;
                        var min = result.min;
                        var max = result.max;
                        if (side < 0)
                        {
                            min[axis] = Mathf.Lerp(origin, target, amount);
                        }
                        else
                        {
                            max[axis] = Mathf.Lerp(origin, target, amount);
                        }

                        var candidate = new Bounds();
                        candidate.SetMinMax(min, max);
                        if (ClearOfSurface(candidate, triangles, padding))
                        {
                            low = amount;
                            result = candidate;
                        }
                        else
                        {
                            high = amount;
                        }
                    }
                }
            }

            result.size = Vector3.Max(Vector3.zero, result.size - Vector3.one * (Guard * 2f));
            return result;
        }

        private static bool ClearOfSurface(Bounds box, List<Triangle> triangles, Vector3 padding)
        {
            return !triangles.Any(triangle => Intersects(triangle, box.center, box.extents + Vector3.one * Guard + padding));
        }

        private static Bounds RemoveOverlap(Bounds box, Bounds other)
        {
            if (box.min.x >= other.max.x || box.max.x <= other.min.x
                || box.min.y >= other.max.y || box.max.y <= other.min.y
                || box.min.z >= other.max.z || box.max.z <= other.min.z)
            {
                return box;
            }

            var best = new Bounds(box.center, Vector3.zero);
            for (var axis = 0; axis < 3; axis++)
            {
                foreach (var side in new[] { -1, 1 })
                {
                    var min = box.min;
                    var max = box.max;
                    if (side < 0)
                    {
                        max[axis] = Mathf.Min(max[axis], other.min[axis] - Guard);
                    }
                    else
                    {
                        min[axis] = Mathf.Max(min[axis], other.max[axis] + Guard);
                    }

                    if (min.x < max.x && min.y < max.y && min.z < max.z)
                    {
                        var candidate = new Bounds();
                        candidate.SetMinMax(min, max);
                        if (Volume(candidate) > Volume(best))
                        {
                            best = candidate;
                        }
                    }
                }
            }

            return best;
        }

        private static float Volume(Bounds box)
        {
            return box.size.x * box.size.y * box.size.z;
        }

        private static Quaternion FindFrame(Vector3[] vertices, int[] indices)
        {
            var candidates = new List<(float Area, Quaternion Rotation)>();
            for (var i = 0; i < indices.Length; i += 3)
            {
                var a = vertices[indices[i + 1]] - vertices[indices[i]];
                var b = vertices[indices[i + 2]] - vertices[indices[i]];
                var normal = Vector3.Cross(a, b);
                if (normal.sqrMagnitude > 1e-20f)
                {
                    candidates.Add((normal.sqrMagnitude, Quaternion.LookRotation(normal, a)));
                }
            }

            var best = Quaternion.identity;
            var bestVolume = FrameVolume(vertices, best);
            foreach (var candidate in candidates.OrderByDescending(item => item.Area).Take(12))
            {
                var volume = FrameVolume(vertices, candidate.Rotation);
                if (volume < bestVolume * 0.999f)
                {
                    best = candidate.Rotation;
                    bestVolume = volume;
                }
            }

            return best;
        }

        private static float FrameVolume(Vector3[] vertices, Quaternion rotation)
        {
            var inverse = Quaternion.Inverse(rotation);
            var bounds = new Bounds(inverse * vertices[0], Vector3.zero);
            foreach (var vertex in vertices)
            {
                bounds.Encapsulate(inverse * vertex);
            }

            return bounds.size.x * bounds.size.y * bounds.size.z;
        }

        private static bool Finite(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x)
                && !float.IsNaN(point.y) && !float.IsInfinity(point.y)
                && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        }

        private static void RequireClosed(Vector3[] vertices, int[] indices)
        {
            var positions = new Dictionary<Vector3Int, int>();
            var points = new List<Vector3>();
            var welded = new int[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] / Guard);
                if (!positions.TryGetValue(key, out var id))
                {
                    id = positions.Count;
                    positions.Add(key, id);
                    points.Add(vertices[i]);
                }

                welded[i] = id;
            }

            var edges = new Dictionary<(int, int), (int Count, int Balance)>();
            for (var i = 0; i < indices.Length; i += 3)
            {
                var a = welded[indices[i]];
                var b = welded[indices[i + 1]];
                var c = welded[indices[i + 2]];
                if (a == b || b == c || a == c)
                {
                    continue;
                }

                foreach (var edge in new[] { (a, b), (b, c), (c, a) })
                {
                    var key = (Math.Min(edge.Item1, edge.Item2), Math.Max(edge.Item1, edge.Item2));
                    edges.TryGetValue(key, out var value);
                    edges[key] = (value.Count + 1, value.Balance + (edge.Item1 < edge.Item2 ? 1 : -1));
                }
            }

            if (edges.Count == 0 || edges.Values.Any(edge => edge.Count > 2 || edge.Count == 2 && edge.Balance != 0))
            {
                throw new InvalidOperationException("Mesh is open, non-manifold, or has inconsistent face winding.");
            }

            var boundary = edges.Where(edge => edge.Value.Count == 1).ToArray();
            var endpoints = boundary.SelectMany(edge => new[] { edge.Key.Item1, edge.Key.Item2 }).Distinct().ToArray();
            var balances = new Dictionary<(int, int), int>();
            foreach (var edge in boundary)
            {
                var start = points[edge.Key.Item1];
                var delta = points[edge.Key.Item2] - start;
                var cuts = endpoints.Select(id => (Id: id, T: Vector3.Dot(points[id] - start, delta) / delta.sqrMagnitude))
                    .Where(cut => cut.T >= 0f && cut.T <= 1f
                        && (points[cut.Id] - start - delta * cut.T).sqrMagnitude <= Guard * Guard)
                    .OrderBy(cut => cut.T).ToArray();
                for (var i = 1; i < cuts.Length; i++)
                {
                    var a = cuts[i - 1].Id;
                    var b = cuts[i].Id;
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    balances.TryGetValue(key, out var balance);
                    balances[key] = balance + edge.Value.Balance * (a < b ? 1 : -1);
                }
            }

            if (balances.Values.Any(balance => balance != 0))
            {
                throw new InvalidOperationException("Mesh has open boundaries or inconsistent face winding.");
            }
        }

        private static bool Intersects(Triangle triangle, Vector3 center, Vector3 half)
        {
            var a = triangle.A - center;
            var b = triangle.B - center;
            var c = triangle.C - center;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            foreach (var axis in axes)
            {
                if (Separates(axis, a, b, c, half))
                {
                    return false;
                }
            }

            var ab = b - a;
            var bc = c - b;
            var ca = a - c;
            if (Separates(Vector3.Cross(ab, bc), a, b, c, half))
            {
                return false;
            }

            foreach (var edge in new[] { ab, bc, ca })
            {
                foreach (var axis in axes)
                {
                    if (Separates(Vector3.Cross(edge, axis), a, b, c, half))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool Separates(Vector3 axis, Vector3 a, Vector3 b, Vector3 c, Vector3 half)
        {
            var pa = Vector3.Dot(axis, a);
            var pb = Vector3.Dot(axis, b);
            var pc = Vector3.Dot(axis, c);
            var radius = Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y + Mathf.Abs(axis.z) * half.z;
            return Mathf.Min(pa, Mathf.Min(pb, pc)) > radius || Mathf.Max(pa, Mathf.Max(pb, pc)) < -radius;
        }

        private static bool Inside(Vector3 point, List<Triangle> triangles)
        {
            var angle = 0d;
            foreach (var triangle in triangles)
            {
                var a = triangle.A - point;
                var b = triangle.B - point;
                var c = triangle.C - point;
                var la = Math.Sqrt(Dot(a, a));
                var lb = Math.Sqrt(Dot(b, b));
                var lc = Math.Sqrt(Dot(c, c));
                var numerator = (double)a.x * ((double)b.y * c.z - (double)b.z * c.y)
                    + (double)a.y * ((double)b.z * c.x - (double)b.x * c.z)
                    + (double)a.z * ((double)b.x * c.y - (double)b.y * c.x);
                var denominator = la * lb * lc + Dot(a, b) * lc + Dot(b, c) * la + Dot(c, a) * lb;
                angle += 2d * Math.Atan2(numerator, denominator);
            }

            var winding = Math.Abs(angle) / (4d * Math.PI);
            if (winding < 0.0001d)
            {
                return false;
            }

            if (Math.Abs(winding - Math.Round(winding)) < 0.0001d)
            {
                return true;
            }

            throw new InvalidOperationException("Mesh has an ambiguous interior.");
        }

        private static double Dot(Vector3 a, Vector3 b)
        {
            return (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        }

        private readonly struct Triangle
        {
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 C;

            public Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                A = a;
                B = b;
                C = c;
            }
        }

        private sealed class Grid
        {
            private readonly int _n;
            public readonly bool[] Surface;
            public readonly bool[] Solid;

            public Grid(int resolution)
            {
                _n = resolution;
                Surface = new bool[_n * _n * _n];
                Solid = new bool[Surface.Length];
            }

            public int Index(int x, int y, int z)
            {
                return x + _n * (y + _n * z);
            }

            public Vector3Int Cell(Vector3 point)
            {
                return new Vector3Int(Mathf.Clamp(Mathf.FloorToInt(point.x * _n), 0, _n - 1),
                    Mathf.Clamp(Mathf.FloorToInt(point.y * _n), 0, _n - 1),
                    Mathf.Clamp(Mathf.FloorToInt(point.z * _n), 0, _n - 1));
            }

            public Vector3 Center(int x, int y, int z)
            {
                return new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) / _n;
            }

            public void FillInterior(Func<Vector3, bool> inside)
            {
                var visited = (bool[])Surface.Clone();
                var queue = new Queue<Vector3Int>();
                var region = new List<int>();
                var directions = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up,
                    Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
                for (var z = 0; z < _n; z++)
                {
                    for (var y = 0; y < _n; y++)
                    {
                        for (var x = 0; x < _n; x++)
                        {
                            var index = Index(x, y, z);
                            if (visited[index])
                            {
                                continue;
                            }

                            var solid = inside(Center(x, y, z));
                            region.Clear();
                            visited[index] = true;
                            queue.Enqueue(new Vector3Int(x, y, z));
                            while (queue.Count != 0)
                            {
                                var point = queue.Dequeue();
                                region.Add(Index(point.x, point.y, point.z));
                                foreach (var direction in directions)
                                {
                                    var next = point + direction;
                                    if (next.x < 0 || next.y < 0 || next.z < 0 || next.x >= _n || next.y >= _n || next.z >= _n)
                                    {
                                        continue;
                                    }

                                    var nextIndex = Index(next.x, next.y, next.z);
                                    if (!visited[nextIndex])
                                    {
                                        visited[nextIndex] = true;
                                        queue.Enqueue(next);
                                    }
                                }
                            }

                            if (solid)
                            {
                                foreach (var cell in region)
                                {
                                    Solid[cell] = true;
                                }
                            }
                        }
                    }
                }
            }

            public int CountCovered(IReadOnlyList<Bounds> boxes)
            {
                return InteriorPoints().Count(point => boxes.Any(box => box.Contains(point)));
            }

            public Bounds LargestFreeBox(IReadOnlyList<Bounds> boxes)
            {
                var free = (bool[])Solid.Clone();
                for (var z = 0; z < _n; z++)
                {
                    for (var y = 0; y < _n; y++)
                    {
                        for (var x = 0; x < _n; x++)
                        {
                            var cell = new Bounds(Center(x, y, z), Vector3.one / _n);
                            if (free[Index(x, y, z)] && boxes.Any(box => box.min.x < cell.max.x && box.max.x > cell.min.x
                                && box.min.y < cell.max.y && box.max.y > cell.min.y
                                && box.min.z < cell.max.z && box.max.z > cell.min.z))
                            {
                                free[Index(x, y, z)] = false;
                            }
                        }
                    }
                }

                var best = new Bounds();
                var bestCells = 0;
                var slab = new bool[_n * _n];
                var heights = new int[_n];
                var stack = new int[_n + 1];
                for (var first = 0; first < _n; first++)
                {
                    for (var i = 0; i < slab.Length; i++)
                    {
                        slab[i] = true;
                    }

                    for (var last = first; last < _n; last++)
                    {
                        Array.Clear(heights, 0, heights.Length);
                        for (var y = 0; y < _n; y++)
                        {
                            for (var z = 0; z < _n; z++)
                            {
                                var index = y * _n + z;
                                slab[index] &= free[Index(last, y, z)];
                                heights[z] = slab[index] ? heights[z] + 1 : 0;
                            }

                            var top = 0;
                            for (var z = 0; z <= _n; z++)
                            {
                                var height = z == _n ? 0 : heights[z];
                                while (top > 0 && heights[stack[top - 1]] > height)
                                {
                                    var column = stack[--top];
                                    var left = top == 0 ? 0 : stack[top - 1] + 1;
                                    var cells = (last - first + 1) * heights[column] * (z - left);
                                    if (cells > bestCells)
                                    {
                                        bestCells = cells;
                                        best.SetMinMax(new Vector3(first, y - heights[column] + 1, left) / _n + Vector3.one * Guard,
                                            new Vector3(last + 1, y + 1, z) / _n - Vector3.one * Guard);
                                    }
                                }

                                stack[top++] = z;
                            }
                        }
                    }
                }

                return best;
            }

            public IEnumerable<Vector3> InteriorPoints()
            {
                for (var z = 0; z < _n; z++)
                {
                    for (var y = 0; y < _n; y++)
                    {
                        for (var x = 0; x < _n; x++)
                        {
                            if (Solid[Index(x, y, z)])
                            {
                                yield return Center(x, y, z);
                            }
                        }
                    }
                }

            }
        }
    }

    public sealed class PrimitiveColliderBakeResult
    {
        private readonly Vector3[] _interiorSamples;
        public IReadOnlyList<Bounds> Boxes { get; }
        public Quaternion Rotation { get; }
        public int InteriorCells { get; }
        public int CoveredCells { get; }
        public float TargetCoverage { get; }

        internal PrimitiveColliderBakeResult(IReadOnlyList<Bounds> boxes, Quaternion rotation, Vector3[] interiorSamples,
            int coveredCells, float targetCoverage)
        {
            Boxes = boxes;
            Rotation = rotation;
            _interiorSamples = interiorSamples;
            InteriorCells = interiorSamples.Length;
            CoveredCells = coveredCells;
            TargetCoverage = targetCoverage;
        }

        internal int CountCovered(IReadOnlyList<Bounds> boxes)
        {
            return _interiorSamples.Count(point => boxes.Any(box => box.Contains(point)));
        }
    }
}
