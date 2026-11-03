using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Screw.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Screw.Tests.Levels
{
    public sealed class LevelColliderTests
    {
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";
        private const int TestLayer = 31;
        private const int TestMask = 1 << TestLayer;
        private LevelView _view;
        private PhysicsScene _physics;

        [OneTimeSetUp]
        public void SetUp()
        {
            var previous = SceneManager.GetActiveScene();
            var sourceScene = SceneManager.GetSceneByPath(GameplayPath);
            var openedHere = !sourceScene.IsValid() || !sourceScene.isLoaded;
            try
            {
                if (openedHere)
                {
                    sourceScene = EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Additive);
                }

                Assert.That(sourceScene.isDirty, Is.False, "Save Gameplay before testing its colliders.");
                var source = sourceScene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameplayLifetimeScope>()).Single().LevelView;
                Assert.That(Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                    .Any(collider => collider.gameObject.layer == TestLayer), Is.False, "Fixture layer 31 must be unused.");
                Assert.That(source.GetComponentsInChildren<Collider>(true)
                    .All(collider => (Physics.DefaultRaycastLayers & (1 << collider.gameObject.layer)) != 0), Is.True);
                SceneManager.SetActiveScene(previous);
                _view = Object.Instantiate(source);
                _view.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _view.transform.localScale = Vector3.one;
                _physics = _view.gameObject.scene.GetPhysicsScene();
                _view.ValidateBindings();
                foreach (var collider in _view.GetComponentsInChildren<Collider>(true))
                {
                    collider.gameObject.layer = TestLayer;
                }

                Physics.SyncTransforms();
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }

                if (openedHere && sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(sourceScene, true);
                }
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_view != null)
            {
                Object.DestroyImmediate(_view.gameObject);
            }
        }

        [Test]
        public void SavedCollidersUseExactPartMeshesAndSeparateHeadAndShaftSurfaces()
        {
            var colliders = _view.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders.Length, Is.EqualTo(22));
            Assert.That(_view.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            foreach (var collider in colliders)
            {
                Assert.That(collider, Is.TypeOf<MeshCollider>());
                Assert.That(collider.enabled && collider.gameObject.activeInHierarchy, Is.True);
                Assert.That(collider.isTrigger, Is.False);
                Assert.That(((MeshCollider)collider).convex, Is.False);
                Assert.That(collider.attachedRigidbody, Is.Null);
                Assert.That((Physics.DefaultRaycastLayers & (1 << collider.gameObject.layer)) != 0, Is.True);
            }

            foreach (var part in _view.Parts)
            {
                Assert.That(part.Node.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1));
                Assert.That(part.Node.GetComponent<MeshCollider>().sharedMesh,
                    Is.SameAs(part.Node.GetComponent<MeshFilter>().sharedMesh));
            }

            foreach (var bolt in _view.Bolts)
            {
                var visual = bolt.Node.GetComponentInChildren<MeshFilter>();
                var head = visual.GetComponent<MeshCollider>();
                var shaft = visual.transform.Find("ShaftCollider").GetComponent<MeshCollider>();
                Assert.That(bolt.Node.GetComponentsInChildren<Collider>().Length, Is.EqualTo(2));
                Assert.That(shaft.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(shaft.transform.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(shaft.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(Vector3.Dot(visual.transform.up, bolt.Node.forward), Is.GreaterThan(0.9999f));
                Assert.That(AssetDatabase.GetAssetPath(head.sharedMesh),
                    Is.EqualTo("Assets/Content/Settings/Levels/Colliders/Screw004_Head.asset"));
                Assert.That(AssetDatabase.GetAssetPath(shaft.sharedMesh),
                    Is.EqualTo("Assets/Content/Settings/Levels/Colliders/Screw004_Shaft.asset"));
            }

            var sourceMesh = _view.GetBolt(0).Node.GetComponentInChildren<MeshFilter>().sharedMesh;
            var sourceVertices = sourceMesh.vertices;
            var triangles = sourceMesh.triangles;
            var expectedHead = new List<Vector3>();
            var expectedShaft = new List<Vector3>();
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = sourceVertices[triangles[i]];
                var b = sourceVertices[triangles[i + 1]];
                var c = sourceVertices[triangles[i + 2]];
                var destination = a.y >= 0.02265f && b.y >= 0.02265f && c.y >= 0.02265f ? expectedHead : expectedShaft;
                destination.Add(a);
                destination.Add(b);
                destination.Add(c);
            }

            Assert.That(expectedHead.Count, Is.EqualTo(512 * 3));
            Assert.That(expectedShaft.Count, Is.EqualTo(170 * 3));
            var meshNode = _view.GetBolt(0).Node.GetComponentInChildren<MeshFilter>().transform;
            AssertTriangles(meshNode.GetComponent<MeshCollider>().sharedMesh, expectedHead);
            AssertTriangles(meshNode.Find("ShaftCollider").GetComponent<MeshCollider>().sharedMesh, expectedShaft);
        }

        [TestCase(0, -24f)]
        [TestCase(1, -24f)]
        [TestCase(2, 24f)]
        [TestCase(3, -24f)]
        [TestCase(4, 24f)]
        [TestCase(5, -24f)]
        [TestCase(6, 24f)]
        [TestCase(7, -24f)]
        [TestCase(8, 24f)]
        public void FixedFrontAndBackViewsHitVisibleHeadsFirst(int boltId, float cameraZ)
        {
            var head = Head(boltId);
            Assert.That(Cast(new Vector3(0, 2, cameraZ), head.bounds.center).collider, Is.SameAs(head));
        }

        [TestCase(0, 24f, -1, 2)]
        [TestCase(1, 24f, 0, -1)]
        [TestCase(2, -24f, -1, 0)]
        [TestCase(3, 24f, 1, -1)]
        [TestCase(4, -24f, -1, 3)]
        [TestCase(5, 24f, 2, -1)]
        [TestCase(6, -24f, 2, -1)]
        [TestCase(7, 24f, 3, -1)]
        [TestCase(8, -24f, 3, -1)]
        public void OppositeViewsHitTheActualPartOrOtherBoltBeforeTheHiddenHead(int boltId, float cameraZ, int partId, int otherBoltId)
        {
            var expected = partId >= 0 ? _view.GetPart(partId).Node.GetComponent<MeshCollider>() : Head(otherBoltId);
            Assert.That(Cast(new Vector3(0, 2, cameraZ), Head(boltId).bounds.center).collider, Is.SameAs(expected));
        }

        [TestCase(18f, -18f, 2, 2)]
        [TestCase(-12f, -24f, 2, 1)]
        public void ElevatedAndLowViewsRespectRealOcclusion(float cameraY, float cameraZ, int boltId, int partId)
        {
            var hit = Cast(new Vector3(0, cameraY, cameraZ), Head(boltId).bounds.center);
            Assert.That(hit.collider, Is.SameAs(_view.GetPart(partId).Node.GetComponent<MeshCollider>()));
            Assert.That(Cast(new Vector3(0, cameraY, cameraZ), Head(0).bounds.center).collider, Is.SameAs(Head(0)));
        }

        [TestCase(0, -2.9065f, 0.6504f, 1)]
        [TestCase(1, -1.6243f, 0.7636f, 2)]
        [TestCase(2, -0.58416f, 2.72688f, 0)]
        public void EmptySwirlRegionsDoNotBlockTheSurfaceBehindThem(int emptyPartId, float x, float y, int visiblePartId)
        {
            var ray = new Ray(new Vector3(x, y, -24), Vector3.forward);
            var emptyPart = _view.GetPart(emptyPartId).Node.GetComponent<MeshCollider>();
            Assert.That(emptyPart.bounds.IntersectRay(ray), Is.True);
            Assert.That(emptyPart.Raycast(ray, out _, 60), Is.False);
            Assert.That(Cast(ray.origin, ray.origin + ray.direction).collider,
                Is.SameAs(_view.GetPart(visiblePartId).Node.GetComponent<MeshCollider>()));
        }

        [Test]
        public void EmptySpaceInsideTheAssemblyBoundsStillHitsBackground()
        {
            var ray = new Ray(new Vector3(-3.568f, -0.8528f, -24), Vector3.forward);
            Assert.That(_view.GetPart(0).Node.GetComponent<MeshCollider>().bounds.IntersectRay(ray), Is.True);
            Assert.That(_physics.Raycast(ray.origin, ray.direction, out _, 60,
                TestMask, QueryTriggerInteraction.Ignore), Is.False);
        }

        [Test]
        public void HeadRecessAndShaftRemainSeparateAtTheExportedMeshRotation()
        {
            foreach (var bolt in _view.Bolts)
            {
                var visual = bolt.Node.GetComponentInChildren<MeshFilter>().transform;
                var head = visual.GetComponent<MeshCollider>();
                var slotRay = new Ray(visual.TransformPoint(new Vector3(0, 0.1f, 0)), -visual.up);
                var rimRay = new Ray(visual.TransformPoint(new Vector3(0.015f, 0.1f, 0.015f)), -visual.up);
                Assert.That(head.Raycast(slotRay, out var slot, 10), Is.True);
                Assert.That(head.Raycast(rimRay, out var rim, 10), Is.True);
                Assert.That(visual.InverseTransformPoint(slot.point).y, Is.EqualTo(0.03780564f).Within(0.00001f));
                Assert.That(visual.InverseTransformPoint(rim.point).y, Is.GreaterThan(visual.InverseTransformPoint(slot.point).y + 0.003f));
                var shaftRay = new Ray(visual.TransformPoint(new Vector3(0.1f, 0.005f, 0)), -visual.right);
                Assert.That(head.Raycast(shaftRay, out _, 10), Is.False);
                Assert.That(visual.Find("ShaftCollider").GetComponent<MeshCollider>().Raycast(shaftRay, out _, 10), Is.True);
            }
        }

        private MeshCollider Head(int boltId)
        {
            return _view.GetBolt(boltId).Node.GetComponentInChildren<MeshFilter>().GetComponent<MeshCollider>();
        }

        private RaycastHit Cast(Vector3 origin, Vector3 target)
        {
            Assert.That(_physics.Raycast(origin, (target - origin).normalized, out var hit, 60,
                TestMask, QueryTriggerInteraction.Ignore), Is.True);
            return hit;
        }

        private static void AssertTriangles(Mesh actual, List<Vector3> expected)
        {
            var vertices = actual.vertices;
            CollectionAssert.AreEqual(expected, actual.triangles.Select(index => vertices[index]).ToArray());
        }
    }
}
