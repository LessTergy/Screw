using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Screw.Editor.Levels;
using Screw.Gameplay;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

namespace Screw.Tests.Gameplay
{
    public sealed class LevelPartPhysicsTests
    {
        private GameplayLifetimeScope _scope;
        private Scene _fixtureScene;
        private LevelView _fixtureView;
        private LevelPartPhysics _fixturePhysics;
        private Transform _fixtureGround;
        private float _fixtureSimulationTime;
        private readonly List<GameObject> _fixtureBolts = new List<GameObject>();
        private float _previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("Assets/Content/Scenes/Gameplay.unity", LoadSceneMode.Single);
            yield return null;
            _scope = Object.FindFirstObjectByType<GameplayLifetimeScope>();
            _scope.GameplaySession.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _previousTimeScale;
            _fixturePhysics?.Dispose();
            _fixturePhysics = null;
            foreach (var bolt in _fixtureBolts)
            {
                if (bolt != null)
                {
                    Object.Destroy(bolt);
                }
            }

            _fixtureBolts.Clear();
            if (_fixtureScene.IsValid() && _fixtureScene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_fixtureScene);
            }
        }

        [UnityTest]
        public IEnumerator RealLevelKeepsSourcePoseAndPrimitiveShapesWhilePinned()
        {
            var view = _scope.LevelView;
            view.ValidateBindings();
            var positions = view.Parts.Select(part => part.Node.position).ToArray();
            var rotations = view.Parts.Select(part => part.Node.rotation).ToArray();
            var scales = view.Parts.Select(part => part.Node.lossyScale).ToArray();
            var sourceModel = view.ContentCatalog.GetLevel(1).Prefab.transform.Find("Assembly/Model");
            var sourceShapes = sourceModel.GetComponentsInChildren<BoxCollider>();
            Assert.That(sourceShapes, Is.Not.Empty);
            Assert.That(view.ModelRoot.GetComponentsInChildren<BoxCollider>().Length, Is.EqualTo(sourceShapes.Length));
            Assert.That(_scope.Ground.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(_scope.Ground.GetComponent<MeshCollider>(), Is.Null);
            var released = LayerMask.NameToLayer("ReleasedParts");
            for (var layer = 0; layer < 32; layer++)
            {
                Assert.That(Physics.GetIgnoreLayerCollision(released, layer),
                    Is.EqualTo(layer != released && layer != LayerMask.NameToLayer("LevelGround")
                        && layer != LayerMask.NameToLayer("AttachedParts")), "Layer " + layer);
            }

            yield return new WaitForSeconds(0.1f);
            for (var index = 0; index < view.Parts.Count; index++)
            {
                var node = view.Parts[index].Node;
                AssertPinned(node, positions[index], rotations[index]);
                Assert.That(node.lossyScale, Is.EqualTo(scales[index]));
                foreach (var collider in node.GetComponentsInChildren<Collider>())
                {
                    Assert.That(collider, Is.TypeOf<BoxCollider>());
                    Assert.That(collider.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("AttachedParts")));
                    Assert.That(collider.attachedRigidbody, Is.SameAs(node.GetComponent<Rigidbody>()));
                }
            }
        }

        [UnityTest]
        public IEnumerator PinnedPartsAndBoltsStayAlignedAcrossRenderedRotations()
        {
            var view = _scope.LevelView;
            var orbit = _scope.SceneCamera.GetComponent<OrbitCamera>();
            var nodes = view.Parts.Select(part => part.Node).Concat(view.Bolts.Select(bolt => bolt.Node)).ToArray();
            var positions = nodes.Select(node => view.transform.InverseTransformPoint(node.position)).ToArray();
            var rotations = nodes.Select(node => Quaternion.Inverse(view.transform.rotation) * node.rotation).ToArray();
            for (var step = 0; step < 12; step++)
            {
                orbit.Rotate(new Vector2(17f, 11f));
                yield return new WaitForSeconds(0.04f);
                yield return null;
                for (var index = 0; index < nodes.Length; index++)
                {
                    Assert.That(Vector3.Distance(view.transform.InverseTransformPoint(nodes[index].position), positions[index]),
                        Is.LessThan(0.0001f), nodes[index].name);
                    Assert.That(Quaternion.Angle(Quaternion.Inverse(view.transform.rotation) * nodes[index].rotation, rotations[index]),
                        Is.LessThan(0.01f), nodes[index].name);
                }
            }
        }

        [UnityTest]
        public IEnumerator RealPartStaysWhileVisibleAndIsDeletedOnlyBelowView()
        {
            var coordinator = _scope.GameplaySession.Coordinator;
            var view = _scope.LevelView;
            var owner = view.GetPart(0).Node;
            var body = owner.GetComponent<Rigidbody>();
            var position = owner.position;
            var rotation = owner.rotation;
            var scale = owner.lossyScale;
            var neighbors = view.Parts.Skip(1).Select(part => part.Node).ToArray();
            var neighborPositions = neighbors.Select(node => node.position).ToArray();
            var neighborRotations = neighbors.Select(node => node.rotation).ToArray();
            var pinnedBolts = view.Bolts.Skip(3).Select(bolt => bolt.Node).ToArray();
            var boltPositions = pinnedBolts.Select(node => node.position).ToArray();
            for (var id = 0; id < 2; id++)
            {
                yield return Finish(coordinator.PlayAsync(id).AsTask());
                AssertPinned(owner, position, rotation);
            }

            Time.timeScale = 0f;
            var task = coordinator.PlayAsync(2).AsTask();
            var turn = coordinator.Attempt.CurrentTurn;
            yield return null;
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.Reserved));
            AssertPinned(owner, position, rotation);
            Time.timeScale = 1f;
            yield return Wait(() => turn.Phase == TurnPhase.InTransit);
            Assert.That(turn.ReleasedPartId, Is.EqualTo(0));
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.useGravity, Is.True);
            Assert.That(Vector3.Distance(owner.lossyScale, scale), Is.LessThan(0.0001f));
            Assert.That(owner.parent, Is.Null);
            Assert.That(owner.gameObject.scene, Is.EqualTo(_scope.gameObject.scene));
            yield return new WaitForSeconds(0.15f);
            Assert.That(owner, Is.Not.Null);
            body.isKinematic = true;
            yield return new WaitForSeconds(1.1f);
            Assert.That(owner != null, Is.True, "A visible part must not expire after one second.");
            Assert.That(task.IsCompleted, Is.False, "Sorting must not unlock a turn while its part remains visible.");
            Assert.That(coordinator.Attempt.CurrentTurn, Is.SameAs(turn));
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.AwaitingCompletion));
            Assert.That(coordinator.PlayAsync(3).GetAwaiter().GetResult().Outcome, Is.EqualTo(BoltReservationOutcome.TurnInProgress));
            owner.position += Vector3.down * 100f;
            Physics.SyncTransforms();
            yield return Finish(task);
            Assert.That(owner == null, Is.True);
            for (var index = 0; index < neighbors.Length; index++)
            {
                AssertPinned(neighbors[index], neighborPositions[index], neighborRotations[index]);
            }

            for (var index = 0; index < pinnedBolts.Length; index++)
            {
                Assert.That(pinnedBolts[index].position, Is.EqualTo(boltPositions[index]));
            }
        }

        [UnityTest]
        public IEnumerator CompoundBodyRestsOnPinnedNeighborAndFallsAfterAssemblyRotation()
        {
            CreateFixture(false);
            Time.timeScale = 0f;
            var body = _fixtureView.GetPart(0).Node.GetComponent<Rigidbody>();
            var contacts = body.gameObject.AddComponent<PartContactProbe>();
            var neighbor = _fixtureView.GetPart(1).Node;
            var position = neighbor.position;
            var rotation = neighbor.rotation;
            var task = _fixturePhysics.ReleaseAsync(0, CancellationToken.None).AsTask();
            Assert.That(Vector3.Distance(body.worldCenterOfMass, body.position), Is.GreaterThan(0.5f),
                "The shared pivot must not be used as the body's center of mass.");
            Assert.That(body.inertiaTensor.x, Is.GreaterThan(0f));
            var physics = _fixtureScene.GetPhysicsScene();
            for (var step = 0; step < 100; step++)
            {
                physics.Simulate(0.02f);
            }

            var bounds = BoundsOf(body.transform);
            Assert.That(bounds.min.y, Is.GreaterThan(neighbor.GetComponent<Collider>().bounds.max.y - 0.05f),
                "Pinned neighbor must support the released part.");
            AssertPinned(neighbor, position, rotation);
            Assert.That(contacts.Contacts.Any(collider => collider.gameObject.layer == LayerMask.NameToLayer("LevelGround")), Is.False);
            Assert.That(contacts.Contacts.Contains(neighbor.GetComponent<Collider>()), Is.True);

            var releasedPosition = body.position;
            var releasedRotation = body.rotation;
            var neighborLocalPosition = neighbor.localPosition;
            var neighborLocalRotation = neighbor.localRotation;
            _fixtureView.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            Assert.That(body.position, Is.EqualTo(releasedPosition));
            Assert.That(body.rotation, Is.EqualTo(releasedRotation));
            Physics.SyncTransforms();
            var groundCollider = _fixtureGround.GetComponent<BoxCollider>();
            for (var step = 0; step < 500 && !contacts.Contacts.Contains(groundCollider); step++)
            {
                physics.Simulate(0.02f);
            }

            Assert.That(contacts.Contacts.Contains(groundCollider), Is.True,
                "Rotation must remove the support and let the part reach the ground. Body: " + body.position
                + "; bounds: " + BoundsOf(body.transform) + "; ground: " + _fixtureGround.position
                + "; camera aspect: " + _scope.SceneCamera.aspect);
            Assert.That(BoundsOf(body.transform).min.y,
                Is.GreaterThan(_fixtureGround.GetComponent<BoxCollider>().bounds.max.y - 0.05f));
            Assert.That(neighbor.localPosition, Is.EqualTo(neighborLocalPosition));
            Assert.That(Quaternion.Angle(neighbor.localRotation, neighborLocalRotation), Is.LessThan(0.01f));
            Assert.That(neighbor.GetComponent<Rigidbody>().isKinematic, Is.True);

            contacts.Contacts.Clear();
            neighbor.SetPositionAndRotation(body.worldCenterOfMass + Vector3.up * 2.5f, Quaternion.identity);
            var second = _fixturePhysics.ReleaseAsync(1, CancellationToken.None).AsTask();
            for (var step = 0; step < 100; step++)
            {
                physics.Simulate(0.02f);
            }

            Assert.That(contacts.Contacts.Contains(neighbor.GetComponent<Collider>()), Is.True,
                "Released parts must produce real collision contacts with each other.");
            Time.timeScale = 1f;
            yield return Finish(task);
            yield return Finish(second);
            Assert.That(body == null && neighbor == null, Is.True);
        }

        [UnityTest]
        public IEnumerator ReleasedRealPartKeepsWorldPoseAndScaleWhenAssemblyRotates()
        {
            var view = _scope.LevelView;
            var owner = view.GetPart(0).Node;
            var position = owner.position;
            var rotation = owner.rotation;
            var scale = owner.lossyScale;
            var colliders = owner.GetComponentsInChildren<Collider>();
            Physics.SyncTransforms();
            var bounds = colliders.Select(collider => collider.bounds).ToArray();
            var neighbor = view.GetPart(1).Node;
            var neighborPosition = neighbor.localPosition;
            var neighborRotation = neighbor.localRotation;
            var bolt = view.GetBolt(3).Node;
            var boltPosition = bolt.localPosition;
            var boltRotation = bolt.localRotation;
            Time.timeScale = 0f;
            var task = _scope.Container.Resolve<LevelPartPhysics>().ReleaseAsync(0, CancellationToken.None).AsTask();
            Assert.That(owner.GetComponent<Rigidbody>().interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
            Assert.That(owner.parent, Is.Null);
            Assert.That(owner.gameObject.scene, Is.EqualTo(_scope.gameObject.scene));
            Assert.That(Vector3.Distance(owner.position, position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(owner.rotation, rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(owner.lossyScale, scale), Is.LessThan(0.0001f));
            _scope.SceneCamera.GetComponent<OrbitCamera>().Rotate(new Vector2(105f, 60f));
            Physics.SyncTransforms();
            Assert.That(Vector3.Distance(owner.position, position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(owner.rotation, rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(owner.lossyScale, scale), Is.LessThan(0.0001f));
            for (var index = 0; index < colliders.Length; index++)
            {
                Assert.That(Vector3.Distance(colliders[index].bounds.center, bounds[index].center), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(colliders[index].bounds.size, bounds[index].size), Is.LessThan(0.0001f));
            }

            Time.timeScale = 1f;
            owner.position += Vector3.down * 100f;
            Physics.SyncTransforms();
            yield return Finish(task);
            Assert.That(neighbor.GetComponent<Rigidbody>().isKinematic, Is.True);
            Assert.That(neighbor.localPosition, Is.EqualTo(neighborPosition));
            Assert.That(Quaternion.Angle(neighbor.localRotation, neighborRotation), Is.LessThan(0.01f));
            Assert.That(bolt.localPosition, Is.EqualTo(boltPosition));
            Assert.That(Quaternion.Angle(bolt.localRotation, boltRotation), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator GravityStaysWorldDownInDifferentAssemblyOrientations()
        {
            CreateFixture(false);
            Time.timeScale = 0f;
            _fixtureView.transform.position = Vector3.up * 15f;
            _fixtureView.transform.localScale = Vector3.one * 1.4f;
            var physics = _fixtureScene.GetPhysicsScene();
            var gravity = Physics.gravity;
            var tasks = new List<Task>();
            var orientations = new[] { new Vector3(35f, 140f, 70f), new Vector3(180f, 25f, 0f) };
            for (var id = 0; id < 2; id++)
            {
                _fixtureView.transform.rotation = Quaternion.Euler(orientations[id]);
                _fixtureView.transform.position += Vector3.right * 10f;
                var body = _fixtureView.GetPart(id).Node.GetComponent<Rigidbody>();
                tasks.Add(_fixturePhysics.ReleaseAsync(id, CancellationToken.None).AsTask());
                var center = body.worldCenterOfMass;
                for (var step = 0; step < 10; step++)
                {
                    physics.Simulate(0.02f);
                }

                Assert.That(body.worldCenterOfMass.y, Is.LessThan(center.y - 0.05f));
                Assert.That(body.worldCenterOfMass.x, Is.EqualTo(center.x).Within(0.001f));
                Assert.That(body.worldCenterOfMass.z, Is.EqualTo(center.z).Within(0.001f));
                Assert.That(Physics.gravity, Is.EqualTo(gravity));
            }

            Time.timeScale = 1f;
            foreach (var task in tasks)
            {
                yield return Finish(task);
            }
        }

        [UnityTest]
        public IEnumerator LastBoltGoingToPoolStillReleasesItsOwner()
        {
            var attempt = CreateFixture(false);
            using (var coordinator = new TurnCoordinator(attempt, _scope.GetComponent<BoltTurnOperations>()))
            {
                var owner = _fixtureView.GetPart(0).Node;
                var neighbor = _fixtureView.GetPart(1).Node;
                var task = coordinator.PlayAsync(6).AsTask();
                var turn = attempt.CurrentTurn;
                Assert.That(turn.Destination.IsPool, Is.True);
                yield return Wait(() => turn.Phase == TurnPhase.InTransit);
                Assert.That(owner.GetComponent<Rigidbody>().isKinematic, Is.False);
                Assert.That(attempt.RemainingBoltsByPart[0], Is.Zero);
                Assert.That(attempt.PoolCount, Is.EqualTo(1));
                yield return Wait(() => turn.Phase == TurnPhase.AwaitingCompletion);
                Assert.That(_fixtureBolts[6].transform.parent,
                    Is.SameAs(_scope.GetComponent<BoltTurnOperations>().Collection.PoolSlots[0]));
                _fixtureView.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                yield return Finish(task);
                Assert.That(owner == null, Is.True);
                Assert.That(neighbor.GetComponent<Rigidbody>().isKinematic, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator SupportedPartKeepsTurnLockedUntilRotationLetsItFall()
        {
            var attempt = CreateFixture(false);
            var owner = _fixtureView.GetPart(0).Node;
            var contacts = owner.gameObject.AddComponent<PartContactProbe>();
            var neighbor = _fixtureView.GetPart(1).Node;
            using (var coordinator = new TurnCoordinator(attempt, _scope.GetComponent<BoltTurnOperations>()))
            {
                var task = coordinator.PlayAsync(6).AsTask();
                var turn = attempt.CurrentTurn;
                yield return Wait(() => turn.Phase == TurnPhase.AwaitingCompletion);
                for (var step = 0; step < 150; step++)
                {
                    _fixtureScene.GetPhysicsScene().Simulate(0.02f);
                }

                Assert.That(contacts.Contacts.Contains(neighbor.GetComponent<Collider>()), Is.True);
                yield return new WaitForSeconds(1.2f);
                Assert.That(owner != null && !task.IsCompleted, Is.True);
                Assert.That(attempt.CurrentTurn, Is.SameAs(turn));
                for (var index = 0; index < 20; index++)
                {
                    Assert.That(coordinator.PlayAsync(0).GetAwaiter().GetResult().Outcome,
                        Is.EqualTo(BoltReservationOutcome.TurnInProgress));
                }

                _fixtureView.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                yield return Finish(task);
                Assert.That(owner == null && attempt.CurrentTurn == null, Is.True);
                yield return null;
                CollectionAssert.AreEqual(new[] { 6 }, attempt.DetachedBoltIds, "Blocked input must not run after unlock.");
                yield return Finish(coordinator.PlayAsync(0).AsTask());
                CollectionAssert.AreEqual(new[] { 6, 0 }, attempt.DetachedBoltIds);
            }
        }

        [UnityTest]
        public IEnumerator ElevenAttachmentsRequireElevenCompletedDetachments()
        {
            var attempt = CreateFixture(true);
            using (var coordinator = new TurnCoordinator(attempt, _scope.GetComponent<BoltTurnOperations>()))
            {
                var owner = _fixtureView.GetPart(0).Node;
                var position = owner.position;
                var rotation = owner.rotation;
                for (var id = 0; id < 10; id++)
                {
                    yield return Finish(coordinator.PlayAsync(id).AsTask());
                    Assert.That(attempt.RemainingBoltsByPart[0], Is.EqualTo(10 - id));
                    AssertPinned(owner, position, rotation);
                }

                var task = coordinator.PlayAsync(10).AsTask();
                var turn = attempt.CurrentTurn;
                AssertPinned(owner, position, rotation);
                yield return Wait(() => turn.Phase == TurnPhase.InTransit);
                Assert.That(turn.ReleasedPartId, Is.EqualTo(0));
                Assert.That(owner.GetComponent<Rigidbody>().isKinematic, Is.False);
                _fixtureView.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                yield return Finish(task);
                Assert.That(owner == null, Is.True);
                Assert.That(_fixtureView.GetPart(1).Node.GetComponent<Rigidbody>().isKinematic, Is.True);
                Assert.That(attempt.RemainingBoltsByPart[1], Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator RemovalRequiresTheWholePartStrictlyBelowTheBoundary()
        {
            var body = PrepareRemovalProbe();
            var task = _fixturePhysics.ReleaseAsync(0, CancellationToken.None).AsTask();
            body.isKinematic = true;
            var boundary = _scope.SceneCamera.ViewportToWorldPoint(new Vector3(0.5f, 0f, 11f)).y - 1f;
            MoveTopTo(body.transform, boundary + 0.05f);
            yield return null;
            yield return null;
            Assert.That(body != null && !task.IsCompleted, Is.True);
            MoveTopTo(body.transform, boundary);
            yield return null;
            yield return null;
            Assert.That(body != null && !task.IsCompleted, Is.True, "A part on the boundary must remain.");
            MoveTopTo(body.transform, boundary - 0.05f);
            yield return Finish(task);
            Assert.That(body == null, Is.True, "Completion must follow actual destruction.");
        }

        [UnityTest]
        public IEnumerator OffsetVisibleGeometrySurvivesBelowPivotAndDuringRotationWithoutATimer()
        {
            var body = PrepareRemovalProbe();
            foreach (var renderer in body.GetComponentsInChildren<Renderer>())
            {
                renderer.transform.localPosition = Vector3.up * 4f;
                renderer.transform.localScale = new Vector3(2f, 8f, 2f);
                var collider = renderer.GetComponent<BoxCollider>();
                collider.center = Vector3.down * 0.5f;
                collider.size = new Vector3(1f, 0.1f, 1f);
            }

            var task = _fixturePhysics.ReleaseAsync(0, CancellationToken.None).AsTask();
            body.isKinematic = true;
            body.transform.position = Vector3.down * 12.5f;
            Physics.SyncTransforms();
            Assert.That(BoundsOf(body.transform).max.y, Is.LessThan(-12f), "The collider and pivot are already below.");
            Assert.That(body.GetComponentInChildren<Renderer>().bounds.max.y, Is.GreaterThan(-10f));
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(body != null && !task.IsCompleted, Is.True, "Visible geometry must survive the former timer.");
            body.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(body != null && !task.IsCompleted, Is.True, "Rotation does not hide the entire part yet.");
            body.transform.position += Vector3.down * 100f;
            Physics.SyncTransforms();
            yield return Finish(task);
            Assert.That(body == null, Is.True);
        }

        [UnityTest]
        public IEnumerator RemovalAccountsForPerspectiveDepth()
        {
            var body = PrepareRemovalProbe();
            body.transform.position = Vector3.forward * 30f;
            var task = _fixturePhysics.ReleaseAsync(0, CancellationToken.None).AsTask();
            body.isKinematic = true;
            MoveTopTo(body.transform, -20f);
            yield return null;
            yield return null;
            Assert.That(body != null && !task.IsCompleted, Is.True, "Far geometry is still within the taller frustum section.");
            body.transform.position = new Vector3(0f, body.transform.position.y, 0f);
            Physics.SyncTransforms();
            yield return Finish(task);
        }

        [UnityTest]
        public IEnumerator ViewportAndFieldOfViewChangesUpdateRemovalAndLowerTheGround()
        {
            var body = PrepareRemovalProbe(true);
            var camera = _scope.SceneCamera;
            var before = camera.transform.position;
            var task = _fixturePhysics.ReleaseAsync(0, CancellationToken.None).AsTask();
            body.isKinematic = true;
            MoveTopTo(body.transform, -7f);
            yield return null;
            yield return null;
            var floor = _fixtureGround.position;
            camera.aspect = 0.2f;
            camera.fieldOfView = 100f;
            MoveTopTo(body.transform, -20f);
            yield return null;
            yield return null;
            Assert.That(camera.transform.position.z, Is.LessThan(before.z));
            Assert.That(body != null && !task.IsCompleted, Is.True, "Expanded framing must not use the previous boundary.");
            Assert.That(_fixtureGround.position.y, Is.LessThan(floor.y), "Ground must stay below the expanded removal region.");
            var lowered = _fixtureGround.position;
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(_fixtureGround.position, lowered), Is.LessThan(0.0001f),
                "Paused physics must not repeatedly lower the ground using stale collider bounds.");
            camera.aspect = 16f / 9f;
            camera.fieldOfView = 40f;
            yield return Finish(task);
        }

        [UnityTest]
        public IEnumerator GroundDoesNotHoldAPartInsideTheRemovalRegion()
        {
            var body = PrepareRemovalProbe();
            var task = _fixturePhysics.ReleaseAsync(0, CancellationToken.None).AsTask();
            var contacts = body.gameObject.AddComponent<PartContactProbe>();
            var physics = _fixtureScene.GetPhysicsScene();
            for (var step = 0; step < 150; step++)
            {
                Physics.SyncTransforms();
                physics.Simulate(0.02f);
            }

            Assert.That(contacts.Contacts.Contains(_fixtureGround.GetComponent<BoxCollider>()), Is.True);
            var bounds = body.GetComponentInChildren<Renderer>().bounds;
            Assert.That(_scope.SceneCamera.WorldToViewportPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.max.z)).y,
                Is.LessThan(0f), "Even a resting part must already be fully below the view.");
            yield return Finish(task);
            Assert.That(body == null, Is.True);
        }

        [UnityTest]
        public IEnumerator RemovalConfigurationRejectsInvalidMarginsAndMissingGeometry()
        {
            CreateFixture(false);
            foreach (var margin in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new LevelPartPhysics(
                    _fixtureView, _fixtureGround, _scope.SceneCamera, margin));
            }

            foreach (var renderer in _fixtureView.GetPart(0).Node.GetComponentsInChildren<Renderer>())
            {
                Object.DestroyImmediate(renderer);
            }

            Assert.Throws<InvalidOperationException>(() => new LevelPartPhysics(
                _fixtureView, _fixtureGround, _scope.SceneCamera, 1f));
            yield return null;
        }

        private Rigidbody PrepareRemovalProbe(bool useGameplayFraming = false)
        {
            CreateFixture(false);
            Time.timeScale = 0f;
            var body = _fixtureView.GetPart(0).Node.GetComponent<Rigidbody>();
            body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            body.transform.localScale = Vector3.one;
            foreach (var renderer in body.GetComponentsInChildren<Renderer>())
            {
                renderer.transform.localPosition = Vector3.zero;
                renderer.transform.localScale = Vector3.one * 2f;
            }

            var camera = _scope.SceneCamera;
            camera.GetComponent<OrbitCamera>().enabled = useGameplayFraming;
            if (!useGameplayFraming)
            {
                camera.transform.SetPositionAndRotation(Vector3.back * 10f, Quaternion.identity);
                camera.fieldOfView = 90f;
                camera.aspect = 1f;
            }

            Physics.SyncTransforms();
            return body;
        }

        private static void MoveTopTo(Transform body, float top)
        {
            Physics.SyncTransforms();
            body.position += Vector3.up * (top - body.GetComponentInChildren<Renderer>().bounds.max.y);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator CancellationStopsRemovalAndRejectsARepeatedRelease()
        {
            CreateFixture(false);
            var owner = _fixtureView.GetPart(0).Node;
            using (var cancellation = new CancellationTokenSource())
            {
                var task = _fixturePhysics.ReleaseAsync(0, cancellation.Token).AsTask();
                var duplicate = _fixturePhysics.ReleaseAsync(0, cancellation.Token).AsTask();
                yield return Wait(() => duplicate.IsCompleted);
                Assert.Throws<InvalidOperationException>(() => duplicate.GetAwaiter().GetResult());
                cancellation.Cancel();
                yield return Wait(() => task.IsCompleted);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                owner.position += Vector3.down * 100f;
                Physics.SyncTransforms();
                yield return new WaitForSeconds(1.1f);
                Assert.That(owner != null, Is.True);
            }
        }

        private LevelAttempt CreateFixture(bool elevenAttachments)
        {
            _fixtureScene = SceneManager.CreateScene("PartPhysicsFixture", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _fixtureSimulationTime = 0f;
            var root = new GameObject("PhysicsFixture");
            SceneManager.MoveGameObjectToScene(root, _fixtureScene);
            _fixtureView = root.AddComponent<LevelView>();
            var owner = new GameObject("Owner_0").transform;
            owner.SetParent(root.transform, false);
            owner.position = new Vector3(0f, 3f, 0f);
            owner.localScale = Vector3.one * 1.2f;
            var first = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            first.SetParent(owner, false);
            first.localPosition = new Vector3(0.6f, 0f, 0f);
            first.localScale = Vector3.one * 0.6f;
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            child.name = "OffsetCollider";
            child.SetParent(owner, false);
            child.localPosition = new Vector3(1.2f, 0.4f, 0f);
            child.localScale = Vector3.one * 0.6f;
            var neighbor = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            neighbor.name = "Owner_1";
            neighbor.SetParent(root.transform, false);
            neighbor.position = new Vector3(0f, 1.5f, 0f);
            neighbor.localScale = new Vector3(5f, 0.4f, 5f);
            var floor = new GameObject("Ground").transform;
            SceneManager.MoveGameObjectToScene(floor.gameObject, _fixtureScene);
            var floorCollider = floor.gameObject.AddComponent<BoxCollider>();
            floorCollider.size = new Vector3(20f, 0.2f, 20f);
            floorCollider.center = new Vector3(0f, -0.1f, 0f);
            _fixtureGround = floor;

            var count = elevenAttachments ? 12 : 9;
            var source = JsonConvert.DeserializeObject<LevelSourceData>(JsonConvert.SerializeObject(_scope.LevelView.ReadSource()));
            source.AllowedBoltPaletteIndices = elevenAttachments ? new[] { 1, 2 } : new[] { 1, 2, 3 };
            source.Bolts = Enumerable.Range(0, count).Select(id => new BoltSourceData
            {
                Id = id,
                OwnerId = (elevenAttachments ? id < 11 : id == 6) ? 0 : 1,
                Position = new double[3],
                EulerDegrees = new double[3],
                SourceWhitelist = "",
                NodePath = "Assembly/Bolts/Bolt_" + id
            }).ToArray();
            source.Parts = Enumerable.Range(0, 2).Select(id => new PartSourceData
            {
                Id = id,
                ChildIndex = id,
                Name = "Owner_" + id,
                NodePath = "Assembly/Model/Owner_" + id,
                BoltIds = source.Bolts.Where(bolt => bolt.OwnerId == id).Select(bolt => bolt.Id).ToArray(),
                SourceReleaseForce = new double[3]
            }).ToArray();
            using (var serialized = new SerializedObject(_fixtureView))
            {
                var parts = serialized.FindProperty("_parts");
                parts.arraySize = 2;
                for (var index = 0; index < 2; index++)
                {
                    var part = parts.GetArrayElementAtIndex(index);
                    part.FindPropertyRelative("_id").intValue = index;
                    part.FindPropertyRelative("_node").objectReferenceValue = index == 0 ? owner : neighbor;
                }

                var bolts = serialized.FindProperty("_bolts");
                bolts.arraySize = count;
                for (var id = 0; id < count; id++)
                {
                    var clone = Object.Instantiate(_scope.LevelView.GetBolt(0).Node.gameObject, root.transform);
                    _fixtureBolts.Add(clone);
                    var bolt = bolts.GetArrayElementAtIndex(id);
                    bolt.FindPropertyRelative("_id").intValue = id;
                    bolt.FindPropertyRelative("_ownerId").intValue = source.Bolts[id].OwnerId;
                    bolt.FindPropertyRelative("_node").objectReferenceValue = clone.transform;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            _fixturePhysics = new LevelPartPhysics(_fixtureView, floor, _scope.SceneCamera, 1f);
            var attempt = new LevelAttempt(source, LevelGameplayDefinitionUtility.Build(source, Enumerable.Range(0, count).ToArray()));
            _scope.GetComponent<BoltTurnOperations>().Construct(_fixtureView, attempt, _scope.LevelView.ContentCatalog, _fixturePhysics);
            return attempt;
        }

        private static Bounds BoundsOf(Transform node)
        {
            var colliders = node.GetComponentsInChildren<Collider>();
            var bounds = colliders[0].bounds;
            foreach (var collider in colliders.Skip(1))
            {
                bounds.Encapsulate(collider.bounds);
            }

            return bounds;
        }

        private static void AssertPinned(Transform node, Vector3 position, Quaternion rotation)
        {
            Assert.That(node.GetComponent<Rigidbody>().isKinematic, Is.True);
            Assert.That(node.GetComponent<Rigidbody>().interpolation, Is.EqualTo(RigidbodyInterpolation.None));
            Assert.That(node.GetComponent<Rigidbody>().useGravity, Is.False);
            Assert.That(node.position, Is.EqualTo(position));
            Assert.That(Quaternion.Angle(node.rotation, rotation), Is.LessThan(0.01f));
        }

        private IEnumerator Finish(Task task)
        {
            yield return Wait(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        private IEnumerator Wait(Func<bool> ready)
        {
            var deadline = Time.realtimeSinceStartup + 10f;
            while (!ready() && Time.realtimeSinceStartup < deadline)
            {
                if (_fixtureScene.IsValid() && _fixtureScene.isLoaded)
                {
                    _fixtureSimulationTime += Time.deltaTime;
                    while (_fixtureSimulationTime >= Time.fixedDeltaTime)
                    {
                        Physics.SyncTransforms();
                        _fixtureScene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
                        _fixtureSimulationTime -= Time.fixedDeltaTime;
                    }
                }

                yield return null;
            }

            Assert.That(ready(), Is.True, "Physics or turn operation timed out.");
        }
    }
}
