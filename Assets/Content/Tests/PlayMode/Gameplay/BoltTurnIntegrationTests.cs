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
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        public IEnumerator LevelCanRotateDuringWithdrawalAndFlightWhileCameraStaysFixed()
        {
            var coordinator = _scope.GameplaySession.Coordinator;
            var bolt = _scope.LevelView.GetBolt(0).Node;
            var id = bolt.GetInstanceID();
            var boltParent = bolt.parent;
            var localDestination = bolt.localPosition + bolt.localRotation * (Vector3.forward * (1.25f * bolt.localScale.z));
            var input = _scope.SceneCamera.GetComponent<OrbitCamera>();
            var cameraPosition = _scope.SceneCamera.transform.position;
            var cameraRotation = _scope.SceneCamera.transform.rotation;
            var initialLevelRotation = input.LevelRoot.rotation;
            var initialAttachments = coordinator.Attempt.RemainingBoltsByPart[0];
            Time.timeScale = 0f;
            var task = coordinator.PlayAsync(0).AsTask();
            var turn = coordinator.Attempt.ActiveTurns.Values.SingleOrDefault();
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.Reserved));
            Assert.That(coordinator.Attempt.RemainingBoltsByPart[0], Is.EqualTo(initialAttachments));
            Assert.That(_collection.Containers[0].Count, Is.Zero);
            input.Rotate(new Vector2(65f, 45f));
            Assert.That(Quaternion.Angle(input.LevelRoot.rotation, initialLevelRotation), Is.GreaterThan(1f));
            Time.timeScale = 1f;
            yield return Wait(() => turn.Phase == TurnPhase.InTransit, "Detachment did not finish.");
            Time.timeScale = 0f;
            Assert.That(coordinator.Attempt.RemainingBoltsByPart[0], Is.EqualTo(initialAttachments - 1));
            Assert.That(_collection.Containers[0].Count, Is.Zero);
            Assert.That(Vector3.Distance(bolt.position, boltParent.TransformPoint(localDestination)), Is.LessThan(0.15f),
                "Withdrawal must follow the rotating assembly's local axis.");
            var flightPosition = bolt.position;
            input.Rotate(new Vector2(35f, 25f));
            Assert.That(bolt.position, Is.EqualTo(flightPosition), "In-flight bolt must no longer inherit assembly rotation.");
            Assert.That(_scope.SceneCamera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(Quaternion.Angle(cameraRotation, _scope.SceneCamera.transform.rotation), Is.LessThan(0.01f));
            var repeated = coordinator.PlayAsync(0).GetAwaiter().GetResult();
            Assert.That(repeated.Outcome, Is.EqualTo(BoltReservationOutcome.AlreadyReserved));
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
                var reference = new LevelAttempt(view.ReadSource(), _scope.GameplayDefinition.Gameplay);
                foreach (var step in route.Steps)
                {
                    var rotation = LevelRouteData.Vector(step.LevelEulerDegrees);
                    input.Rotate(new Vector2(rotation.y - input.Yaw, rotation.x - input.Pitch));
                    Vector2 point = camera.ViewportToScreenPoint(new Vector3(step.ViewportPoint[0], step.ViewportPoint[1], 0f));
                    input.BeginPointer(-1, point);
                    input.UpdatePointer(-1, point, true);
                    var attempt = _scope.GameplaySession.Attempt;
                    Assert.That(attempt.ActiveTurns.Values.SingleOrDefault(), Is.Not.Null, "Recorded pointer did not start a turn for " + step.BoltId);
                    Assert.That(attempt.ActiveTurns.Values.SingleOrDefault().BoltId, Is.EqualTo(step.BoltId));
                    var turn = attempt.ActiveTurns.Values.SingleOrDefault();
                    yield return FinishReleasedTurn(attempt, turn);
                    var expected = reference.ReserveBolt(step.BoltId).Turn;
                    Assert.That(reference.ConfirmDetachment(expected), Is.True);
                    Assert.That(reference.ConfirmArrival(expected), Is.True);
                    Assert.That(reference.CompleteTurn(expected), Is.True);
                    Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(JsonConvert.SerializeObject(reference)),
                        "Production state diverged from the P2 command sequence after bolt " + step.BoltId);

                    Assert.That(attempt.DetachedBoltIds.Count, Is.EqualTo(step.BoltId + 1));
                    Assert.That(attempt.PoolCount, Is.Zero);
                    AssertMaterialsAndIdentity(attempt);
                    AssertStableVisualState(attempt);
                }

                Assert.That(_scope.GameplaySession.Attempt.Status, Is.EqualTo(AttemptStatus.Won));
                Assert.That(_scope.GameplaySession.Attempt.CollectedBoltIds.Count, Is.EqualTo(9));
                Assert.That(_collection.Containers.All(container => !container.Panel.gameObject.activeSelf), Is.True);
                AssertResult("Победа!");
                if (pass == 0)
                {
                    var oldScope = _scope;
                    var oldBolts = view.Bolts.Select(bolt => bolt.Node).ToArray();
                    Click(SessionReference<Button>("_retryButton"));
                    yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                        "Retry after victory did not reload Gameplay.");
                    yield return null;
                    BindScope();
                    Assert.That(oldBolts.All(bolt => bolt == null), Is.True);
                    Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
                    Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.False);
                    AssertStableVisualState(_scope.GameplaySession.Attempt);
                }
            }
        }

        [UnityTest]
        public IEnumerator RapidPointerBurstsCompleteTheLevelWhileSeveralReleasedPartsRemainVisible()
        {
            _scope.GameplaySession.enabled = true;
            var view = _scope.LevelView;
            var attempt = _scope.GameplaySession.Attempt;
            var route = LevelRouteData.Read(File.ReadAllText(LevelRouteData.LevelOnePath), view.ReadSource());
            var camera = _scope.SceneCamera;
            var input = camera.GetComponent<OrbitCamera>();
            camera.aspect = route.Camera.Aspect;
            camera.fieldOfView = route.Camera.FieldOfViewDegrees;
            input.FrameLevel();
            var supported = new List<Rigidbody>();
            for (var batch = 0; batch < 3; batch++)
            {
                Time.timeScale = 0f;
                foreach (var step in route.Steps.Skip(batch * 3).Take(3))
                {
                    var angles = LevelRouteData.Vector(step.LevelEulerDegrees);
                    input.Rotate(new Vector2(angles.y - input.Yaw, angles.x - input.Pitch));
                    Vector2 point = camera.ViewportToScreenPoint(new Vector3(step.ViewportPoint[0], step.ViewportPoint[1], 0f));
                    input.BeginPointer(-1, point);
                    input.UpdatePointer(-1, point, true);
                    Assert.That(attempt.ActiveTurns.ContainsKey(step.BoltId), Is.True, "Tap was rejected for bolt " + step.BoltId);
                    Assert.That(attempt.ActiveTurns[step.BoltId].Phase, Is.EqualTo(TurnPhase.Reserved));
                }

                Assert.That(attempt.ActiveTurns.Count, Is.GreaterThanOrEqualTo(3));
                Time.timeScale = 1f;
                var expected = (batch + 1) * 3;
                yield return Wait(() => attempt.DetachedBoltIds.Count == expected, "Burst did not withdraw all three bolts.");
                if (batch < 2)
                {
                    var body = view.GetPart(batch).Node.GetComponent<Rigidbody>();
                    Assert.That(body.isKinematic, Is.False);
                    body.isKinematic = true;
                    body.position += Vector3.left * 30f;
                    supported.Add(body);
                    Physics.SyncTransforms();
                }
            }

            Assert.That(supported.Count(body => body != null), Is.EqualTo(2));
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(attempt.DetachedBoltIds.Distinct().Count(), Is.EqualTo(9));
            foreach (var body in supported)
            {
                body.isKinematic = false;
                body.WakeUp();
            }

            yield return Wait(() => attempt.Status == AttemptStatus.Won, "All operations and released parts did not finish.");
            Assert.That(attempt.ActiveTurns, Is.Empty);
            Assert.That(attempt.CollectedBoltIds.Count, Is.EqualTo(9));
            Assert.That(view.Parts.All(part => part.Node == null), Is.True);
            AssertStableVisualState(attempt);
            AssertMaterialsAndIdentity(attempt);
            AssertResult("Победа!");
        }

        [UnityTest]
        public IEnumerator RealPoolFlightsReplayEveryChainedReplacementInLogicalOrder()
        {
            var view = _scope.LevelView;
            var source = JsonConvert.DeserializeObject<LevelSourceData>(JsonConvert.SerializeObject(view.ReadSource()));
            source.AllowedBoltPaletteIndices = new[] { 1, 2, 3 };
            var data = LevelGameplayDefinitionUtility.Build(source, Enumerable.Range(0, 9).ToArray());
            var attempt = new LevelAttempt(source, data);
            _operations.Construct(view, attempt, view.ContentCatalog, _scope.Container.Resolve<LevelPartPhysics>());
            using (var coordinator = new TurnCoordinator(attempt, _operations))
            {
                for (var id = 6; id <= 8; id++)
                {
                    yield return PlayAndFinish(coordinator, id);
                    Assert.That(_collection.PoolCount, Is.EqualTo(id - 5));
                    Assert.That(view.GetBolt(id).Node.parent, Is.SameAs(_collection.PoolSlots[id - 6]));
                    AssertStableVisualState(attempt);
                }

                yield return PlayAndFinish(coordinator, 0);
                yield return PlayAndFinish(coordinator, 1);
                var task = coordinator.PlayAsync(2).AsTask();
                var turn = attempt.ActiveTurns.Values.SingleOrDefault();
                var sawOriginalFull = false;
                var sawNextGroup = false;
                var deadline = Time.realtimeSinceStartup + 10f;
                while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                {
                    sawOriginalFull |= _collection.Containers[0].GroupId == 0 && _collection.Containers[0].Count == 3;
                    sawNextGroup |= _collection.Containers[0].GroupId == 2;
                    if (sawNextGroup && _collection.Containers[0].GroupId == null && _collection.PoolCount == 0)
                    {
                        break;
                    }
                    yield return null;
                }

                yield return FinishReleasedTurn(attempt, turn);
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
                    yield return PlayAndFinish(coordinator, id);
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
            yield return Wait(() => oldScope.GameplaySession.Attempt.ActiveTurns.Values.SingleOrDefault().Phase == TurnPhase.InTransit,
                "Flight did not begin.");
            _scope.Container.Resolve<SceneNavigator>().RestartGameplay();
            yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                "Gameplay did not restart.");
            yield return null;
            BindScope();
            Assert.That(oldBolts.All(bolt => bolt == null), Is.True);
            Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
            Assert.That(_scope.GameplaySession.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
            AssertMaterialsAndIdentity(_scope.GameplaySession.Attempt);
            AssertStableVisualState(_scope.GameplaySession.Attempt);
        }

        [UnityTest]
        public IEnumerator PendingRemovalAcceptsRealTapsAllowsDragAndCancelsOnSceneRestart()
        {
            var coordinator = _scope.GameplaySession.Coordinator;
            yield return Finish(coordinator.PlayAsync(0).AsTask());
            yield return Finish(coordinator.PlayAsync(1).AsTask());
            var task = coordinator.PlayAsync(2).AsTask();
            var turn = coordinator.Attempt.ActiveTurns.Values.SingleOrDefault();
            yield return Wait(() => turn.Phase == TurnPhase.InTransit, "Part did not release.");
            var part = _scope.LevelView.GetPart(0).Node;
            part.GetComponent<Rigidbody>().isKinematic = true;
            part.position += Vector3.left * 30f;
            yield return Wait(() => turn.Phase == TurnPhase.AwaitingCompletion, "Arrival did not complete.");
            yield return new WaitForSeconds(1.2f);
            Assert.That(task.IsCompleted, Is.False);

            var camera = _scope.SceneCamera;
            var input = camera.GetComponent<OrbitCamera>();
            var route = LevelRouteData.Read(File.ReadAllText(LevelRouteData.LevelOnePath), _scope.LevelView.ReadSource());
            camera.aspect = route.Camera.Aspect;
            camera.fieldOfView = route.Camera.FieldOfViewDegrees;
            input.FrameLevel();
            var step = route.Steps[3];
            var rotation = LevelRouteData.Vector(step.LevelEulerDegrees);
            input.Rotate(new Vector2(rotation.y - input.Yaw, rotation.x - input.Pitch));
            Vector2 point = camera.ViewportToScreenPoint(new Vector3(step.ViewportPoint[0], step.ViewportPoint[1], 0f));
            _scope.GameplaySession.enabled = true;
            var selection = _scope.Container.Resolve<BoltSelection>();
            var selections = 0;
            Action<LevelBoltBinding> onSelected = bolt =>
            {
                Assert.That(bolt.Id, Is.EqualTo(3));
                selections++;
            };
            selection.Selected += onSelected;
            for (var index = 0; index < 20; index++)
            {
                input.BeginPointer(-1, point);
                input.UpdatePointer(-1, point, true);
            }

            selection.Selected -= onSelected;
            Assert.That(selections, Is.EqualTo(1), "Accepted withdrawal disables the head collider immediately.");
            Assert.That(coordinator.Attempt.ActiveTurns.Keys, Is.EquivalentTo(new[] { 2, 3 }));
            var nextTurn = coordinator.Attempt.ActiveTurns[3];
            Assert.That(nextTurn.Phase, Is.EqualTo(TurnPhase.Reserved));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, coordinator.Attempt.DetachedBoltIds);
            var levelRotation = input.LevelRoot.rotation;
            var cameraPosition = camera.transform.position;
            var cameraRotation = camera.transform.rotation;
            var partPosition = part.position;
            input.BeginPointer(-1, point);
            input.UpdatePointer(-1, point + new Vector2(40f, 20f), true);
            Assert.That(Quaternion.Angle(input.LevelRoot.rotation, levelRotation), Is.GreaterThan(1f));
            Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(camera.transform.rotation, Is.EqualTo(cameraRotation));
            Assert.That(part.position, Is.EqualTo(partPosition));
            Assert.That(coordinator.Attempt.ActiveTurns[2], Is.SameAs(turn));

            var oldScope = _scope;
            _scope.Container.Resolve<SceneNavigator>().RestartGameplay();
            yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                "Gameplay did not restart while waiting for removal.");
            yield return null;
            BindScope();
            Assert.That(task.IsCompleted, Is.True);
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.That(part == null, Is.True);
            Assert.That(_scope.GameplaySession.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
            Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
            Assert.That(_scope.LevelView.Parts.All(binding => binding.Node.parent == _scope.LevelView.ModelRoot
                && binding.Node.GetComponent<Rigidbody>().isKinematic), Is.True);
            yield return null;
            Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty, "Rejected taps must not enter the new attempt.");
            AssertMaterialsAndIdentity(_scope.GameplaySession.Attempt);
            AssertStableVisualState(_scope.GameplaySession.Attempt);
        }

        [UnityTest]
        public IEnumerator RestartButtonInterceptsPointerWithoutRotatingOrSelectingBehindUI()
        {
            _scope.GameplaySession.enabled = true;
            var input = _scope.SceneCamera.GetComponent<OrbitCamera>();
            var button = SessionReference<Button>("_restartButton");
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
            Assert.That(_scope.SceneCamera.pixelRect.Contains(point), Is.True);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.That(hits.Any(hit => hit.gameObject == button.gameObject), Is.True);
            var rotation = input.LevelRoot.rotation;
            var tapped = 0;
            Action<Vector2> onTapped = position => tapped++;
            input.Tapped += onTapped;
            input.BeginPointer(-1, point);
            input.UpdatePointer(-1, point, true);
            input.BeginPointer(-1, point);
            input.UpdatePointer(-1, point + new Vector2(40f, 20f), true);
            input.Tapped -= onTapped;
            Assert.That(tapped, Is.Zero);
            Assert.That(input.LevelRoot.rotation, Is.EqualTo(rotation));
            Assert.That(_scope.GameplaySession.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestartButtonRestoresEveryTurnPhaseAndIgnoresDoubleClicks()
        {
            for (var phase = 0; phase < 4; phase++)
            {
                var session = _scope.GameplaySession;
                session.enabled = true;
                var input = _scope.SceneCamera.GetComponent<OrbitCamera>();
                var initialCameraPosition = _scope.SceneCamera.transform.position;
                var initialFieldOfView = _scope.SceneCamera.fieldOfView;
                var initialAspect = _scope.SceneCamera.aspect;
                var initialPivot = input.Pivot;
                var initialGround = _scope.Ground.position;
                var initialYaw = input.Yaw;
                var initialPitch = input.Pitch;
                if (phase >= 2)
                {
                    yield return Finish(session.PlayAsync(0).AsTask());
                    yield return Finish(session.PlayAsync(1).AsTask());
                }

                Time.timeScale = phase == 0 ? 0f : 1f;
                var task = session.PlayAsync(phase >= 2 ? 2 : 0).AsTask();
                var turn = session.Attempt.ActiveTurns.Values.SingleOrDefault();
                if (phase >= 1)
                {
                    yield return Wait(() => turn.Phase == TurnPhase.InTransit, "Flight did not begin.");
                }

                if (phase >= 2)
                {
                    _scope.LevelView.GetPart(0).Node.GetComponent<Rigidbody>().isKinematic = true;
                    yield return Wait(() => turn.Phase == TurnPhase.AwaitingCompletion, "Sorting did not begin.");
                }

                if (phase == 3)
                {
                    yield return new WaitForSeconds(1.2f);
                    Assert.That(task.IsCompleted, Is.False, "Visible part must keep removal pending.");
                }

                Time.timeScale = 0f;
                input.Rotate(new Vector2(75f, 45f));
                _scope.SceneCamera.fieldOfView = 75f;
                _scope.Ground.position += Vector3.down * 20f;
                var oldScope = _scope;
                var oldNodes = _scope.LevelView.Parts.Select(part => part.Node)
                    .Concat(_scope.LevelView.Bolts.Select(bolt => bolt.Node)).ToArray();
                var restart = SessionReference<Button>("_restartButton");
                Time.timeScale = 1f;
                Click(restart);
                Click(restart);
                Assert.That(session.enabled, Is.False, "Restart must stop receiving selections immediately.");
                yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                    "Restart did not replace Gameplay in phase " + phase);
                yield return null;
                BindScope();
                Assert.That(task.IsCompleted, Is.True);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                Assert.That(oldNodes.All(node => node == null), Is.True);
                Assert.That(_scope.GameplaySession.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
                Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
                Assert.That(_scope.GameplaySession.Attempt.ContainerQueue, Is.EqualTo(new[] { 0 }));
                Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.False);
                Assert.That(SessionReference<Button>("_restartButton").interactable, Is.True);
                Assert.That(_scope.SceneCamera.fieldOfView, Is.EqualTo(initialFieldOfView));
                Assert.That(_scope.SceneCamera.aspect, Is.EqualTo(initialAspect));
                Assert.That(Vector3.Distance(_scope.SceneCamera.transform.position, initialCameraPosition), Is.LessThan(0.001f));
                input = _scope.SceneCamera.GetComponent<OrbitCamera>();
                Assert.That(input.Pivot, Is.EqualTo(initialPivot));
                Assert.That(input.Yaw, Is.EqualTo(initialYaw));
                Assert.That(input.Pitch, Is.EqualTo(initialPitch));
                Assert.That(_scope.Ground.position, Is.EqualTo(initialGround));
                Assert.That(_scope.LevelView.Parts.All(part => part.Node.parent == _scope.LevelView.ModelRoot
                    && part.Node.GetComponent<Rigidbody>().isKinematic), Is.True);
                AssertMaterialsAndIdentity(_scope.GameplaySession.Attempt);
                AssertStableVisualState(_scope.GameplaySession.Attempt);
                yield return new WaitForSeconds(1.2f);
                Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty, "Old callbacks changed the new attempt.");
            }
        }

        [UnityTest]
        public IEnumerator OverflowShowsResultWithoutDetachingTheSixthBoltAndRetryRestoresLevel()
        {
            var view = _scope.LevelView;
            var source = PrepareSortingSource(new[] { 1, 2, 3 });
            var data = new LevelGameplayData
            {
                LevelNumber = source.Number,
                ColorGroups = new[]
                {
                    new ColorGroupData { Id = 0, BoltIds = new[] { 0, 1, 2 } },
                    new ColorGroupData { Id = 1, BoltIds = new[] { 9, 10, 11 } },
                    new ColorGroupData { Id = 2, BoltIds = new[] { 3, 4, 5, 6, 7, 8 } }
                },
                ContainerGroups = new[] { 0, 1, 2, 2 }
            };
            var attempt = new LevelAttempt(source, data);
            _operations.Construct(view, attempt, view.ContentCatalog, _scope.Container.Resolve<LevelPartPhysics>());
            using (var coordinator = new TurnCoordinator(attempt, _operations))
            {
                var session = _scope.GameplaySession;
                session.Construct(coordinator, _scope.Container.Resolve<BoltSelection>(), _scope.Container.Resolve<SceneNavigator>());
                session.enabled = true;
                for (var id = 3; id <= 7; id++)
                {
                    var task = session.PlayAsync(id).AsTask();
                    yield return FinishReleasedTurn(attempt, attempt.ActiveTurns.Values.SingleOrDefault());
                    yield return Finish(task);
                }

                Assert.That(_collection.PoolCount, Is.EqualTo(5));
                Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.False);
                var sixth = view.GetBolt(8).Node;
                var position = sixth.position;
                var parent = sixth.parent;
                var remaining = attempt.RemainingBoltsByPart.ToArray();
                yield return Finish(session.PlayAsync(8).AsTask());
                Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Lost));
                Assert.That(sixth.gameObject.activeSelf && sixth.GetComponent<Collider>().enabled, Is.True);
                Assert.That(sixth.position, Is.EqualTo(position));
                Assert.That(sixth.parent, Is.SameAs(parent));
                CollectionAssert.AreEqual(remaining, attempt.RemainingBoltsByPart.ToArray());
                Assert.That(attempt.DetachedBoltIds, Has.Count.EqualTo(5));
                AssertResult("Нет места");
                var oldScope = _scope;
                Click(SessionReference<Button>("_retryButton"));
                yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                    "Retry after overflow did not reload Gameplay.");
                yield return null;
                BindScope();
                Assert.That(_scope.GameplaySession.Attempt.Status, Is.EqualTo(AttemptStatus.Active));
                Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
                Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.False);
                AssertMaterialsAndIdentity(_scope.GameplaySession.Attempt);
                AssertStableVisualState(_scope.GameplaySession.Attempt);
            }
        }

        [UnityTest]
        public IEnumerator FullPoolRescueTransfersMiddleEntriesAndRestartCancelsTheChain()
        {
            for (var cancel = 0; cancel < 2; cancel++)
            {
                var source = PrepareSortingSource(new[] { 1, 2, 3, 4 });
                var attempt = new LevelAttempt(source,
                    LevelGameplayDefinitionUtility.Build(source, Enumerable.Range(0, 12).ToArray()));
                var view = _scope.LevelView;
                _operations.Construct(view, attempt, view.ContentCatalog, _scope.Container.Resolve<LevelPartPhysics>());
                using (var coordinator = new TurnCoordinator(attempt, _operations))
                {
                    var session = _scope.GameplaySession;
                    session.Construct(coordinator, _scope.Container.Resolve<BoltSelection>(), _scope.Container.Resolve<SceneNavigator>());
                    session.enabled = true;
                    foreach (var id in new[] { 9, 6, 10, 7, 8, 0, 1 })
                    {
                        var pending = session.PlayAsync(id).AsTask();
                        yield return FinishReleasedTurn(attempt, attempt.ActiveTurns.Values.SingleOrDefault());
                        yield return Finish(pending);
                    }

                    CollectionAssert.AreEqual(new int?[] { 9, 6, 10, 7, 8 }, attempt.PoolSlots);
                    Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
                    Assert.That(_collection.PoolCount, Is.EqualTo(5));
                    Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.False);
                    var task = session.PlayAsync(2).AsTask();
                    var turn = attempt.ActiveTurns.Values.SingleOrDefault();
                    yield return Wait(() => turn.Phase == TurnPhase.AwaitingCompletion, "Rescue did not begin sorting.");
                    var transfers = turn.SortingSteps.OfType<PoolTransfer>().ToArray();
                    CollectionAssert.AreEqual(new[] { 6, 7, 8, 9, 10 }, transfers.Select(step => step.BoltId));
                    CollectionAssert.AreEqual(new[] { 1, 3, 4, 0, 2 }, transfers.Select(step => step.PoolSlotIndex));
                    CollectionAssert.AreEqual(new int?[] { 2, 3 },
                        turn.SortingSteps.OfType<ContainerChange>().Select(step => step.NextColorGroupId));
                    if (cancel == 0)
                    {
                        yield return FinishReleasedTurn(attempt, turn);
                        yield return Finish(task);
                        Assert.That(_collection.PoolCount, Is.Zero);
                        Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
                        Assert.That(_collection.Containers[0].GroupId, Is.EqualTo(3));
                        Assert.That(_collection.Containers[0].Count, Is.EqualTo(2));
                        Assert.That(attempt.ContainerQueue, Is.Empty);
                        foreach (var id in new[] { 0, 1, 2, 6, 7, 8 })
                        {
                            Assert.That(view.GetBolt(id).Node.gameObject.activeSelf, Is.False, "Collected bolt " + id);
                        }

                        for (var index = 0; index < 2; index++)
                        {
                            Assert.That(view.GetBolt(9 + index).Node.parent, Is.SameAs(_collection.Containers[0].Slots[index]));
                        }
                    }
                    else
                    {
                        yield return Wait(() => _collection.Containers[0].GroupId == 2 && _collection.PoolCount == 4,
                            "The first transfer from the middle of the pool did not start.");
                        Assert.That(task.IsCompleted, Is.False);
                        Assert.That(view.GetBolt(6).Node.parent, Is.SameAs(_scope.SceneCamera.transform));
                    }

                    var oldScope = _scope;
                    var oldNodes = view.Bolts.Select(bolt => bolt.Node).Concat(view.Parts.Select(part => part.Node)).ToArray();
                    Click(SessionReference<Button>("_restartButton"));
                    yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                        "Restart did not clear the sorting fixture.");
                    yield return null;
                    BindScope();
                    Assert.That(oldNodes.All(node => node == null), Is.True);
                    if (cancel != 0)
                    {
                        Assert.That(task.IsCompleted, Is.True);
                        Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                    }

                    yield return new WaitForSeconds(1.2f);
                    Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
                    Assert.That(_scope.LevelView.Bolts, Has.Count.EqualTo(9));
                    Assert.That(_scope.LevelView.Parts.All(part => part.Node != null), Is.True);
                    AssertMaterialsAndIdentity(_scope.GameplaySession.Attempt);
                    AssertStableVisualState(_scope.GameplaySession.Attempt);
                }
            }
        }

        private LevelSourceData PrepareSortingSource(int[] palette)
        {
            _scope.GameplaySession.enabled = false;
            var view = _scope.LevelView;
            var source = JsonConvert.DeserializeObject<LevelSourceData>(JsonConvert.SerializeObject(view.ReadSource()));
            source.AllowedBoltPaletteIndices = palette;
            var owner = source.Parts.Last();
            source.Bolts = source.Bolts.Concat(Enumerable.Range(9, 3).Select(id => new BoltSourceData
            {
                Id = id, OwnerId = owner.Id, Position = new double[3], EulerDegrees = new double[3],
                SourceWhitelist = "", NodePath = "Assembly/Bolts/FixtureBolt_" + id
            })).ToArray();
            owner.BoltIds = source.Bolts.Where(bolt => bolt.OwnerId == owner.Id).Select(bolt => bolt.Id).ToArray();
            using (var serialized = new SerializedObject(view))
            {
                var bolts = serialized.FindProperty("_bolts");
                bolts.arraySize = 12;
                for (var id = 9; id < 12; id++)
                {
                    var clone = Object.Instantiate(view.GetBolt(0).Node.gameObject, view.BoltsRoot);
                    clone.name = "FixtureBolt_" + id;
                    var binding = bolts.GetArrayElementAtIndex(id);
                    binding.FindPropertyRelative("_id").intValue = id;
                    binding.FindPropertyRelative("_ownerId").intValue = owner.Id;
                    binding.FindPropertyRelative("_node").objectReferenceValue = clone.transform;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return source;
        }

        private void AssertResult(string text)
        {
            Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.True);
            Assert.That(SessionReference<Text>("_resultLabel").text, Is.EqualTo(text));
            Assert.That(SessionReference<Button>("_retryButton").IsInteractable(), Is.True);
            Assert.That(SessionReference<Button>("_restartButton").gameObject.activeSelf, Is.False);
        }

        private T SessionReference<T>(string field) where T : Object
        {
            using (var serialized = new SerializedObject(_scope.GameplaySession))
            {
                return (T)serialized.FindProperty(field).objectReferenceValue;
            }
        }

        private static void Click(Button button)
        {
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            }, ExecuteEvents.pointerClickHandler);
        }

        private IEnumerator PlayAndFinish(TurnCoordinator coordinator, int boltId)
        {
            var task = coordinator.PlayAsync(boltId).AsTask();
            yield return FinishReleasedTurn(coordinator.Attempt, coordinator.Attempt.ActiveTurns.Values.SingleOrDefault());
            yield return Finish(task);
        }

        private IEnumerator FinishReleasedTurn(LevelAttempt attempt, AttemptTurn turn)
        {
            yield return Wait(() => turn.Phase != TurnPhase.Reserved, "Bolt did not detach.");
            if (turn.ReleasedPartId.HasValue)
            {
                var part = _scope.LevelView.GetPart(turn.ReleasedPartId.Value).Node;
                var input = _scope.SceneCamera.GetComponent<OrbitCamera>();
                var releasePoses = new[]
                {
                    Vector2.zero, new Vector2(90f, 0f), new Vector2(180f, 0f), new Vector2(270f, 0f),
                    new Vector2(0f, 90f), new Vector2(0f, 180f), new Vector2(0f, 270f),
                    new Vector2(90f, 90f), new Vector2(180f, 90f), new Vector2(270f, 90f),
                    new Vector2(90f, 180f), new Vector2(180f, 180f), new Vector2(270f, 180f),
                    new Vector2(90f, 270f), new Vector2(180f, 270f), new Vector2(270f, 270f)
                };
                foreach (var pose in releasePoses)
                {
                    if (part == null)
                    {
                        break;
                    }

                    Assert.That(attempt.ActiveTurns.Values.SingleOrDefault(), Is.SameAs(turn), "A visible part must keep its turn locked.");
                    Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active), "Victory must wait for actual removal.");
                    input.Rotate(new Vector2(pose.x - input.Yaw, pose.y - input.Pitch));
                    yield return new WaitForSeconds(0.8f);
                }

                yield return Wait(() => part == null, "Released part did not exit after the recorded release motion.");
            }

            yield return Wait(() => attempt.ActiveTurns.Values.SingleOrDefault() == null, "Production turn did not finish.");
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
