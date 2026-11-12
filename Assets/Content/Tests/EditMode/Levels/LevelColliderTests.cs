using System.Linq;
using NUnit.Framework;
using Screw.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Screw.Tests.Levels
{
    public sealed class LevelColliderTests
    {
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";
        private Scene _sourceScene;
        private bool _openedHere;
        private LevelView _view;
        private BoxCollider _baseHead;

        [OneTimeSetUp]
        public void SetUp()
        {
            Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
            var previous = SceneManager.GetActiveScene();
            _sourceScene = SceneManager.GetSceneByPath(GameplayPath);
            _openedHere = !_sourceScene.IsValid() || !_sourceScene.isLoaded;
            try
            {
                if (_openedHere)
                {
                    _sourceScene = EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Additive);
                }

                Assert.That(_sourceScene.isDirty, Is.False, "Save Gameplay before testing its colliders.");
                _view = _sourceScene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameplayLifetimeScope>()).Single().LevelView;
                Assert.That(_view, Is.Not.Null);
                _view.ValidateBindings();
                var catalog = _view.ContentCatalog;
                var source = LevelDataReader.ReadCatalog(catalog.SourceCatalog.text);
                var prefab = catalog.GetColor(source.BoltDefaultPaletteIndex).Prefab;
                Assert.That(prefab, Is.Not.Null);
                _baseHead = prefab.GetComponent<BoxCollider>();
                Assert.That(_baseHead, Is.Not.Null, "The shared head collider must be on the bolt prefab root.");
                Physics.SyncTransforms();
            }
            catch
            {
                TearDown();
                throw;
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_openedHere && _sourceScene.IsValid() && _sourceScene.isLoaded)
            {
                Assert.That(_sourceScene.isDirty, Is.False, "Gameplay became dirty during the tests and was left open to preserve changes.");
                EditorSceneManager.CloseScene(_sourceScene, true);
            }
        }

        [Test]
        public void SharedPrefabUsesOneNonTriggerHeadBoxWithoutRigidbody()
        {
            Assert.That(_baseHead.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1));
            Assert.That(_baseHead.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(_baseHead.enabled && _baseHead.gameObject.activeSelf, Is.True);
            Assert.That(_baseHead.isTrigger, Is.False);
            Assert.That(_baseHead.center.z - _baseHead.size.z * 0.5f, Is.GreaterThan(0f));
            Assert.That(_baseHead.size.z, Is.InRange(0.2f, 0.26f));
            Assert.That(_baseHead.size.x, Is.InRange(0.78f, 0.82f));
            Assert.That(_baseHead.size.y, Is.InRange(0.78f, 0.82f));
        }

        [Test]
        public void SavedGameplayBoltsInheritTheSharedRootColliderWithoutOverrides()
        {
            Assert.That(_view.Bolts, Has.Count.EqualTo(9));
            foreach (var bolt in _view.Bolts)
            {
                AssertInheritedHead(bolt.Node);
            }
        }

        [Test]
        public void EveryCampaignLevelInheritsTheSameHeadCollider()
        {
            Assert.That(_view.ContentCatalog.Levels, Is.Not.Empty);
            foreach (var level in _view.ContentCatalog.Levels)
            {
                var bolts = level.Prefab.transform.Find("Assembly/Bolts");
                Assert.That(bolts, Is.Not.Null, "Level " + level.Number);
                Assert.That(bolts.childCount, Is.GreaterThan(0), "Level " + level.Number);
                foreach (Transform bolt in bolts)
                {
                    AssertInheritedHead(bolt);
                }
            }
        }

        [Test]
        public void SavedGameplayUsesOnlyPrimitiveCollidersWithoutRigidbody()
        {
            var modelPrefabPath = _view.ReadSource().SourcePrefab;
            foreach (var part in _view.Parts)
            {
                var blockers = part.Node.GetComponentsInChildren<Collider>(true);
                Assert.That(blockers, Is.Not.Empty, "Missing blockers on part " + part.Id);
                foreach (var blocker in blockers)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(blocker);
                    Assert.That(source, Is.Not.Null, "Blocker must come from the common model prefab.");
                    Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(modelPrefabPath));
                }
            }

            var colliders = _view.LevelRoot.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders, Is.Not.Empty);
            Assert.That(_view.LevelRoot.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            foreach (var collider in colliders)
            {
                Assert.That(collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider,
                    Is.True, collider.name);
                Assert.That(collider.enabled && collider.gameObject.activeInHierarchy, Is.True, collider.name);
                Assert.That(collider.isTrigger, Is.False, collider.name);
                Assert.That((Physics.DefaultRaycastLayers & (1 << collider.gameObject.layer)) != 0, Is.True, collider.name);
            }
        }

        [TestCase(0f, 0f, true)]
        [TestCase(-0.38f, 0f, true)]
        [TestCase(0.38f, 0f, true)]
        [TestCase(0f, -0.38f, true)]
        [TestCase(0f, 0.38f, true)]
        [TestCase(0.38f, 0.38f, true)]
        [TestCase(-0.42f, 0f, false)]
        [TestCase(0.42f, 0f, false)]
        [TestCase(0f, -0.42f, false)]
        [TestCase(0f, 0.42f, false)]
        public void HeadRaysHitCenterAndEdgesAndMissNearbySpace(float x, float y, bool expected)
        {
            foreach (var bolt in _view.Bolts)
            {
                var head = Head(bolt.Node);
                var didHit = Cast(head, new Vector3(x, y, 1f), new Vector3(x, y, -1f), out var hit);
                Assert.That(didHit, Is.EqualTo(expected), "Bolt " + bolt.Id);
                if (expected)
                {
                    Assert.That(hit.collider, Is.SameAs(head));
                    Assert.That(bolt.Node.InverseTransformPoint(hit.point).z, Is.InRange(0.23f, 0.27f));
                }
            }
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void HeadBoxHasSurfacesOnBothSidesAlongTheBoltAxis(float side)
        {
            foreach (var bolt in _view.Bolts)
            {
                var head = Head(bolt.Node);
                Assert.That(Cast(head, Vector3.forward * side, Vector3.back * side, out var hit),
                    Is.True, "Bolt " + bolt.Id);
                Assert.That(Vector3.Dot(hit.normal, bolt.Node.forward * side), Is.GreaterThan(0.99f));
            }
        }

        [TestCase(-0.5f, false)]
        [TestCase(-0.2f, false)]
        [TestCase(0f, false)]
        [TestCase(0.13f, true)]
        [TestCase(0.3f, false)]
        public void SideRaysHitTheHeadButNotTheShaft(float z, bool expected)
        {
            foreach (var bolt in _view.Bolts)
            {
                Assert.That(Cast(Head(bolt.Node), new Vector3(1f, 0f, z), new Vector3(-1f, 0f, z), out _),
                    Is.EqualTo(expected), "Bolt " + bolt.Id);
            }
        }

        private void AssertInheritedHead(Transform bolt)
        {
            var head = Head(bolt);
            Assert.That(bolt.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1), bolt.name);
            Assert.That(bolt.GetComponentsInChildren<Rigidbody>(true), Is.Empty, bolt.name);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(head), Is.SameAs(_baseHead), bolt.name);
            Assert.That(head.center, Is.EqualTo(_baseHead.center), bolt.name);
            Assert.That(head.size, Is.EqualTo(_baseHead.size), bolt.name);
            Assert.That(head.enabled, Is.EqualTo(_baseHead.enabled), bolt.name);
            Assert.That(head.isTrigger, Is.EqualTo(_baseHead.isTrigger), bolt.name);
            using (var serialized = new SerializedObject(head))
            {
                foreach (var propertyName in new[] { "m_Center", "m_Size", "m_Enabled", "m_IsTrigger" })
                {
                    var property = serialized.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(property.prefabOverride, Is.False, bolt.name + ": " + propertyName);
                }
            }
        }

        private static BoxCollider Head(Transform bolt)
        {
            var head = bolt.GetComponent<BoxCollider>();
            Assert.That(head, Is.Not.Null, "Missing root head collider: " + bolt.name);
            return head;
        }

        private static bool Cast(BoxCollider head, Vector3 from, Vector3 to, out RaycastHit hit)
        {
            var origin = head.transform.TransformPoint(from);
            var direction = head.transform.TransformPoint(to) - origin;
            return head.Raycast(new Ray(origin, direction.normalized), out hit, direction.magnitude);
        }
    }
}
