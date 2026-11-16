using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Screw.Tests.Gameplay
{
    public sealed class BoltTurnIntegrationTests
    {
        private const string GameplayPath = "Assets/Content/Scenes/Gameplay.unity";
        private const string BoxParticlesName = "Container Close Particles";
        private const string BoltParticlesName = "Bolt Withdrawal Particles";
        private const int BoltBurstSize = 12;
        private GameplayLifetimeScope _scope;
        private BoltTurnOperations _operations;
        private CollectionView _collection;
        private float _previousTimeScale;
        private Hash128 _catalogHash;
        private IDisposable _testProfileRegistration;
        private readonly Dictionary<Material, string> _materials = new Dictionary<Material, string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            _catalogHash = AssetDatabase.GetAssetDependencyHash(LevelContentCatalogUtility.CatalogAssetPath);
            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
            var testProfile = new CampaignProgress(catalog.Levels.Count);
            _testProfileRegistration = LifetimeScope.Enqueue(builder =>
            {
                if (!builder.Exists(typeof(CampaignProgress)))
                {
                    builder.RegisterInstance(testProfile);
                }
            });
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
            _testProfileRegistration.Dispose();
            Time.timeScale = _previousTimeScale;
            Assert.That(AssetDatabase.GetAssetDependencyHash(LevelContentCatalogUtility.CatalogAssetPath), Is.EqualTo(_catalogHash));
            foreach (var material in _materials)
            {
                Assert.That(EditorJsonUtility.ToJson(material.Key), Is.EqualTo(material.Value), material.Key.name);
            }
        }

        [UnityTest]
        public IEnumerator CollectionViewRebuildsAnEmptyExpandedPoolCache()
        {
            var attempt = _scope.GameplaySession.Attempt;
            var catalog = _scope.LevelView.ContentCatalog;
            var slotIndex = LevelDataValidator.PoolCapacity;
            var extra = _collection.PoolSlots[slotIndex - 1].parent.Find("Extra Pool Slot");
            Assert.That(extra, Is.Not.Null);
            Assert.That(extra.gameObject.activeSelf, Is.False);

            var expandedSlots = typeof(CollectionView).GetField("_expandedPoolSlots", BindingFlags.Instance | BindingFlags.NonPublic);
            expandedSlots.SetValue(_collection, Array.Empty<Transform>());
            yield return null;

            _collection.Initialize(attempt, catalog);
            Assert.That(_collection.PoolSlots.Count, Is.EqualTo(slotIndex));
            var profile = _scope.Container.Resolve<CampaignProgress>();
            profile.RecordVictory(attempt.LevelNumber, AttemptStatus.Won);
            Assert.That(profile.TryBuyExtraSlot(), Is.True);
            Assert.That(profile.TryUseExtraSlot(attempt), Is.True);
            Assert.That(_collection.PoolSlots.Count, Is.EqualTo(slotIndex + 1));
            Assert.That(_collection.PoolSlots[slotIndex], Is.SameAs(extra));
            yield return null;
            Assert.That(extra.gameObject.activeInHierarchy, Is.True);

            _collection.Initialize(attempt.Restart(), catalog);
            Assert.That(_collection.PoolSlots.Count, Is.EqualTo(slotIndex));
            Assert.That(extra.gameObject.activeSelf, Is.False);
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
            Assert.That(SessionReference<Text>("_levelLabel").text, Is.EqualTo("Уровень 1"));
            Assert.That(_collection.GetComponentsInChildren<TMP_Text>(true), Is.Empty);
            using (var view = new SerializedObject(_collection))
            {
                var safeArea = (RectTransform)view.FindProperty("_safeArea").objectReferenceValue;
                Assert.That(safeArea.GetComponents<MonoBehaviour>().Any(component => component.GetType().FullName == "Crystal.SafeArea"), Is.True);
                Assert.That(SessionReference<Text>("_levelLabel").transform.IsChildOf(safeArea), Is.True);
                Assert.That(SessionReference<Button>("_restartButton").transform.IsChildOf(safeArea), Is.True);
                var locked = view.FindProperty("_lockedContainers");
                Assert.That(locked.arraySize, Is.EqualTo(2));
                var previous = _collection.Containers[1].transform;
                for (var index = 0; index < locked.arraySize; index++)
                {
                    var container = (Transform)locked.GetArrayElementAtIndex(index).objectReferenceValue;
                    Assert.That(container.gameObject.activeInHierarchy, Is.True);
                    Assert.That(container.localPosition.x, Is.GreaterThan(previous.localPosition.x));
                    Assert.That(container.localPosition.y, Is.EqualTo(previous.localPosition.y));
                    Assert.That(container.GetComponent<CollectionContainer>(), Is.Null);
                    previous = container;
                }
            }
            Assert.That(attempt.Bolts.Values.Count(bolt => bolt.PaletteIndex == 1), Is.EqualTo(6));
            Assert.That(attempt.Bolts.Values.Count(bolt => bolt.PaletteIndex == 2), Is.EqualTo(3));
            AssertMaterialsAndIdentity(attempt);
            AssertStableVisualState(attempt);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CampaignPlaysEverySavedRouteAndRetriesTheFinalLevel()
        {
            var catalog = _scope.LevelView.ContentCatalog;
            var campaign = new CampaignProgress(catalog.Levels.Count);
            var loads = 0;
            void CountGameplayLoads(Scene scene, LoadSceneMode mode)
            {
                if (scene.path == GameplayPath)
                {
                    loads++;
                }
            }

            using (LifetimeScope.Enqueue(builder => builder.RegisterInstance(campaign)))
            {
                yield return LoadGameplay();
                SceneManager.sceneLoaded += CountGameplayLoads;
                try
                {
                    foreach (var entry in catalog.Levels.OrderBy(level => level.Number))
                    {
                        var session = _scope.GameplaySession;
                        session.enabled = true;
                        Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(entry.Number));
                        Assert.That(session.Attempt.LevelNumber, Is.EqualTo(entry.Number));
                        Assert.That(SessionReference<Text>("_levelLabel").text, Is.EqualTo($"Уровень {entry.Number}"));
                        Assert.That(_scope.GameplayDefinition, Is.SameAs(entry.Gameplay));
                        Assert.That(_scope.LevelAnchor.GetComponentsInChildren<LevelView>(true).Length, Is.EqualTo(1));
                        Assert.That(_scope.SceneCamera.GetComponent<OrbitCamera>().LevelRoot, Is.SameAs(_scope.LevelView.transform));
                        Assert.That(Vector3.Dot(_scope.LevelView.ModelRoot.forward, -_scope.SceneCamera.transform.forward),
                            Is.GreaterThan(0.9f), "Level " + entry.Number + " must start facing the camera.");
                        Assert.That(_collection.PoolCount, Is.Zero);
                        AssertMaterialsAndIdentity(session.Attempt);
                        AssertStableVisualState(session.Attempt);
                        Assert.That(session.Attempt.DetachedBoltIds, Is.Empty);

                        var attempt = session.Attempt;
                        var view = _scope.LevelView;
                        var route = FrameSavedRoute();
                        foreach (var step in route.Steps)
                        {
                            yield return PlayRouteStep(attempt, step);
                            Assert.That(attempt.PoolCount, Is.Zero);
                            AssertStableVisualState(attempt);
                        }

                        Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Won));
                        CollectionAssert.AreEquivalent(attempt.Bolts.Keys, attempt.CollectedBoltIds);
                        Assert.That(view.Parts.All(part => part.Node == null), Is.True);
                        var isFinal = entry.Number == catalog.Levels.Count;
                        var next = SessionReference<Button>("_nextButton");
                        var retry = SessionReference<Button>("_retryButton");
                        AssertResult(isFinal ? "Кампания пройдена" : "Победа!");
                        Assert.That(next.gameObject.activeSelf, Is.EqualTo(!isFinal));
                        var oldScope = _scope;
                        var oldView = oldScope.LevelView;
                        var previousLoads = loads;
                        if (isFinal)
                        {
                            next.onClick.Invoke();
                            Assert.That(campaign.PendingLevelNumber, Is.Null);
                            Click(retry);
                        }
                        else
                        {
                            Click(next);
                        }

                        Assert.That(next.interactable || retry.interactable, Is.False, "Result buttons must lock during reload.");
                        next.onClick.Invoke();
                        retry.onClick.Invoke();
                        yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                            "Campaign did not reload Gameplay.");
                        yield return null;
                        BindScope();
                        Assert.That(loads, Is.EqualTo(previousLoads + 1));
                        Assert.That(oldView == null, Is.True);
                        Assert.That(campaign.PendingLevelNumber, Is.Null);
                        Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(isFinal ? entry.Number : entry.Number + 1));
                        Assert.That(SessionReference<Text>("_levelLabel").text,
                            Is.EqualTo($"Уровень {campaign.CurrentLevelNumber}"));
                        Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
                        Assert.That(_scope.GameplaySession.Attempt.Status, Is.EqualTo(AttemptStatus.Active));
                    }
                }
                finally
                {
                    SceneManager.sceneLoaded -= CountGameplayLoads;
                }
            }
        }

        [UnityTest]
        public IEnumerator RobotCanOverflowOrRecoverFromTheSameFullPool()
        {
            var catalog = _scope.LevelView.ContentCatalog;
            var robot = catalog.Levels.Single(entry => entry.Gameplay.ReadSource().SourceName == "RetroRobot");
            var campaign = new CampaignProgress(catalog.Levels.Count);
            while (campaign.CurrentLevelNumber < robot.Number)
            {
                Assert.That(campaign.TryAdvance(campaign.CurrentLevelNumber, AttemptStatus.Won), Is.True);
                campaign.CompleteTransition();
            }

            using (LifetimeScope.Enqueue(builder => builder.RegisterInstance(campaign)))
            {
                yield return LoadGameplay();
                var prefix = new[] { 0, 1, 6, 7, 3, 8, 15, 16 };
                for (var pass = 0; pass < 2; pass++)
                {
                    _scope.GameplaySession.enabled = true;
                    var attempt = _scope.GameplaySession.Attempt;
                    Assert.That(attempt.LevelNumber, Is.EqualTo(robot.Number));
                    Assert.That(attempt.DetachedBoltIds, Is.Empty);
                    Assert.That(attempt.PoolCount, Is.Zero);
                    Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.False);
                    var route = FrameSavedRoute();
                    var steps = route.Steps.ToDictionary(step => step.BoltId);
                    for (var index = 0; index < prefix.Length; index++)
                    {
                        yield return PlayRouteStep(attempt, steps[prefix[index]]);
                        Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
                        Assert.That(attempt.PoolCount, Is.EqualTo(Mathf.Max(0, index - 2)));
                        AssertStableVisualState(attempt);
                    }

                    CollectionAssert.AreEqual(new int?[] { 7, 3, 8, 15, 16 }, attempt.PoolSlots);
                    Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.False);
                    if (pass == 0)
                    {
                        var sixth = _scope.LevelView.GetBolt(17).Node;
                        var parent = sixth.parent;
                        var localPosition = sixth.localPosition;
                        var remaining = attempt.RemainingBoltsByPart.ToArray();
                        TapRouteStep(steps[17]);
                        Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Lost));
                        Assert.That(attempt.ActiveTurns, Is.Empty);
                        CollectionAssert.AreEqual(prefix, attempt.DetachedBoltIds);
                        CollectionAssert.AreEqual(remaining, attempt.RemainingBoltsByPart.ToArray());
                        Assert.That(sixth.GetComponent<Collider>().enabled && sixth.gameObject.activeInHierarchy, Is.True);
                        Assert.That(sixth.parent, Is.SameAs(parent));
                        Assert.That(sixth.localPosition, Is.EqualTo(localPosition));
                        AssertResult("Нет места");
                        Assert.That(SessionReference<Button>("_nextButton").gameObject.activeSelf, Is.False);

                        var oldView = _scope.LevelView;
                        Click(SessionReference<Button>("_retryButton"));
                        yield return Wait(() => oldView == null, "Retry did not replace the robot.");
                        yield return null;
                        BindScope();
                        continue;
                    }

                    yield return PlayRouteStep(attempt, steps[2]);
                    Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
                    Assert.That(attempt.PoolCount, Is.EqualTo(2));
                    CollectionAssert.AreEquivalent(new int?[] { 15, 16 }, attempt.PoolSlots.Where(id => id.HasValue));
                    CollectionAssert.IsSubsetOf(new[] { 7, 3, 8 }, attempt.CollectedBoltIds);
                    AssertStableVisualState(attempt);

                    foreach (var step in route.Steps.Where(step => !attempt.DetachedBoltIds.Contains(step.BoltId)))
                    {
                        yield return PlayRouteStep(attempt, step);
                    }

                    Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Won));
                    Assert.That(attempt.PoolCount, Is.Zero);
                    Assert.That(_scope.LevelView.Parts.All(part => part.Node == null), Is.True);
                    AssertResult(robot.Number == catalog.Levels.Count ? "Кампания пройдена" : "Победа!");
                }
            }
        }

        [UnityTest]
        public IEnumerator LevelCanRotateDuringWithdrawalAndFlightWhileCameraStaysFixed()
        {
            var coordinator = _scope.GameplaySession.Coordinator;
            var bolt = _scope.LevelView.GetBolt(0).Node;
            var particles = GetParticles(BoltParticlesName);
            var particleSpace = _scope.LevelView.LevelRoot;
            var impact = BoltHeadInParticleSpace(bolt, particleSpace);
            Assert.That(particles.main.customSimulationSpace, Is.SameAs(particleSpace));
            var renderer = bolt.GetComponentInChildren<MeshRenderer>();
            var originalMaterials = renderer.sharedMaterials;
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
            Assert.That(particles.particleCount, Is.EqualTo(BoltBurstSize), "The accepted tap must emit before detachment begins.");
            var sparks = new ParticleSystem.Particle[BoltBurstSize];
            Assert.That(particles.GetParticles(sparks), Is.EqualTo(sparks.Length));
            Assert.That(sparks.All(spark => Vector3.Distance(spark.position, impact.Position) < impact.Diameter), Is.True);
            Assert.That(sparks.All(spark => spark.velocity.magnitude > impact.Diameter), Is.True);
            AssertRadialBurst(particles, particleSpace.InverseTransformDirection(_scope.SceneCamera.transform.right),
                particleSpace.InverseTransformDirection(_scope.SceneCamera.transform.up));
            var foregroundMaterials = renderer.sharedMaterials;
            for (var slotIndex = 0; slotIndex < foregroundMaterials.Length; slotIndex++)
            {
                Assert.That(foregroundMaterials[slotIndex].renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Overlay));
                Assert.That(foregroundMaterials[slotIndex].color, Is.EqualTo(originalMaterials[slotIndex].color));
            }

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
            var particlesBeforeRepeat = particles.particleCount;
            var repeated = coordinator.PlayAsync(0).GetAwaiter().GetResult();
            Assert.That(repeated.Outcome, Is.EqualTo(BoltReservationOutcome.AlreadyReserved));
            Assert.That(particles.particleCount, Is.EqualTo(particlesBeforeRepeat));

            var camera = _scope.SceneCamera;
            var head = bolt.GetComponent<BoxCollider>();
            var slot = _collection.GetSlot(turn.Destination);
            float HeadDepth()
            {
                return camera.WorldToViewportPoint(bolt.TransformPoint(head.center)).z;
            }

            var startDepth = HeadDepth();
            var startSize = bolt.lossyScale.x / startDepth;
            var slotDepth = camera.WorldToViewportPoint(slot.position).z;
            var flightDepth = Mathf.Lerp(startDepth, slotDepth, 0.2f);
            Time.timeScale = 0.1f;
            yield return Wait(() => HeadDepth() < flightDepth, "Bolt did not approach the slot.");
            CollectionAssert.AreEqual(foregroundMaterials, renderer.sharedMaterials);
            Assert.That(HeadDepth(), Is.GreaterThan((startDepth + slotDepth) * 0.5f), "Sample must be taken during flight.");
            Assert.That(bolt.lossyScale.x / HeadDepth(), Is.EqualTo(startSize).Within(startSize * 0.01f),
                "Perspective must not change the bolt's apparent scale during flight.");
            Assert.That(_collection.Containers[0].Count, Is.Zero);

            Time.timeScale = 1f;
            yield return Finish(task);
            Assert.That(bolt.GetInstanceID(), Is.EqualTo(id));
            Assert.That(bolt.parent, Is.SameAs(_collection.GetSlot(turn.Destination)));
            Assert.That(bolt.localScale, Is.EqualTo(Vector3.one * 0.25f));
            Assert.That(Vector3.Distance(bolt.TransformPoint(bolt.GetComponent<BoxCollider>().center), bolt.parent.position),
                Is.LessThan(0.001f), "The head, rather than the shaft pivot, must align with the slot.");
            Assert.That(_collection.Containers[0].Count, Is.EqualTo(1));
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
                var profile = _scope.Container.Resolve<CampaignProgress>();
                Assert.That(RuntimeControl<Text>("Reward Amount").gameObject.activeInHierarchy, Is.EqualTo(pass == 0));
                var balance = RuntimeControl<Text>("Balance Amount");
                Assert.That(balance.text, Is.EqualTo("0"));
                Assert.That(profile.Coins, Is.EqualTo(pass == 0 ? 100 : 0));
                if (pass == 0)
                {
                    yield return Wait(() => GameObject.Find("Reward Coin") != null, "The first flying coin did not appear.");
                    var coin = GameObject.Find("Reward Coin").GetComponent<RawImage>();
                    Assert.That(coin.texture, Is.Not.Null);
                    Assert.That(coin.rectTransform.rect.width, Is.GreaterThan(0f));
                    var initialFrame = coin.uvRect;
                    yield return Wait(() => coin != null && coin.uvRect != initialFrame, "The flying coin did not change sprite frames.");

                    for (var amount = 20; amount <= 100; amount += 20)
                    {
                        var expectedBalance = amount.ToString();
                        yield return Wait(() => balance.text == expectedBalance, "Missing coin arrival for balance " + expectedBalance);
                    }

                    Assert.That(profile.TryBuyExtraSlot(), Is.True);
                }

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

            var apply = RuntimeControl<Button>("Apply Extra Slot");
            Assert.That(apply.interactable, Is.True);
            Assert.That(RuntimeControl<Text>("Stock Amount").text, Is.EqualTo("1"));
            Click(apply);
            yield return null;
            Assert.That(_collection.PoolSlots.Count, Is.EqualTo(6));
            Assert.That(_collection.PoolSlots[5].gameObject.activeInHierarchy, Is.True);
            Assert.That(apply.interactable, Is.False);
            Assert.That(RuntimeControl<Text>("Stock Amount").text, Is.EqualTo("0"));
            Assert.That(_scope.Container.Resolve<CampaignProgress>().ExtraSlots, Is.Zero);
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
        public IEnumerator RapidTapsEmitAtBothBoltHeadsAndRestartClearsTheBursts()
        {
            var coordinator = _scope.GameplaySession.Coordinator;
            var particles = GetParticles(BoltParticlesName);
            var particleSpace = _scope.LevelView.LevelRoot;
            var firstHead = BoltHeadInParticleSpace(_scope.LevelView.GetBolt(0).Node, particleSpace);
            var secondHead = BoltHeadInParticleSpace(_scope.LevelView.GetBolt(6).Node, particleSpace);
            var first = coordinator.PlayAsync(0).AsTask();
            var second = coordinator.PlayAsync(6).AsTask();
            var duplicate = coordinator.PlayAsync(0).GetAwaiter().GetResult();
            Assert.That(duplicate.Outcome, Is.EqualTo(BoltReservationOutcome.AlreadyReserved));

            var sparks = new ParticleSystem.Particle[particles.main.maxParticles];
            var count = particles.GetParticles(sparks);
            Assert.That(count, Is.EqualTo(2 * BoltBurstSize), "Both accepted taps must emit immediately.");
            Assert.That(sparks.Take(count).Count(spark => Vector3.Distance(spark.position, firstHead.Position) < firstHead.Diameter),
                Is.EqualTo(BoltBurstSize));
            Assert.That(sparks.Take(count).Count(spark => Vector3.Distance(spark.position, secondHead.Position) < secondHead.Diameter),
                Is.EqualTo(BoltBurstSize));

            var oldScope = _scope;
            _scope.Container.Resolve<SceneNavigator>().RestartGameplay();
            yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                "Gameplay did not restart while withdrawal particles were alive.");
            yield return null;
            BindScope();
            Assert.That(particles == null, Is.True);
            Assert.That(GetParticles(BoltParticlesName).particleCount, Is.Zero);
            Assert.That(first.IsCompleted && second.IsCompleted, Is.True);
            Assert.Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => second.GetAwaiter().GetResult());
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
                    Assert.That(view.GetBolt(id).Node.localScale, Is.EqualTo(Vector3.one * 0.18f));
                    AssertStableVisualState(attempt);
                }

                yield return PlayAndFinish(coordinator, 0);
                yield return PlayAndFinish(coordinator, 1);
                var particles = GetParticles(BoxParticlesName);
                var burstGroups = new HashSet<int>();
                var container = _collection.Containers[0];
                var panel = container.Panel;
                var lid = container.Lid.transform;
                var closedY = lid.localPosition.y;
                var restScale = panel.localScale;
                Assert.That(lid.IsChildOf(panel), Is.True);
                var task = coordinator.PlayAsync(2).AsTask();
                var turn = attempt.ActiveTurns.Values.SingleOrDefault();
                var sawOriginalFull = false;
                var sawClosedLid = false;
                var sawImpactSquash = false;
                var sawStretch = false;
                var sawNextGroup = false;
                var deadline = Time.realtimeSinceStartup + 10f;
                while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                {
                    sawOriginalFull |= container.GroupId == 0 && container.Count == 3;
                    var lidY = lid.localPosition.y;
                    var panelScale = panel.localScale;
                    sawClosedLid |= container.GroupId == 0 && lid.gameObject.activeSelf
                        && Mathf.Approximately(lidY, closedY);
                    if (particles.particleCount > 0)
                    {
                        Assert.That(lid.gameObject.activeSelf, Is.True);
                        Assert.That(lidY, Is.EqualTo(closedY).Within(0.001f), "The lid must stay seated after impact.");
                        Assert.That(container.GroupId.HasValue, Is.True);
                        if (burstGroups.Add(container.GroupId.Value))
                        {
                            AssertRadialBurst(particles, lid.right, lid.up);
                        }

                        sawImpactSquash |= Mathf.Abs(panelScale.y) < Mathf.Abs(restScale.y) * 0.9f
                            && Mathf.Abs(panelScale.x) > Mathf.Abs(restScale.x);
                        sawStretch |= Mathf.Abs(panelScale.y) > Mathf.Abs(restScale.y) * 1.03f
                            && Mathf.Abs(panelScale.x) < Mathf.Abs(restScale.x);
                    }

                    if (lid.gameObject.activeSelf && panel.localPosition.y > 0.001f)
                    {
                        Assert.That(lidY, Is.EqualTo(closedY).Within(0.001f), "The lid must settle before the panel moves.");
                        Assert.That(Vector3.Distance(panelScale, restScale), Is.LessThan(0.001f),
                            "The box must regain its original shape before the panel moves.");
                    }

                    sawNextGroup |= container.GroupId == 2;
                    if (sawNextGroup && container.GroupId == null && _collection.PoolCount == 0)
                    {
                        break;
                    }
                    yield return null;
                }

                yield return FinishReleasedTurn(attempt, turn);
                yield return Finish(task);
                Assert.That(sawOriginalFull, Is.True, "The first full container must visibly leave before replacement.");
                Assert.That(sawClosedLid, Is.True, "The filled container must close before it leaves.");
                Assert.That(sawImpactSquash, Is.True, "The whole box must compress at impact.");
                Assert.That(sawStretch, Is.True, "The box must stretch as it rebounds from compression.");
                CollectionAssert.AreEquivalent(new[] { 0, 2 }, burstGroups, "Direct and pool fills must both emit.");
                Assert.That(particles.particleCount, Is.Zero);
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
            var camera = _scope.SceneCamera;
            var input = camera.GetComponent<OrbitCamera>();
            var route = LevelRouteData.Read(File.ReadAllText(LevelRouteData.LevelOnePath), _scope.LevelView.ReadSource());
            camera.aspect = route.Camera.Aspect;
            camera.fieldOfView = route.Camera.FieldOfViewDegrees;
            input.FrameLevel();
            var step = route.Steps[0];
            var angles = LevelRouteData.Vector(step.LevelEulerDegrees);
            input.Rotate(new Vector2(angles.y - input.Yaw, angles.x - input.Pitch));
            Vector2 point = camera.ViewportToScreenPoint(new Vector3(step.ViewportPoint[0], step.ViewportPoint[1], 0f));
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
            var task = coordinator.PlayAsync(1).AsTask();
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
            var step = route.Steps[2];
            var rotation = LevelRouteData.Vector(step.LevelEulerDegrees);
            input.Rotate(new Vector2(rotation.y - input.Yaw, rotation.x - input.Pitch));
            Vector2 point = camera.ViewportToScreenPoint(new Vector3(step.ViewportPoint[0], step.ViewportPoint[1], 0f));
            _scope.GameplaySession.enabled = true;
            var selection = _scope.Container.Resolve<BoltSelection>();
            var selections = 0;
            Action<LevelBoltBinding> onSelected = bolt =>
            {
                Assert.That(bolt.Id, Is.EqualTo(step.BoltId));
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
            Assert.That(coordinator.Attempt.ActiveTurns.Keys, Is.EquivalentTo(new[] { 1, 2 }));
            var nextTurn = coordinator.Attempt.ActiveTurns[2];
            Assert.That(nextTurn.Phase, Is.EqualTo(TurnPhase.Reserved));
            CollectionAssert.AreEqual(new[] { 0, 1 }, coordinator.Attempt.DetachedBoltIds);
            var levelRotation = input.LevelRoot.rotation;
            var cameraPosition = camera.transform.position;
            var cameraRotation = camera.transform.rotation;
            var partPosition = part.position;
            input.BeginPointer(-1, point);
            input.UpdatePointer(-1, point + new Vector2(40f, 20f), true);
            Assert.That(input.LevelRoot.rotation, Is.Not.EqualTo(levelRotation));
            Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(camera.transform.rotation, Is.EqualTo(cameraRotation));
            Assert.That(part.position, Is.EqualTo(partPosition));
            Assert.That(coordinator.Attempt.ActiveTurns[1], Is.SameAs(turn));

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
        public IEnumerator PauseInterceptsPointerFreezesTheCurrentTurnAndResumesIt()
        {
            _scope.GameplaySession.enabled = true;
            var input = _scope.SceneCamera.GetComponent<OrbitCamera>();
            var button = RuntimeControl<Button>("Pause");
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

            var session = _scope.GameplaySession;
            var task = session.PlayAsync(0).AsTask();
            var turn = session.Attempt.ActiveTurns[0];
            var resume = RuntimeControl<Button>("Resume");
            var audioWasPaused = AudioListener.pause;
            try
            {
                Click(button);
                Assert.That(resume.gameObject.activeInHierarchy, Is.True);
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(AudioListener.pause, Is.True);
                Assert.That(input.enabled, Is.False);
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(task.IsCompleted, Is.False);
                Assert.That(turn.Phase, Is.EqualTo(TurnPhase.Reserved));
            }
            finally
            {
                if (resume.gameObject.activeInHierarchy)
                {
                    Click(resume);
                }
            }

            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(AudioListener.pause, Is.EqualTo(audioWasPaused));
            Assert.That(input.enabled, Is.True);
            yield return Wait(() => turn.Phase != TurnPhase.Reserved, "The turn did not resume.");
        }

        [UnityTest]
        public IEnumerator RestartButtonRestoresEveryTurnPhaseAndIgnoresDoubleClicks()
        {
            for (var phase = 0; phase < 5; phase++)
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
                }

                if (phase == 4)
                {
                    yield return Finish(session.PlayAsync(6).AsTask());
                }

                Time.timeScale = phase == 0 ? 0f : 1f;
                var boltId = phase == 4 ? 2 : phase >= 2 ? 1 : 0;
                var task = session.PlayAsync(boltId).AsTask();
                var turn = session.Attempt.ActiveTurns.Values.SingleOrDefault();
                if (phase >= 1)
                {
                    yield return Wait(() => turn.Phase == TurnPhase.InTransit, "Flight did not begin.");
                }

                if (phase == 2 || phase == 3)
                {
                    _scope.LevelView.GetPart(0).Node.GetComponent<Rigidbody>().isKinematic = true;
                    yield return Wait(() => turn.Phase == TurnPhase.AwaitingCompletion, "Sorting did not begin.");
                }

                if (phase == 3)
                {
                    yield return new WaitForSeconds(1.2f);
                    Assert.That(task.IsCompleted, Is.False, "Visible part must keep removal pending.");
                }

                var oldParticles = GetParticles(BoxParticlesName);
                if (phase == 4)
                {
                    yield return Wait(() => oldParticles.particleCount > 0, "The lid impact did not emit particles.");
                }

                Time.timeScale = 1f;
                Click(RuntimeControl<Button>("Pause"));
                input.Rotate(new Vector2(75f, 45f));
                _scope.SceneCamera.fieldOfView = 75f;
                _scope.Ground.position += Vector3.down * 20f;
                var oldScope = _scope;
                var oldAudio = oldScope.GetComponent<AudioSource>();
                var oldNodes = _scope.LevelView.Parts.Select(part => part.Node)
                    .Concat(_scope.LevelView.Bolts.Select(bolt => bolt.Node)).ToArray();
                var restart = SessionReference<Button>("_restartButton");
                Click(restart);
                Click(restart);
                Assert.That(session.enabled, Is.False, "Restart must stop receiving selections immediately.");
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                yield return Wait(() => oldScope == null && Object.FindFirstObjectByType<GameplayLifetimeScope>() != null,
                    "Restart did not replace Gameplay in phase " + phase);
                yield return null;
                BindScope();
                Assert.That(oldAudio == null, Is.True);
                Assert.That(oldParticles == null, Is.True);
                Assert.That(GetParticles(BoxParticlesName).particleCount, Is.Zero);
                Assert.That(_scope.GetComponent<AudioSource>().isPlaying, Is.False);
                Assert.That(task.IsCompleted, Is.True);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                Assert.That(oldNodes.All(node => node == null), Is.True);
                Assert.That(_scope.GameplaySession.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
                Assert.That(_scope.GameplaySession.Attempt.DetachedBoltIds, Is.Empty);
                Assert.That(_scope.GameplaySession.Attempt.ContainerQueue, Is.EqualTo(new[] { 0 }));
                Assert.That(SessionReference<GameObject>("_resultPanel").activeSelf, Is.False);
                Assert.That(RuntimeControl<Button>("Pause").interactable, Is.True);
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
                session.Construct(coordinator, _scope.Container.Resolve<BoltSelection>(), _scope.Container.Resolve<SceneNavigator>(),
                    _scope.Container.Resolve<CampaignProgress>(), _scope.SceneCamera.GetComponent<OrbitCamera>(), _operations,
                    _scope.Container.Resolve<ScreenUi>());
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
                    session.Construct(coordinator, _scope.Container.Resolve<BoltSelection>(), _scope.Container.Resolve<SceneNavigator>(),
                        _scope.Container.Resolve<CampaignProgress>(), _scope.SceneCamera.GetComponent<OrbitCamera>(), _operations,
                        _scope.Container.Resolve<ScreenUi>());
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
                    Click(RuntimeControl<Button>("Pause"));
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
            Assert.That(SessionReference<Button>("_restartButton").gameObject.activeInHierarchy, Is.False);
        }

        private T SessionReference<T>(string field) where T : Object
        {
            using (var serialized = new SerializedObject(_scope.GameplaySession))
            {
                return (T)serialized.FindProperty(field).objectReferenceValue;
            }
        }

        private static T RuntimeControl<T>(string name) where T : Component
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(control => control.gameObject.name == name);
        }

        private static void Click(Button button)
        {
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            }, ExecuteEvents.pointerClickHandler);
        }

        private static void AssertRadialBurst(ParticleSystem particles, Vector3 right, Vector3 up)
        {
            var samples = new ParticleSystem.Particle[particles.main.maxParticles];
            var count = particles.GetParticles(samples);
            var min = Vector2.zero;
            var max = Vector2.zero;
            for (var index = 0; index < count; index++)
            {
                var velocity = samples[index].velocity;
                var projected = new Vector2(Vector3.Dot(velocity, right), Vector3.Dot(velocity, up));
                min = Vector2.Min(min, projected);
                max = Vector2.Max(max, projected);
            }

            Assert.That(min.x, Is.LessThan(0f));
            Assert.That(max.x, Is.GreaterThan(0f));
            Assert.That(min.y, Is.LessThan(0f));
            Assert.That(max.y, Is.GreaterThan(0f));
        }

        private ParticleSystem GetParticles(string name)
        {
            return _operations.transform.Find(name).GetComponent<ParticleSystem>();
        }

        private static (Vector3 Position, float Diameter) BoltHeadInParticleSpace(Transform bolt, Transform particleSpace)
        {
            var head = bolt.GetComponent<BoxCollider>();
            var front = head.center + Vector3.forward * (head.size.z * 0.5f);
            var diameter = particleSpace.InverseTransformVector(bolt.TransformVector(Vector3.right * head.size.x)).magnitude;
            return (particleSpace.InverseTransformPoint(bolt.TransformPoint(front)), diameter);
        }

        private IEnumerator PlayAndFinish(TurnCoordinator coordinator, int boltId)
        {
            var task = coordinator.PlayAsync(boltId).AsTask();
            yield return FinishReleasedTurn(coordinator.Attempt, coordinator.Attempt.ActiveTurns.Values.SingleOrDefault());
            yield return Finish(task);
        }

        private LevelRouteData FrameSavedRoute()
        {
            var source = _scope.LevelView.ReadSource();
            var route = LevelRouteData.Read(File.ReadAllText(PreparedLevelUtility.RoutePath(source.Number)), source);
            var camera = _scope.SceneCamera;
            camera.fieldOfView = route.Camera.FieldOfViewDegrees;
            camera.aspect = route.Camera.Aspect;
            camera.GetComponent<OrbitCamera>().FrameLevel();
            return route;
        }

        private void TapRouteStep(RouteStepData step)
        {
            var camera = _scope.SceneCamera;
            var input = camera.GetComponent<OrbitCamera>();
            var rotation = LevelRouteData.Vector(step.LevelEulerDegrees);
            input.Rotate(new Vector2(rotation.y - input.Yaw, rotation.x - input.Pitch));
            Vector2 point = camera.ViewportToScreenPoint(new Vector3(step.ViewportPoint[0], step.ViewportPoint[1], 0f));
            var selection = _scope.Container.Resolve<BoltSelection>();
            var selected = new List<int>();
            void OnSelected(LevelBoltBinding bolt) { selected.Add(bolt.Id); }
            selection.Selected += OnSelected;
            try
            {
                input.BeginPointer(-1, point);
                input.UpdatePointer(-1, point, true);
            }
            finally
            {
                selection.Selected -= OnSelected;
            }

            CollectionAssert.AreEqual(new[] { step.BoltId }, selected,
                "Level " + _scope.LevelView.LevelNumber + ", recorded pointer for bolt " + step.BoltId);
        }

        private IEnumerator PlayRouteStep(LevelAttempt attempt, RouteStepData step)
        {
            TapRouteStep(step);
            Assert.That(attempt.ActiveTurns.TryGetValue(step.BoltId, out var turn), Is.True,
                "Recorded pointer did not start a turn for bolt " + step.BoltId + " on level " + attempt.LevelNumber);
            yield return FinishReleasedTurn(attempt, turn);
            Assert.That(attempt.DetachedBoltIds, Does.Contain(step.BoltId));
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
                .Count(filter => filter.sharedMesh == mesh), Is.EqualTo(attempt.Bolts.Count),
                "Flights must move the original objects.");
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
                Assert.That(view.Lid.gameObject.activeSelf, Is.False);
                if (model.IsActive)
                {
                    var color = _scope.LevelView.ContentCatalog.GetColor(model.PaletteIndex).Primary.color;
                    Assert.That(view.Panel.Find("Body").GetComponent<SpriteRenderer>().color, Is.EqualTo(color));
                    Assert.That(view.Panel.Find("Insert").GetComponent<SpriteRenderer>().color, Is.EqualTo(Color.white));
                }
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
