using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Screw.Bootstrap;
using Screw.Editor.Levels;
using Screw.Gameplay;
using Screw.Levels;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

namespace Screw.Tests.Gameplay
{
    public sealed class BoltTurnIntegrationTests
    {
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";
        private GameplayLifetimeScope _scope;
        private BoltTurnOperations _operations;
        private CollectionView _collection;
        private float _previousTimeScale;
        private Hash128 _catalogHash;
        private readonly Dictionary<Material, string> _materials = new Dictionary<Material, string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            _catalogHash = AssetDatabase.GetAssetDependencyHash(LevelContentCatalogUtility.CatalogAssetPath);
            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
            _materials.Clear();
            foreach (var material in catalog.Palette.SelectMany(color => new[] { color.Primary, color.Secondary }).Distinct())
            {
                _materials.Add(material, EditorJsonUtility.ToJson(material));
            }

            yield return LoadGameplay();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _previousTimeScale;
            Assert.That(AssetDatabase.GetAssetDependencyHash(LevelContentCatalogUtility.CatalogAssetPath), Is.EqualTo(_catalogHash));
            foreach (var material in _materials)
            {
                Assert.That(EditorJsonUtility.ToJson(material.Key), Is.EqualTo(material.Value), material.Key.name);
            }
        }

        [UnityTest]
        public IEnumerator SavedGameplayAssignsBothPaletteSlotsAndHasExactlyNineOriginalBolts()
        {
            var attempt = _scope.GameplaySession.Attempt;
            Assert.That(_scope.GameplayDefinition, Is.SameAs(AssetDatabase.LoadAssetAtPath<LevelGameplayDefinition>(
                LevelGameplayDefinitionUtility.LevelOnePath)));
            Assert.That(_collection.Containers.Count, Is.EqualTo(2));
            Assert.That(_collection.Containers.All(container => container.Slots.Count == 3), Is.True);
            Assert.That(_collection.PoolSlots.Count, Is.EqualTo(5));
            Assert.That(_collection.PoolCount, Is.Zero);
            Assert.That(attempt.Bolts.Values.Count(bolt => bolt.PaletteIndex == 1), Is.EqualTo(6));
            Assert.That(attempt.Bolts.Values.Count(bolt => bolt.PaletteIndex == 2), Is.EqualTo(3));
            AssertMaterialsAndIdentity(attempt);
            AssertStableVisualState(attempt);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DetachmentPrecedesArrivalAndCameraCanMoveDuringTheOriginalBoltFlight()
        {
            var coordinator = _scope.GameplaySession.Coordinator;
            var bolt = _scope.LevelView.GetBolt(0).Node;
            var id = bolt.GetInstanceID();
            var position = bolt.position;
            var axis = bolt.forward;
            var scale = bolt.lossyScale.z;
            var initialAttachments = coordinator.Attempt.RemainingBoltsByPart[0];
            Time.timeScale = 0f;
            var task = coordinator.PlayAsync(0).AsTask();
            var turn = coordinator.Attempt.CurrentTurn;
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.Reserved));
            Assert.That(coordinator.Attempt.RemainingBoltsByPart[0], Is.EqualTo(initialAttachments));
            Assert.That(_collection.Containers[0].Count, Is.Zero);
            Time.timeScale = 1f;
            yield return Wait(() => turn.Phase == TurnPhase.InTransit, "Detachment did not finish.");
            Time.timeScale = 0f;
            Assert.That(coordinator.Attempt.RemainingBoltsByPart[0], Is.EqualTo(initialAttachments - 1));
            Assert.That(_collection.Containers[0].Count, Is.Zero);
            Assert.That(Vector3.Dot(bolt.position - position, axis), Is.GreaterThan(scale));
            var input = _scope.SceneCamera.GetComponent<OrbitCamera>();
            var previous = _scope.SceneCamera.transform.rotation;
            input.Rotate(new Vector2(35f, 25f));
            Assert.That(Quaternion.Angle(previous, _scope.SceneCamera.transform.rotation), Is.GreaterThan(1f));
            var rejected = coordinator.PlayAsync(1).GetAwaiter().GetResult();
            Assert.That(rejected.Outcome, Is.EqualTo(BoltReservationOutcome.TurnInProgress));
            Time.timeScale = 1f;
            yield return Finish(task);
            Assert.That(bolt.GetInstanceID(), Is.EqualTo(id));
            Assert.That(bolt.parent, Is.SameAs(_collection.GetSlot(turn.Destination)));
            Assert.That(Vector3.Distance(bolt.TransformPoint(bolt.GetComponent<BoxCollider>().center), bolt.parent.position),
                Is.LessThan(0.001f), "The head, rather than the shaft pivot, must align with the slot.");
            Assert.That(_collection.Containers[0].Count, Is.EqualTo(1));
            Assert.That(_collection.Containers[0].Panel.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("1/3"));
            CollectionAssert.AreEqual(new[] { 0 }, coordinator.Attempt.DetachedBoltIds);
            AssertMaterialsAndIdentity(coordinator.Attempt);
            AssertStableVisualState(coordinator.Attempt);
        }

        [UnityTest]
        public IEnumerator ProductionPointerRouteCollectsAllNineBoltsAndRepeatsAfterSceneReload()
        {
            for (var pass = 0; pass < 2; pass++)
            {
                _scope.GameplaySession.enabled = true;
                var view = _scope.LevelView;
                var route = LevelRouteData.Read(File.ReadAllText(LevelRouteData.LevelOnePath), view.ReadSource());
                var camera = _scope.SceneCamera;
                var input = camera.GetComponent<OrbitCamera>();
                camera.fieldOfView = route.Camera.FieldOfViewDegrees;
                camera.aspect = route.Camera.Aspect;
                input.FrameLevel();
                foreach (var step in route.Steps)
                {
                    var rotation = (view.LevelRoot.rotation * Quaternion.Euler(LevelRouteData.Vector(step.CameraEulerDegrees))).eulerAngles;
                    input.Rotate(new Vector2(rotation.y - input.Yaw, rotation.x - input.Pitch));
                    Vector2 point = camera.ViewportToScreenPoint(new Vector3(step.ViewportPoint[0], step.ViewportPoint[1], 0f));
                    input.BeginPointer(-1, point);
                    input.UpdatePointer(-1, point, true);
                    var attempt = _scope.GameplaySession.Attempt;
                    Assert.That(attempt.CurrentTurn, Is.Not.Null, "Recorded pointer did not start a turn for " + step.BoltId);
                    Assert.That(attempt.CurrentTurn.BoltId, Is.EqualTo(step.BoltId));
                    yield return Wait(() => attempt.CurrentTurn == null, "Production turn did not finish.");
                    Assert.That(attempt.DetachedBoltIds.Count, Is.EqualTo(step.BoltId + 1));
                    Assert.That(attempt.PoolCount, Is.Zero);
                    AssertMaterialsAndIdentity(attempt);
                    AssertStableVisualState(attempt);
                }

                Assert.That(_scope.GameplaySession.Attempt.Status, Is.EqualTo(AttemptStatus.Won));
                Assert.That(_scope.GameplaySession.Attempt.CollectedBoltIds.Count, Is.EqualTo(9));
                Assert.That(_collection.Containers.All(container => !container.Panel.gameObject.activeSelf), Is.True);
                if (pass == 0)
                {
                    var oldBolts = view.Bolts.Select(bolt => bolt.Node).ToArray();
                    yield return LoadGameplay();
                    Assert.That(oldBolts.All(bolt => bolt == null), Is.True);
                    Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
                    AssertStableVisualState(_scope.GameplaySession.Attempt);
                }
            }
        }

        [UnityTest]
        public IEnumerator RealPoolFlightsReplayEveryChainedReplacementInLogicalOrder()
        {
            var view = _scope.LevelView;
            var source = JsonConvert.DeserializeObject<LevelSourceData>(JsonConvert.SerializeObject(view.ReadSource()));
            source.AllowedBoltPaletteIndices = new[] { 1, 2, 3 };
            var data = LevelGameplayDefinitionUtility.Build(source, Enumerable.Range(0, 9).ToArray());
            var attempt = new LevelAttempt(source, data);
            _operations.Construct(view, attempt, view.ContentCatalog);
            using (var coordinator = new TurnCoordinator(attempt, _operations))
            {
                for (var id = 6; id <= 8; id++)
                {
                    yield return Finish(coordinator.PlayAsync(id).AsTask());
                    Assert.That(_collection.PoolCount, Is.EqualTo(id - 5));
                    Assert.That(view.GetBolt(id).Node.parent, Is.SameAs(_collection.PoolSlots[id - 6]));
                    AssertStableVisualState(attempt);
                }

                yield return Finish(coordinator.PlayAsync(0).AsTask());
                yield return Finish(coordinator.PlayAsync(1).AsTask());
                var task = coordinator.PlayAsync(2).AsTask();
                var sawOriginalFull = false;
                var sawNextGroup = false;
                var deadline = Time.realtimeSinceStartup + 10f;
                while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                {
                    sawOriginalFull |= _collection.Containers[0].GroupId == 0 && _collection.Containers[0].Count == 3;
                    sawNextGroup |= _collection.Containers[0].GroupId == 2;
                    yield return null;
                }

                yield return Finish(task);
                Assert.That(sawOriginalFull, Is.True, "The first full container must visibly leave before replacement.");
                Assert.That(sawNextGroup, Is.True, "The queued color must appear before pool transfers.");
                Assert.That(attempt.CollectedBoltIds.Count, Is.EqualTo(6));
                Assert.That(_collection.PoolCount, Is.Zero);
                Assert.That(_collection.Containers[0].GroupId, Is.Null);
                for (var id = 6; id <= 8; id++)
                {
                    Assert.That(view.GetBolt(id).Node.parent, Is.SameAs(_collection.Containers[0].Slots[id - 6]));
                    Assert.That(view.GetBolt(id).Node.gameObject.activeSelf, Is.False);
                }

                for (var id = 3; id <= 5; id++)
                {
                    yield return Finish(coordinator.PlayAsync(id).AsTask());
                    AssertStableVisualState(attempt);
                }

                Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Won));
                AssertMaterialsAndIdentity(attempt);
            }
        }

        [UnityTest]
        public IEnumerator SceneRestartDuringFlightCancelsOldTweensAndRecreatesTheInitialView()
        {
            var oldScope = _scope;
            var oldBolts = _scope.LevelView.Bolts.Select(bolt => bolt.Node).ToArray();
            _scope.GameplaySession.enabled = true;
            var input = _scope.SceneCamera.GetComponent<OrbitCamera>();
            input.Rotate(new Vector2(-input.Yaw, -input.Pitch));
            Vector2 point = _scope.SceneCamera.WorldToScreenPoint(_scope.LevelView.GetBolt(0).Node.GetComponent<Collider>().bounds.center);
            input.BeginPointer(-1, point);
            input.UpdatePointer(-1, point, true);
            yield return Wait(() => oldScope.GameplaySession.Attempt.CurrentTurn.Phase == TurnPhase.InTransit,
                "Flight did not begin.");
            _scope.Container.Resolve<SceneNavigator>().RestartGameplay();
            yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                "Gameplay did not restart.");
            yield return null;
            BindScope();
            Assert.That(oldBolts.All(bolt => bolt == null), Is.True);
            Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
            Assert.That(_scope.GameplaySession.Attempt.CurrentTurn, Is.Null);
            AssertMaterialsAndIdentity(_scope.GameplaySession.Attempt);
            AssertStableVisualState(_scope.GameplaySession.Attempt);
        }

        private IEnumerator LoadGameplay()
        {
            yield return SceneManager.LoadSceneAsync(GameplayPath, LoadSceneMode.Single);
            yield return null;
            BindScope();
            _scope.GameplaySession.enabled = false;
        }

        private void BindScope()
        {
            _scope = Object.FindFirstObjectByType<GameplayLifetimeScope>();
            _operations = _scope.GetComponent<BoltTurnOperations>();
            _collection = _operations.Collection;
        }

        private void AssertMaterialsAndIdentity(LevelAttempt attempt)
        {
            foreach (var binding in _scope.LevelView.Bolts)
            {
                var color = _scope.LevelView.ContentCatalog.GetColor(attempt.Bolts[binding.Id].PaletteIndex);
                CollectionAssert.AreEqual(new[] { color.Primary, color.Secondary },
                    binding.Node.GetComponentInChildren<MeshRenderer>(true).sharedMaterials, "Bolt " + binding.Id);
            }

            var mesh = _scope.LevelView.GetBolt(0).Node.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Assert.That(Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(filter => filter.sharedMesh == mesh), Is.EqualTo(9), "Flights must move the original objects.");
        }

        private void AssertStableVisualState(LevelAttempt attempt)
        {
            Assert.That(_collection.PoolCount, Is.EqualTo(attempt.PoolCount));
            for (var index = 0; index < 2; index++)
            {
                var model = attempt.Containers[index];
                var view = _collection.Containers[index];
                Assert.That(view.GroupId, Is.EqualTo(model.IsActive ? (int?)model.ColorGroupId : null));
                Assert.That(view.Count, Is.EqualTo(model.Slots.Count(id => id.HasValue)));
                Assert.That(view.Panel.gameObject.activeSelf, Is.EqualTo(model.IsActive));
            }

            foreach (var part in _scope.LevelView.ReadSource().Parts)
            {
                Assert.That(attempt.RemainingBoltsByPart[part.Id], Is.EqualTo(part.BoltIds.Count(id => !attempt.DetachedBoltIds.Contains(id))));
            }
        }

        private static IEnumerator Finish(Task task)
        {
            yield return Wait(() => task.IsCompleted, "Animation task did not finish.");
            task.GetAwaiter().GetResult();
        }

        private static IEnumerator Wait(Func<bool> ready, string message)
        {
            var deadline = Time.realtimeSinceStartup + 10f;
            while (!ready() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(ready(), Is.True, message);
        }
    }
}
