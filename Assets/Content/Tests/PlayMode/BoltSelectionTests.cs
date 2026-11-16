#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Screw.Tests.Levels
{
    public sealed class BoltSelectionTests
    {
        private const int PreviewPointer = 7;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private Scene _scene;
        private GameObject _root;
        private LevelView _view;
        private Camera _camera;
        private BoltSelection _selection;
        private readonly Dictionary<int, Transform> _nodes = new Dictionary<int, Transform>();
        private readonly List<int> _selectedIds = new List<int>();
        private GameObject _foreignBlocker;

        [SetUp]
        public void SetUp()
        {
            _nodes.Clear();
            _selectedIds.Clear();
            _scene = SceneManager.CreateScene("BoltSelectionTest_" + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _root = new GameObject("SelectionLevel");
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _view = _root.AddComponent<LevelView>();
            var cameraObject = new GameObject("SelectionCamera", typeof(Camera), typeof(OrbitCamera));
            SceneManager.MoveGameObjectToScene(cameraObject, _scene);
            cameraObject.GetComponent<OrbitCamera>().enabled = false;
            _camera = cameraObject.GetComponent<Camera>();
            _camera.enabled = false;
            _camera.pixelRect = new Rect(0f, 0f, 512f, 512f);
            _camera.aspect = 1f;
            _camera.fieldOfView = 60f;
            _camera.transform.position = Vector3.forward * 5f;
            _camera.transform.LookAt(Vector3.zero);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_selection != null)
            {
                _selection.Dispose();
                _selection = null;
            }

            if (_foreignBlocker != null)
            {
                Object.Destroy(_foreignBlocker);
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        [Test]
        public void VisibleFrontHeadReturnsItsBindingAndPublishesOneSelection()
        {
            var node = CreateBolt(42, Vector3.zero);
            PrepareSelection();
            var before = node.position;

            Assert.That(_selection.TrySelect(ScreenPoint(node.position), out var bolt), Is.True);
            Assert.That(bolt, Is.SameAs(_view.GetBolt(42)));
            Assert.That(bolt.OwnerId, Is.EqualTo(13));
            CollectionAssert.AreEqual(new[] { 42 }, _selectedIds);
            Assert.That(node.position, Is.EqualTo(before));
            Assert.That(node.GetComponent<Collider>().enabled, Is.True);
        }

        [Test]
        public void PartInFrontBlocksTheHead()
        {
            CreateBolt(42, Vector3.zero);
            CreateBlocker(Vector3.forward);
            PrepareSelection();
            AssertNoSelection(ScreenPoint(Vector3.zero));
        }

        [Test]
        public void NearestVisibleBoltIsSelectedInsteadOfTheBoltBehindIt()
        {
            CreateBolt(42, Vector3.zero);
            CreateBolt(43, Vector3.forward);
            PrepareSelection();

            Assert.That(_selection.TrySelect(ScreenPoint(Vector3.zero), out var bolt), Is.True);
            Assert.That(bolt.Id, Is.EqualTo(43));
            CollectionAssert.AreEqual(new[] { 43 }, _selectedIds);
        }

        [Test]
        public void BackFacingNearestBoltStillBlocksTheBoltBehindIt()
        {
            CreateBolt(42, Vector3.zero);
            CreateBolt(43, Vector3.forward).rotation = Quaternion.Euler(0f, 180f, 0f);
            PrepareSelection();
            AssertNoSelection(ScreenPoint(Vector3.zero));
        }

        [TestCase(90f)]
        [TestCase(180f)]
        public void SideAndBackFacingHeadsCannotBeSelected(float yaw)
        {
            CreateBolt(42, Vector3.zero).rotation = Quaternion.Euler(0f, yaw, 0f);
            PrepareSelection();
            AssertNoSelection(ScreenPoint(Vector3.zero));
        }

        [Test]
        public void ClickingOnlyAPartDoesNotSelectAnUnrelatedBolt()
        {
            CreateBolt(42, Vector3.right * 2f);
            CreateBlocker(Vector3.zero);
            PrepareSelection();
            AssertNoSelection(ScreenPoint(Vector3.zero));
        }

        [Test]
        public void BackgroundDoesNotPublishASelection()
        {
            CreateBolt(42, Vector3.zero);
            PrepareSelection();
            AssertNoSelection(new Vector2(480f, 480f));
        }

        [Test]
        public void TriggerDoesNotBlockTheHead()
        {
            CreateBolt(42, Vector3.zero);
            CreateBlocker(Vector3.forward).isTrigger = true;
            PrepareSelection();
            Assert.That(_selection.TrySelect(ScreenPoint(Vector3.zero), out var bolt), Is.True);
            Assert.That(bolt.Id, Is.EqualTo(42));
        }

        [Test]
        public void ChildColliderIsAnObstacleAndDoesNotResolveThroughItsParent()
        {
            var node = CreateBolt(42, Vector3.zero);
            CreateBlocker(Vector3.forward).transform.SetParent(node, true);
            PrepareSelection();
            AssertNoSelection(ScreenPoint(Vector3.zero));
        }

        [TestCase(-1f, 256f)]
        [TestCase(513f, 256f)]
        [TestCase(256f, -1f)]
        [TestCase(256f, 513f)]
        public void PointsOutsideTheCameraViewportAreIgnored(float x, float y)
        {
            CreateBolt(42, Vector3.zero);
            PrepareSelection();
            AssertNoSelection(new Vector2(x, y));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ClippedHeadsCannotBeSelected(bool beyondFarPlane)
        {
            CreateBolt(42, Vector3.zero);
            PrepareSelection();
            if (beyondFarPlane)
            {
                _camera.farClipPlane = 3f;
            }
            else
            {
                _camera.nearClipPlane = 6f;
            }

            AssertNoSelection(new Vector2(256f, 256f));
        }

        [Test]
        public void FacingUsesTheBoltLocalForwardUnderARotatedHierarchy()
        {
            var node = CreateBolt(42, Vector3.zero);
            _root.transform.SetPositionAndRotation(new Vector3(3f, 2f, -1f), Quaternion.Euler(45f, 120f, 30f));
            _camera.transform.position = node.position + node.forward * 5f;
            _camera.transform.LookAt(node.position, node.up);
            PrepareSelection();

            Assert.That(_selection.TrySelect(ScreenPoint(node.position), out var bolt), Is.True);
            Assert.That(bolt.Id, Is.EqualTo(42));
        }

        [Test]
        public void ColliderMovementIsSynchronizedBeforeSelection()
        {
            CreateBolt(42, Vector3.zero);
            var blocker = CreateBlocker(Vector3.right * 2f);
            PrepareSelection();
            Assert.That(_selection.TrySelect(ScreenPoint(Vector3.zero), out _), Is.True);
            blocker.transform.position = Vector3.forward;

            Assert.That(_selection.TrySelect(ScreenPoint(Vector3.zero), out var bolt), Is.False);
            Assert.That(bolt, Is.Null);
            CollectionAssert.AreEqual(new[] { 42 }, _selectedIds);
        }

        [Test]
        public void AColliderInAnotherPhysicsSceneDoesNotBlockSelection()
        {
            CreateBolt(42, Vector3.zero);
            _foreignBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _foreignBlocker.transform.position = Vector3.forward;
            PrepareSelection();

            Assert.That(_selection.TrySelect(ScreenPoint(Vector3.zero), out var bolt), Is.True);
            Assert.That(bolt.Id, Is.EqualTo(42));
        }

        [TestCase("release")]
        [TestCase("outside")]
        [TestCase("reset")]
        [TestCase("detached")]
        public void HoldPreviewsOnlyTheOriginalPartAndRestoresItWhenTheGestureEnds(string ending)
        {
            var modelRoot = new GameObject("Model").transform;
            modelRoot.SetParent(_root.transform, false);
            var frontPart = new GameObject("FrontPart").transform;
            frontPart.SetParent(modelRoot, false);
            var front = CreateBlocker(Vector3.back);
            front.transform.SetParent(frontPart, true);
            var side = CreateBlocker(Vector3.right * 1.5f);
            side.transform.SetParent(modelRoot, true);
            var hiddenBolt = CreateBolt(42, Vector3.zero);
            hiddenBolt.rotation = Quaternion.LookRotation(Vector3.back);
            using (var view = new SerializedObject(_view))
            {
                view.FindProperty("_modelRoot").objectReferenceValue = modelRoot;
                var parts = view.FindProperty("_parts");
                parts.arraySize = 2;
                parts.GetArrayElementAtIndex(0).FindPropertyRelative("_id").intValue = 13;
                parts.GetArrayElementAtIndex(0).FindPropertyRelative("_node").objectReferenceValue = frontPart;
                parts.GetArrayElementAtIndex(1).FindPropertyRelative("_id").intValue = 14;
                parts.GetArrayElementAtIndex(1).FindPropertyRelative("_node").objectReferenceValue = side.transform;
                view.ApplyModifiedPropertiesWithoutUndo();
            }

            var orbit = _camera.GetComponent<OrbitCamera>();
            using (var camera = new SerializedObject(orbit))
            {
                camera.FindProperty("_levelRoot").objectReferenceValue = _root.transform;
                camera.FindProperty("_startAngles").vector2Value = Vector2.zero;
                camera.ApplyModifiedPropertiesWithoutUndo();
            }

            orbit.FrameLevel();
            PrepareSelection();
            var tapped = 0;
            orbit.Tapped += _ => tapped++;
            var frontRenderer = front.GetComponent<MeshRenderer>();
            var sideRenderer = side.GetComponent<MeshRenderer>();
            var boltRenderer = hiddenBolt.GetComponent<MeshRenderer>();
            var frontMaterials = frontRenderer.sharedMaterials;
            var sideMaterials = sideRenderer.sharedMaterials;
            var boltMaterials = boltRenderer.sharedMaterials;
            var start = ScreenPoint(front.bounds.center);
            var away = ScreenPoint(side.bounds.center);
            orbit.BeginPointer(PreviewPointer, start);
            SamplePreviewPointer(orbit, start, false, 0.2f);
            var preview = Resources.Load<Material>("PartPreview");
            Assert.That(preview, Is.Not.Null);
            Assert.That(frontRenderer.sharedMaterial, Is.EqualTo(preview));
            Assert.That(_selection.TrySelect(ScreenPoint(hiddenBolt.position), out _), Is.False);

            SamplePreviewPointer(orbit, away, false);
            var previewRotation = Mathf.Abs(Mathf.DeltaAngle(0f, orbit.Yaw));
            Assert.That(previewRotation, Is.GreaterThan(0.01f));
            Assert.That(frontRenderer.sharedMaterial, Is.EqualTo(preview));
            CollectionAssert.AreEqual(sideMaterials, sideRenderer.sharedMaterials);
            CollectionAssert.AreEqual(boltMaterials, boltRenderer.sharedMaterials);

            switch (ending)
            {
                case "release":
                    SamplePreviewPointer(orbit, away, true);
                    break;
                case "outside":
                    SamplePreviewPointer(orbit, new Vector2(-1f, away.y), false);
                    break;
                case "reset":
                    orbit.ResetOrbit();
                    break;
                case "detached":
                    frontPart.SetParent(null, true);
                    SamplePreviewPointer(orbit, away, false);
                    frontPart.SetParent(modelRoot, true);
                    break;
            }

            CollectionAssert.AreEqual(frontMaterials, frontRenderer.sharedMaterials);
            Assert.That(front.enabled, Is.True);
            SamplePreviewPointer(orbit, away, true);
            Assert.That(tapped, Is.Zero);
            var endYaw = orbit.Yaw;
            typeof(OrbitCamera).GetMethod("AdvanceInertia", PrivateInstance).Invoke(orbit, new object[] { 0.2f });
            Assert.That(orbit.Yaw, Is.EqualTo(endYaw));

            orbit.ResetOrbit();
            orbit.BeginPointer(PreviewPointer, start);
            SamplePreviewPointer(orbit, away, false);
            var dragRotation = Mathf.Abs(Mathf.DeltaAngle(0f, orbit.Yaw));
            Assert.That(previewRotation, Is.LessThan(dragRotation * 0.5f));
        }

        [TestCase("missing")]
        [TestCase("trigger")]
        [TestCase("disabled")]
        [TestCase("ignored")]
        [TestCase("inactive")]
        public void InvalidPartBlockingConfigurationFailsExplicitly(string defect)
        {
            CreateBolt(42, Vector3.zero);
            var part = new GameObject("Part13");
            part.transform.SetParent(_root.transform, false);
            if (defect != "missing")
            {
                var collider = part.AddComponent<BoxCollider>();
                collider.isTrigger = defect == "trigger";
                collider.enabled = defect != "disabled";
                if (defect == "ignored")
                {
                    part.layer = 2;
                }
            }

            part.SetActive(defect != "inactive");
            using (var serialized = new SerializedObject(_view))
            {
                var parts = serialized.FindProperty("_parts");
                parts.arraySize = 1;
                var binding = parts.GetArrayElementAtIndex(0);
                binding.FindPropertyRelative("_id").intValue = 13;
                binding.FindPropertyRelative("_node").objectReferenceValue = part.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            Assert.Throws<InvalidOperationException>(() => PrepareSelection());
        }

        private Transform CreateBolt(int id, Vector3 position)
        {
            var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
            node.name = "Bolt_" + id;
            node.transform.SetParent(_root.transform, false);
            node.transform.position = position;
            node.transform.localScale = new Vector3(0.4f, 0.4f, 0.2f);
            _nodes.Add(id, node.transform);
            return node.transform;
        }

        private BoxCollider CreateBlocker(Vector3 position)
        {
            var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
            node.name = "PartBlocker";
            node.transform.SetParent(_root.transform, false);
            node.transform.position = position;
            return node.GetComponent<BoxCollider>();
        }

        private void PrepareSelection()
        {
            using (var serialized = new SerializedObject(_view))
            {
                var bolts = serialized.FindProperty("_bolts");
                bolts.arraySize = _nodes.Count;
                var index = 0;
                foreach (var pair in _nodes)
                {
                    var binding = bolts.GetArrayElementAtIndex(index++);
                    binding.FindPropertyRelative("_id").intValue = pair.Key;
                    binding.FindPropertyRelative("_ownerId").intValue = 13;
                    binding.FindPropertyRelative("_node").objectReferenceValue = pair.Value;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            _selection = new BoltSelection(_view, _camera);
            _selection.Start();
            _selection.Selected += bolt => _selectedIds.Add(bolt.Id);
        }

        private Vector2 ScreenPoint(Vector3 position)
        {
            return _camera.WorldToScreenPoint(position);
        }

        private static void SamplePreviewPointer(OrbitCamera orbit, Vector2 position, bool released, float deltaTime = 1f / 60f)
        {
            typeof(OrbitCamera).GetMethod("UpdatePointer", PrivateInstance)
                .Invoke(orbit, new object[] { PreviewPointer, position, released, deltaTime });
        }

        private void AssertNoSelection(Vector2 point)
        {
            Assert.That(_selection.TrySelect(point, out var bolt), Is.False);
            Assert.That(bolt, Is.Null);
            Assert.That(_selectedIds, Is.Empty);
        }
    }
}
#endif
