using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Screw.Editor.Levels;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Screw.Tests.Levels
{
    public sealed class PrimitiveColliderBakerTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<string> _assets = new List<string>();
        private Vector3[] _cubeVertices;
        private int[] _cubeTriangles;

        [SetUp]
        public void SetUp()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeVertices = cube.GetComponent<MeshFilter>().sharedMesh.vertices;
            _cubeTriangles = cube.GetComponent<MeshFilter>().sharedMesh.triangles;
            Object.DestroyImmediate(cube);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _assets.AsEnumerable().Reverse())
            {
                AssetDatabase.DeleteAsset(asset);
            }

            _assets.Clear();
            foreach (var item in _objects)
            {
                if (item != null && !EditorUtility.IsPersistent(item))
                {
                    Object.DestroyImmediate(item);
                }
            }

            _objects.Clear();
        }

        [Test]
        public void SolidOffsetBoxProducesOneInternalColliderWithoutChangingMesh()
        {
            var mesh = Boxes((new Vector3(12f, -7f, 3f), new Vector3(2f, 4f, 6f), false));
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var result = PrimitiveColliderBaker.Analyze(mesh);
            Assert.That(result.Boxes, Has.Count.EqualTo(1));
            Assert.That(result.CoveredCells, Is.EqualTo(result.InteriorCells));
            var expected = mesh.bounds;
            foreach (var point in Corners(result.Boxes[0]))
            {
                Assert.That(expected.Contains(point), Is.True);
            }

            CollectionAssert.AreEqual(vertices, mesh.vertices);
            CollectionAssert.AreEqual(triangles, mesh.triangles);
        }

        [Test]
        public void HollowMeshDoesNotFillAnEnclosedCavityOrCrossItsBoundary()
        {
            var mesh = Boxes((Vector3.zero, Vector3.one * 4f, false), (Vector3.zero, Vector3.one * 2f, true));
            var result = PrimitiveColliderBaker.Analyze(mesh, 16, 8, 0.5f);
            var outer = new Bounds(Vector3.zero, Vector3.one * 4f);
            var cavity = new Bounds(Vector3.zero, Vector3.one * 2f);
            Assert.That(result.Boxes.Count, Is.GreaterThan(1));
            Assert.That(result.Boxes.Count, Is.LessThanOrEqualTo(8));
            for (var i = 0; i < result.Boxes.Count; i++)
            {
                for (var j = i + 1; j < result.Boxes.Count; j++)
                {
                    var a = result.Boxes[i];
                    var b = result.Boxes[j];
                    var interiorsOverlap = a.min.x < b.max.x && a.max.x > b.min.x
                        && a.min.y < b.max.y && a.max.y > b.min.y
                        && a.min.z < b.max.z && a.max.z > b.min.z;
                    Assert.That(interiorsOverlap, Is.False, "Compound boxes must have disjoint interiors.");
                }
            }

            foreach (var box in result.Boxes)
            {
                var intersectsCavity = box.min.x < cavity.max.x && box.max.x > cavity.min.x
                    && box.min.y < cavity.max.y && box.max.y > cavity.min.y
                    && box.min.z < cavity.max.z && box.max.z > cavity.min.z;
                Assert.That(intersectsCavity, Is.False, "The entire box must avoid the cavity, not just its corners.");
                Assert.That(Corners(box).All(outer.Contains), Is.True);
            }
        }

        [TestCase(0.005f)]
        [TestCase(0.05f)]
        public void SurfaceInsetUsesMeshThicknessAndMeasuresTheRemainingInterior(float inset)
        {
            var mesh = Boxes((Vector3.zero, new Vector3(2f, 4f, 6f), false));
            var baseline = PrimitiveColliderBaker.Analyze(mesh, surfaceInset: 0f);
            var result = PrimitiveColliderBaker.Analyze(mesh, surfaceInset: inset);
            Assert.That(result.InteriorCells, Is.LessThanOrEqualTo(baseline.InteriorCells));
            Assert.That(result.CoveredCells, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(result.InteriorCells * 0.9f)));
            Assert.That(result.Boxes, Has.Count.EqualTo(1));
            var box = result.Boxes[0];
            var reduction = 4f * inset;
            Assert.That(box.size.x, Is.EqualTo(2f - reduction).Within(0.001f));
            Assert.That(box.size.y, Is.EqualTo(4f - reduction).Within(0.001f));
            Assert.That(box.size.z, Is.EqualTo(6f - reduction).Within(0.001f));
            var allowed = new Bounds(mesh.bounds.center, mesh.bounds.size - Vector3.one * (reduction - 0.0001f));
            Assert.That(Corners(box).All(allowed.Contains), Is.True);
        }

        [TestCase(1)]
        [TestCase(15)]
        public void CoverageFallsInFivePointStepsToRespectTheColliderBudget(int budget)
        {
            var mesh = Boxes((Vector3.zero, Vector3.one * 4f, false), (Vector3.zero, Vector3.one * 2f, true));
            var result = PrimitiveColliderBaker.Analyze(mesh, 16, budget, 0.9f, 0f, reduceCoverageToFit: true);
            Assert.That(result.Boxes.Count, Is.InRange(1, budget));
            Assert.That(result.TargetCoverage, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.9f));
            Assert.That((90 - Mathf.RoundToInt(result.TargetCoverage * 100f)) % 5, Is.Zero);
            Assert.That(result.CoveredCells, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(result.InteriorCells * result.TargetCoverage)));
            if (result.TargetCoverage < 0.9f)
            {
                Assert.That(result.CoveredCells, Is.LessThan(Mathf.CeilToInt(result.InteriorCells * (result.TargetCoverage + 0.05f))));
            }

            if (budget == 1)
            {
                Assert.That(result.TargetCoverage, Is.LessThan(0.9f));
                Assert.Throws<InvalidOperationException>(() => PrimitiveColliderBaker.Analyze(mesh, 16, budget, 0.9f, 0f));
            }
        }

        [Test]
        public void AchievableCoverageKeepsTheRequestedTarget()
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            var result = PrimitiveColliderBaker.Analyze(mesh, reduceCoverageToFit: true);
            Assert.That(result.TargetCoverage, Is.EqualTo(0.9f));
            Assert.That(result.Boxes, Has.Count.EqualTo(1));
        }

        [TestCase(0.03f)]
        [TestCase(0.1f)]
        public void CustomCoverageStepControlsTheBudgetFallback(float step)
        {
            var mesh = Boxes((Vector3.zero, Vector3.one * 4f, false), (Vector3.zero, Vector3.one * 2f, true));
            var result = PrimitiveColliderBaker.Analyze(mesh, 16, 1, 0.9f, 0f,
                reduceCoverageToFit: true, coverageStep: step);
            Assert.That(result.TargetCoverage, Is.GreaterThan(0f).And.LessThan(0.9f));
            Assert.That((0.9f - result.TargetCoverage) / step,
                Is.EqualTo(Mathf.Round((0.9f - result.TargetCoverage) / step)).Within(0.0001f));
            Assert.That(result.CoveredCells, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(result.InteriorCells * result.TargetCoverage)));
            Assert.That(result.CoveredCells, Is.LessThan(Mathf.CeilToInt(result.InteriorCells * (result.TargetCoverage + step))));
        }

        [TestCase(0f)]
        [TestCase(-0.01f)]
        [TestCase(0.000001f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidCoverageStepIsRejected(float step)
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveColliderBaker.Analyze(mesh, coverageStep: step));
        }

        [Test]
        public void LevelAndViewResolveTheSharedModelWhileReviewCopiesStayIndependent()
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            var model = new GameObject("Model");
            _objects.Add(model);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var modelPath = SaveFixture(model, mesh);
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var level = new GameObject("Level");
            _objects.Add(level);
            var assembly = new GameObject("Assembly");
            assembly.transform.SetParent(level.transform, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, assembly.transform);
            instance.name = "Model";
            var levelPath = "Assets/__ColliderLevel_" + Guid.NewGuid().ToString("N") + ".prefab";
            _assets.Add(levelPath);
            var levelAsset = PrefabUtility.SaveAsPrefabAsset(level, levelPath);

            var wrapper = new GameObject("View");
            _objects.Add(wrapper);
            var view = wrapper.AddComponent<Screw.Levels.LevelView>();
            var levelInstance = (GameObject)PrefabUtility.InstantiatePrefab(levelAsset, wrapper.transform);
            using (var serialized = new SerializedObject(view))
            {
                serialized.FindProperty("_levelRoot").objectReferenceValue = levelInstance.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var viewPath = "Assets/__ColliderView_" + Guid.NewGuid().ToString("N") + ".prefab";
            _assets.Add(viewPath);
            var viewAsset = PrefabUtility.SaveAsPrefabAsset(wrapper, viewPath);
            var copyPath = "Assets/__ColliderCopy_" + Guid.NewGuid().ToString("N") + ".prefab";
            _assets.Add(copyPath);
            Assert.That(AssetDatabase.CopyAsset(modelPath, copyPath), Is.True);

            Assert.That(ColliderBatchUtility.ResolveModelPath(modelAsset), Is.EqualTo(modelPath));
            Assert.That(ColliderBatchUtility.ResolveModelPath(levelAsset), Is.EqualTo(modelPath));
            Assert.That(ColliderBatchUtility.ResolveModelPath(viewAsset), Is.EqualTo(modelPath));
            Assert.That(ColliderBatchUtility.ResolveModelPath(AssetDatabase.LoadAssetAtPath<GameObject>(copyPath)), Is.EqualTo(copyPath));
        }

        [Test]
        public void BatchForwardsCustomParametersAndRejectsInvalidSettingsBeforeChangingAssets()
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            var model = new GameObject("CustomColliderParameters");
            _objects.Add(model);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var path = SaveFixture(model, mesh);
            var reportPath = ColliderBatchUtility.BatchReportPath;
            var previousReport = File.Exists(reportPath) ? File.ReadAllBytes(reportPath) : null;
            try
            {
                ColliderBatchUtility.Run(new[] { path }, resolution: 12, maxBoxes: 2,
                    minimumCoverage: 0.85f, surfaceInset: 0.001f, coverageStep: 0.03f);
                var batch = JArray.Parse(File.ReadAllText(reportPath));
                Assert.That((string)batch[0]["status"], Is.EqualTo("prepared"));
                var report = JObject.Parse(File.ReadAllText(PrimitiveColliderBaker.ReportPath(path)));
                Assert.That((int)report["resolution"], Is.EqualTo(12));
                Assert.That((int)report["maxBoxes"], Is.EqualTo(2));
                Assert.That((float)report["requestedCoverage"], Is.EqualTo(0.85f));
                Assert.That((float)report["surfaceInset"], Is.EqualTo(0.001f));
                Assert.That((float)report["coverageStep"], Is.EqualTo(0.03f));
                var prefabBytes = File.ReadAllBytes(path);
                var reportBytes = File.ReadAllBytes(reportPath);
                Assert.Throws<ArgumentOutOfRangeException>(() => ColliderBatchUtility.Run(new[] { path }, coverageStep: 0f));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(prefabBytes));
                Assert.That(File.ReadAllBytes(reportPath), Is.EqualTo(reportBytes));
            }
            finally
            {
                if (previousReport == null)
                {
                    File.Delete(reportPath);
                }
                else
                {
                    File.WriteAllBytes(reportPath, previousReport);
                }
            }
        }

        [TestCase(-0.1f)]
        [TestCase(0.5f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidSurfaceInsetIsRejected(float inset)
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveColliderBaker.Analyze(mesh, surfaceInset: inset));
        }

        [Test]
        public void SeparateVolumesKeepTheirEmptyGapAndProduceDeterministicResults()
        {
            var mesh = Boxes((Vector3.left * 2f, Vector3.one, false), (Vector3.right * 2f, Vector3.one, false));
            var first = PrimitiveColliderBaker.Analyze(mesh, 24, 32, 1f);
            var second = PrimitiveColliderBaker.Analyze(mesh, 24, 32, 1f);
            CollectionAssert.AreEqual(first.Boxes, second.Boxes);
            Assert.That(first.Boxes, Has.Count.EqualTo(2));
            Assert.That(first.Boxes.All(box => box.max.x < -1.5f || box.min.x > 1.5f), Is.True);
        }

        [Test]
        public void TiltedThinBoxAndMirroredWindingStayInsideTheOriginalVolume()
        {
            var size = new Vector3(3f, 0.3f, 2f);
            var rotation = Quaternion.Euler(17f, 31f, 23f);
            var mesh = Boxes((Vector3.zero, size, false));
            mesh.vertices = mesh.vertices.Select(point => rotation * new Vector3(-point.x, point.y, point.z)).ToArray();
            mesh.RecalculateBounds();
            var result = PrimitiveColliderBaker.Analyze(mesh, 32, 128, 0.8f);
            Assert.That(result.Boxes, Has.Count.EqualTo(1));
            var original = new Bounds(Vector3.zero, size);
            foreach (var box in result.Boxes)
            {
                Assert.That(Corners(box).All(point => original.Contains(Quaternion.Inverse(rotation) * (result.Rotation * point))), Is.True);
            }
        }

        [Test]
        public void OpenAndInconsistentlyWoundMeshesAreRejected()
        {
            var open = Boxes((Vector3.zero, Vector3.one, false));
            open.triangles = open.triangles.Skip(3).ToArray();
            Assert.Throws<InvalidOperationException>(() => PrimitiveColliderBaker.Analyze(open));
            var flipped = Boxes((Vector3.zero, Vector3.one, false));
            var indices = flipped.triangles;
            var swap = indices[0];
            indices[0] = indices[1];
            indices[1] = swap;
            flipped.triangles = indices;
            Assert.Throws<InvalidOperationException>(() => PrimitiveColliderBaker.Analyze(flipped));
        }

        [Test]
        public void ConvexVolumeAddsDisjointInternalBoxesToReachEightyPercent()
        {
            var vertices = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var indices = new List<int>();
            foreach (var x in new[] { 0, 1 })
            {
                foreach (var y in new[] { 2, 3 })
                {
                    foreach (var z in new[] { 4, 5 })
                    {
                        var normal = Vector3.Cross(vertices[y] - vertices[x], vertices[z] - vertices[x]);
                        var outward = Vector3.Dot(normal, vertices[x]) > 0f;
                        indices.AddRange(new[] { x, outward ? y : z, outward ? z : y });
                    }
                }
            }

            var mesh = new Mesh { vertices = vertices, triangles = indices.ToArray() };
            mesh.RecalculateBounds();
            _objects.Add(mesh);
            var result = PrimitiveColliderBaker.Analyze(mesh, maxBoxes: 64, minimumCoverage: 0.8f, surfaceInset: 0f);
            Assert.That(result.CoveredCells, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(result.InteriorCells * 0.8f)));
            Assert.That(result.Boxes.Count, Is.InRange(2, 64));
            foreach (var box in result.Boxes)
            {
                foreach (var corner in Corners(box).Select(point => result.Rotation * point))
                {
                    Assert.That(Mathf.Abs(corner.x) + Mathf.Abs(corner.y) + Mathf.Abs(corner.z), Is.LessThanOrEqualTo(1f));
                }
            }

            for (var i = 0; i < result.Boxes.Count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    var a = result.Boxes[i];
                    var b = result.Boxes[j];
                    Assert.That(a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y
                        && a.max.y > b.min.y && a.min.z < b.max.z && a.max.z > b.min.z, Is.False);
                }
            }
        }

        [Test]
        public void CollinearSplitEdgeDoesNotTurnAClosedMeshIntoAnOpenVolume()
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            var vertices = mesh.vertices.ToList();
            var indices = mesh.triangles;
            var middle = vertices.Count;
            vertices.Add((vertices[indices[0]] + vertices[indices[1]]) * 0.5f);
            mesh.vertices = vertices.ToArray();
            mesh.triangles = new[] { indices[0], middle, indices[2], middle, indices[1], indices[2] }
                .Concat(indices.Skip(3)).ToArray();
            var result = PrimitiveColliderBaker.Analyze(mesh);
            Assert.That(result.Boxes, Has.Count.EqualTo(1));
            Assert.That(result.Boxes.SelectMany(Corners).All(mesh.bounds.Contains), Is.True);
        }

        [Test]
        public void BudgetFailureNeverSubstitutesAnExteriorBox()
        {
            var mesh = Boxes((Vector3.zero, Vector3.one * 4f, false), (Vector3.zero, Vector3.one * 2f, true));
            var exception = Assert.Throws<InvalidOperationException>(() => PrimitiveColliderBaker.Analyze(mesh, 16, 1, 1f));
            StringAssert.Contains("budget", exception.Message);
        }

        [TestCase(0, 32, 0.9f)]
        [TestCase(65, 32, 0.9f)]
        [TestCase(24, 0, 0.9f)]
        [TestCase(24, 257, 0.9f)]
        [TestCase(24, 32, 0f)]
        [TestCase(24, 32, 1.1f)]
        public void InvalidSettingsAreRejected(int resolution, int boxes, float coverage)
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveColliderBaker.Analyze(mesh, resolution, boxes, coverage));
        }

        [Test]
        public void NullFlatAndNonFiniteInputCannotProduceColliders()
        {
            Assert.Throws<ArgumentNullException>(() => PrimitiveColliderBaker.Analyze(null));
            var mesh = Boxes((Vector3.zero, new Vector3(1f, 0f, 1f), false));
            Assert.Throws<InvalidOperationException>(() => PrimitiveColliderBaker.Analyze(mesh));
            var valid = Boxes((Vector3.zero, Vector3.one, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveColliderBaker.Analyze(valid, minimumCoverage: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveColliderBaker.Analyze(valid, minimumCoverage: float.PositiveInfinity));
        }

        [Test]
        public void RepeatedPrefabBakePreservesManualShapesTransformsMeshAndGuid()
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            var root = new GameObject("ColliderBakeFixture");
            _objects.Add(root);
            var part = new GameObject("Part");
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = new Vector3(2f, 3f, -4f);
            part.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            part.transform.localScale = new Vector3(-1f, 2f, 3f);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var manual = new GameObject("Manual");
            manual.transform.SetParent(root.transform, false);
            manual.AddComponent<MeshFilter>().sharedMesh = mesh;
            manual.AddComponent<SphereCollider>().radius = 0.25f;
            var path = SaveFixture(root, mesh);
            var guid = AssetDatabase.AssetPathToGUID(path);
            Assert.That(PrimitiveColliderBaker.BakePrefab(path), Is.EqualTo(1));
            var once = AssetDatabase.GetAssetDependencyHash(path);
            Assert.That(PrimitiveColliderBaker.BakePrefab(path), Is.EqualTo(1));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(saved.GetComponentsInChildren<BoxCollider>(true), Has.Length.EqualTo(1));
            var material = saved.GetComponentsInChildren<BoxCollider>(true).Single().sharedMaterial;
            Assert.That(AssetDatabase.GetAssetPath(material), Is.EqualTo(PrimitiveColliderBaker.PhysicsMaterialPath));
            Assert.That(material.staticFriction, Is.Zero);
            Assert.That(material.dynamicFriction, Is.Zero);
            Assert.That(material.bounciness, Is.Zero);
            Assert.That(material.frictionCombine, Is.EqualTo(PhysicMaterialCombine.Multiply));
            Assert.That(material.bounceCombine, Is.EqualTo(PhysicMaterialCombine.Multiply));
            Assert.That(saved.GetComponentsInChildren<SphereCollider>(true).Single().radius, Is.EqualTo(0.25f));
            Assert.That(saved.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            var savedPart = saved.transform.Find("Part");
            Assert.That(savedPart.localPosition, Is.EqualTo(part.transform.localPosition));
            Assert.That(savedPart.localRotation, Is.EqualTo(part.transform.localRotation));
            Assert.That(savedPart.localScale, Is.EqualTo(part.transform.localScale));
            Assert.That(savedPart.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            Assert.That(savedPart.Find(PrimitiveColliderBaker.GeneratedRootName).childCount, Is.EqualTo(1));
            Assert.That(once.isValid, Is.True);
            var report = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(PrimitiveColliderBaker.ReportPath(path)));
            Assert.That((bool)report["saved"], Is.True);
            Assert.That((int)report["penetrations"], Is.Zero);
            Assert.That((int)report["parts"][0]["boxes"], Is.EqualTo(1));
            Assert.That((float)report["parts"][0]["coverage"], Is.GreaterThanOrEqualTo((float)report["parts"][0]["selectedTarget"]));
            Assert.That((bool)report["parts"][0]["insideSourceValidated"], Is.True);
        }

        [Test]
        public void OverlappingClosedShellsKeepTheirSharedInterior()
        {
            var mesh = Boxes((Vector3.left * 0.35f, Vector3.one * 2f, false),
                (Vector3.right * 0.35f, Vector3.one * 2f, false));
            var result = PrimitiveColliderBaker.Analyze(mesh, 16, 8, 0.35f);
            var volume = new Bounds(Vector3.zero, new Vector3(2.7f, 2f, 2f));
            var sharedInterior = new Bounds(Vector3.zero, new Vector3(1.3f, 2f, 2f));
            Assert.That(result.Boxes.Any(box => box.min.x < sharedInterior.max.x
                && box.max.x > sharedInterior.min.x && box.min.y < sharedInterior.max.y
                && box.max.y > sharedInterior.min.y && box.min.z < sharedInterior.max.z
                && box.max.z > sharedInterior.min.z), Is.True);
            Assert.That(result.Boxes.SelectMany(Corners).All(volume.Contains), Is.True);
            Assert.That(result.CoveredCells, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(result.InteriorCells * 0.35f)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitReplacementPreservesGeometryAndDoesNotSaveOnFailure(bool invalid)
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            if (invalid)
            {
                mesh.triangles = mesh.triangles.Skip(3).ToArray();
            }

            var root = new GameObject("ManualColliderFixture");
            _objects.Add(root);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<BoxCollider>();
            var child = new GameObject("OldManualShape");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<SphereCollider>();
            var path = SaveFixture(root, mesh);
            var guid = AssetDatabase.AssetPathToGUID(path);
            var before = AssetDatabase.GetAssetDependencyHash(path);
            if (invalid)
            {
                Assert.Throws<InvalidOperationException>(() => PrimitiveColliderBaker.BakePrefab(path, replaceExisting: true));
                Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.EqualTo(before));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Collider>(), Has.Length.EqualTo(2));
                return;
            }

            Assert.That(PrimitiveColliderBaker.BakePrefab(path, replaceExisting: true), Is.EqualTo(1));
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(saved.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            Assert.That(saved.GetComponent<Collider>(), Is.Null);
            Assert.That(saved.transform.Find("OldManualShape"), Is.Null);
            Assert.That(saved.GetComponentsInChildren<Collider>(), Has.Length.EqualTo(1));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OrientedPrefabBakePreservesInternalBoundsOrRejectsShearing(bool nonUniformScale)
        {
            var size = new Vector3(3f, 0.3f, 2f);
            var meshRotation = Quaternion.Euler(17f, 31f, 23f);
            var mesh = Boxes((Vector3.zero, size, false));
            mesh.vertices = mesh.vertices.Select(point => meshRotation * point).ToArray();
            mesh.RecalculateBounds();
            var root = new GameObject("OrientedColliderFixture");
            _objects.Add(root);
            root.transform.localScale = nonUniformScale ? new Vector3(1f, 2f, 3f) : Vector3.one * 2f;
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var path = SaveFixture(root, mesh);
            if (nonUniformScale)
            {
                var before = AssetDatabase.GetAssetDependencyHash(path);
                var error = Assert.Throws<InvalidOperationException>(() => PrimitiveColliderBaker.BakePrefab(path));
                StringAssert.Contains("sheared", error.Message);
                Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.EqualTo(before));
                return;
            }

            Assert.That(PrimitiveColliderBaker.BakePrefab(path), Is.EqualTo(1));
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var collider = saved.GetComponentsInChildren<BoxCollider>(true).Single();
            var volume = new Bounds(Vector3.zero, size);
            foreach (var point in Corners(new Bounds(collider.center, collider.size)))
            {
                var meshPoint = saved.transform.InverseTransformPoint(collider.transform.TransformPoint(point));
                Assert.That(volume.Contains(Quaternion.Inverse(meshRotation) * meshPoint), Is.True);
            }
        }

        [Test]
        public void InvalidPartDoesNotPartiallyOverwriteThePrefab()
        {
            var mesh = Boxes((Vector3.zero, Vector3.one, false));
            var root = new GameObject("InvalidColliderBakeFixture");
            _objects.Add(root);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var path = SaveFixture(root, mesh);
            mesh.triangles = mesh.triangles.Skip(3).ToArray();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            var before = AssetDatabase.GetAssetDependencyHash(path);
            Assert.Throws<InvalidOperationException>(() => PrimitiveColliderBaker.BakePrefab(path));
            Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.EqualTo(before));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Collider>(true), Is.Empty);
            var report = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(PrimitiveColliderBaker.ReportPath(path)));
            Assert.That((bool)report["saved"], Is.False);
            Assert.That((string)report["error"], Is.Not.Empty);
        }

        private string SaveFixture(GameObject root, Mesh mesh)
        {
            var stem = "Assets/__PrimitiveColliderBaker_" + Guid.NewGuid().ToString("N");
            _assets.Add(stem + ".asset");
            AssetDatabase.CreateAsset(mesh, _assets.Last());
            _assets.Add(stem + ".prefab");
            PrefabUtility.SaveAsPrefabAsset(root, _assets.Last());
            return _assets.Last();
        }

        private Mesh Boxes(params (Vector3 Center, Vector3 Size, bool Inward)[] boxes)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            foreach (var box in boxes)
            {
                var offset = vertices.Count;
                vertices.AddRange(_cubeVertices.Select(point => box.Center + Vector3.Scale(point, box.Size)));
                for (var i = 0; i < _cubeTriangles.Length; i += 3)
                {
                    indices.Add(offset + _cubeTriangles[i]);
                    indices.Add(offset + _cubeTriangles[i + (box.Inward ? 2 : 1)]);
                    indices.Add(offset + _cubeTriangles[i + (box.Inward ? 1 : 2)]);
                }
            }

            var mesh = new Mesh { name = "ColliderBakeMesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            _objects.Add(mesh);
            return mesh;
        }

        private static IEnumerable<Vector3> Corners(Bounds box)
        {
            foreach (var x in new[] { box.min.x, box.max.x })
            {
                foreach (var y in new[] { box.min.y, box.max.y })
                {
                    foreach (var z in new[] { box.min.z, box.max.z })
                    {
                        yield return new Vector3(x, y, z);
                    }
                }
            }
        }
    }
}
