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
    public sealed class MeshRepairWorkflowTests
    {
        private string _directory;
        private Mesh _before;
        private Mesh _after;
        private GameObject _source;
        private MeshRepairReview _review;
        private byte[] _batchReport;

        [SetUp]
        public void SetUp()
        {
            var name = "__MeshRepairTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _directory = "Assets/" + name;
            _batchReport = File.Exists(ColliderBatchUtility.BatchReportPath) ? File.ReadAllBytes(ColliderBatchUtility.BatchReportPath) : null;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _before = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
            _after = Object.Instantiate(_before);
            Object.DestroyImmediate(cube);
            AssetDatabase.CreateAsset(_before, _directory + "/Before.asset");
            AssetDatabase.CreateAsset(_after, _directory + "/After.asset");
            var root = new GameObject("Model");
            try
            {
                foreach (var partName in new[] { "A", "B" })
                {
                    var part = new GameObject(partName, typeof(MeshFilter));
                    part.transform.SetParent(root.transform, false);
                    part.transform.localPosition = new Vector3(partName == "A" ? 2f : -2f, 3f, 4f);
                    part.GetComponent<MeshFilter>().sharedMesh = _before;
                    part.AddComponent<SphereCollider>().radius = 0.2f;
                    var generated = new GameObject(PrimitiveColliderBaker.GeneratedRootName, typeof(BoxCollider));
                    generated.transform.SetParent(part.transform, false);
                }
                _source = PrefabUtility.SaveAsPrefabAsset(root, _directory + "/Model.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            var preview = new GameObject("Preview", typeof(MeshFilter), typeof(BoxCollider));
            GameObject previewAsset;
            try
            {
                preview.GetComponent<MeshFilter>().sharedMesh = _after;
                previewAsset = PrefabUtility.SaveAsPrefabAsset(preview, _directory + "/Preview.prefab");
            }
            finally
            {
                Object.DestroyImmediate(preview);
            }
            _review = ScriptableObject.CreateInstance<MeshRepairReview>();
            _review.Initialize(_source, new List<MeshRepairReview.Part>
            {
                Entry("A", previewAsset), Entry("B", previewAsset)
            });
            AssetDatabase.CreateAsset(_review, _directory + "/Review.asset");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_directory);
            if (_batchReport == null)
            {
                File.Delete(ColliderBatchUtility.BatchReportPath);
            }
            else
            {
                File.WriteAllBytes(ColliderBatchUtility.BatchReportPath, _batchReport);
            }
        }

        private MeshRepairReview.Part Entry(string path, GameObject preview)
        {
            return new MeshRepairReview.Part
            {
                NodePath = path, Problem = "Synthetic repair approval fixture", Before = _before,
                BeforeHash = MeshRepairUtility.MeshHash(_before), After = _after,
                AfterHash = MeshRepairUtility.MeshHash(_after), Preview = preview
            };
        }

        [Test]
        public void CleanAnalysisCreatesNoReviewAndDoesNotChangeThePrefab()
        {
            var path = AssetDatabase.GetAssetPath(_source);
            var before = File.ReadAllBytes(path);
            Assert.That(MeshRepairUtility.Analyze(_source), Is.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void ColliderGenerationRefusesOpenMeshesWithoutRepairingOrSavingThem()
        {
            _before.triangles = _before.triangles.Skip(3).ToArray();
            EditorUtility.SetDirty(_before);
            AssetDatabase.SaveAssetIfDirty(_before);
            var path = AssetDatabase.GetAssetPath(_source);
            var before = File.ReadAllBytes(path);
            ColliderBatchUtility.Run(new[] { path });
            var report = JArray.Parse(File.ReadAllText(ColliderBatchUtility.BatchReportPath));
            Assert.That((string)report[0]["status"], Is.EqualTo("failed"));
            Assert.That(report[0]["geometryProblems"].Count(), Is.EqualTo(2));
            Assert.That(report[0]["repairReport"], Is.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void ApprovalAppliesOnlyOneMeshAndPreservesOtherPartsAndManualColliders()
        {
            var originalMeshBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(_before));
            var path = AssetDatabase.GetAssetPath(_source);
            var guid = AssetDatabase.AssetPathToGUID(path);
            MeshRepairUtility.Apply(_review, 0);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var a = saved.transform.Find("A");
            var b = saved.transform.Find("B");
            Assert.That(a.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(_after));
            Assert.That(b.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(_before));
            Assert.That(a.Find(PrimitiveColliderBaker.GeneratedRootName), Is.Null);
            Assert.That(b.Find(PrimitiveColliderBaker.GeneratedRootName), Is.Not.Null);
            Assert.That(a.GetComponent<SphereCollider>().radius, Is.EqualTo(0.2f));
            Assert.That(a.localPosition, Is.EqualTo(new Vector3(2f, 3f, 4f)));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(_before)), Is.EqualTo(originalMeshBytes));
            Assert.That(_review.Parts[0].Applied, Is.True);
            Assert.That(_review.Parts[1].Applied, Is.False);
            Assert.Throws<InvalidOperationException>(() => MeshRepairUtility.Apply(_review, 0));
            MeshRepairUtility.Apply(_review, 1);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find("B").GetComponent<MeshFilter>().sharedMesh, Is.SameAs(_after));
            var report = JObject.Parse(File.ReadAllText(_directory + "/Report.json"));
            Assert.That(report["problemParts"].Count(), Is.EqualTo(2));
            Assert.That(report["problemParts"].All(part => (bool)part["Applied"]), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChangedMeshesCannotBeAppliedFromAnOutdatedReview(bool changeAfter)
        {
            var changed = changeAfter ? _after : _before;
            changed.vertices = changed.vertices.Select(point => point * 0.9f).ToArray();
            changed.RecalculateBounds();
            EditorUtility.SetDirty(changed);
            AssetDatabase.SaveAssetIfDirty(changed);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(changed), ImportAssetOptions.ForceUpdate);
            var path = AssetDatabase.GetAssetPath(_source);
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => MeshRepairUtility.Apply(_review, 0));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(_review.Parts[0].Applied, Is.False);
        }

        [Test]
        public void FailedPreviewCannotBeApproved()
        {
            _review.Parts[0].Error = "Collider generation failed";
            var path = AssetDatabase.GetAssetPath(_source);
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => MeshRepairUtility.Apply(_review, 0));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void FullRepairReplacesIndicesAndPreservesMaterialGroupsAndRenderAttributes()
        {
            var originalIndices = _before.triangles;
            var half = originalIndices.Length / 2;
            _before.subMeshCount = 2;
            _before.SetTriangles(originalIndices.Take(half).ToArray(), 0);
            _before.SetTriangles(originalIndices.Skip(half).ToArray(), 1);
            var sources = Enumerable.Range(0, _before.vertexCount).Reverse().ToArray();
            var triangles = originalIndices.Select(index => _before.vertexCount - 1 - index).ToArray();
            var payload = new JObject
            {
                ["mode"] = "full",
                ["vertices"] = JArray.FromObject(sources.Select(i => new[] { _before.vertices[i].x, _before.vertices[i].y, _before.vertices[i].z })),
                ["attributeSources"] = JArray.FromObject(sources),
                ["triangles"] = JArray.FromObject(triangles),
                ["triangleSources"] = JArray.FromObject(Enumerable.Range(0, originalIndices.Length / 3))
            };
            var repaired = MeshRepairUtility.BuildRepairedMesh(_before, payload);
            try
            {
                Assert.That(repaired.subMeshCount, Is.EqualTo(2));
                Assert.That(repaired.GetTriangles(0), Is.EqualTo(triangles.Take(half).ToArray()));
                Assert.That(repaired.GetTriangles(1), Is.EqualTo(triangles.Skip(half).ToArray()));
                Assert.That(repaired.uv, Is.EqualTo(sources.Select(i => _before.uv[i]).ToArray()));
                Assert.That(repaired.normals, Is.EqualTo(sources.Select(i => _before.normals[i]).ToArray()));
                Assert.That(_before.triangles, Is.EqualTo(originalIndices));
            }
            finally
            {
                Object.DestroyImmediate(repaired);
            }
        }

        [Test]
        public void InvalidFullRepairMaterialMappingDoesNotChangeSourceMesh()
        {
            var originalIndices = _before.triangles;
            var payload = new JObject
            {
                ["mode"] = "full",
                ["vertices"] = JArray.FromObject(_before.vertices.Select(v => new[] { v.x, v.y, v.z })),
                ["attributeSources"] = JArray.FromObject(Enumerable.Range(0, _before.vertexCount)),
                ["triangles"] = JArray.FromObject(originalIndices),
                ["triangleSources"] = JArray.FromObject(Enumerable.Repeat(originalIndices.Length, originalIndices.Length / 3))
            };
            Assert.Throws<InvalidOperationException>(() => MeshRepairUtility.BuildRepairedMesh(_before, payload));
            Assert.That(_before.triangles, Is.EqualTo(originalIndices));
        }

        [Test]
        public void LegacyCapPayloadPreservesAllSourceTriangles()
        {
            var payload = new JObject
            {
                ["vertices"] = JArray.FromObject(_before.vertices.Select(v => new[] { v.x, v.y, v.z })),
                ["attributeSources"] = JArray.FromObject(Enumerable.Range(0, _before.vertexCount)),
                ["capTriangles"] = new JArray()
            };
            var repaired = MeshRepairUtility.BuildRepairedMesh(_before, payload);
            try
            {
                Assert.That(repaired.triangles, Is.EqualTo(_before.triangles));
                Assert.That(repaired.uv, Is.EqualTo(_before.uv));
            }
            finally
            {
                Object.DestroyImmediate(repaired);
            }
        }
    }
}
