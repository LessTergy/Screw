using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Screw.Editor.Levels;
using Screw.Gameplay;
using Screw.Levels;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Screw.Tests.Levels
{
    public sealed class LevelRouteTests
    {
        private GameplayLifetimeScope _scope;
        private LevelRouteData _route;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Content/Scenes/Gameplay.unity", LoadSceneMode.Single);
            yield return null;
            _scope = Object.FindFirstObjectByType<GameplayLifetimeScope>();
            _scope.GameplaySession.enabled = false;
            _route = LevelRouteData.Read(File.ReadAllText(LevelRouteData.LevelOnePath), _scope.LevelView.ReadSource());
        }

        [UnityTest]
        public IEnumerator SavedWitnessUsesActualPointerSelectionAndReleasesOnlyCompletedOwners()
        {
            var view = _scope.LevelView;
            var source = view.ReadSource();
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelGameplayDefinition>(LevelGameplayDefinitionUtility.LevelOnePath);
            var attempt = new LevelAttempt(definition.ReadSource(), definition.Gameplay);
            var selection = _scope.Container.Resolve<BoltSelection>();
            var selected = 0;
            System.Action<LevelBoltBinding> countSelection = bolt => selected++;
            selection.Selected += countSelection;
            try
            {
                using (var session = new LevelRouteSession(view, _scope.SceneCamera, selection, _route))
                {
                    for (var index = 0; index < _route.Steps.Length; index++)
                    {
                        var id = _route.Steps[index].BoltId;
                        session.ReplayNext();
                        Assert.That(selected, Is.EqualTo(index + 1), "Exactly one selection per pointer gesture.");
                        Assert.That(session.StepIndex, Is.EqualTo(index + 1));
                        var removed = _route.Steps.Take(index + 1).Select(step => step.BoltId).ToArray();
                        foreach (var bolt in view.Bolts)
                        {
                            Assert.That(bolt.Node.gameObject.activeSelf, Is.EqualTo(!removed.Contains(bolt.Id)));
                        }

                        foreach (var part in source.Parts)
                        {
                            Assert.That(view.GetPart(part.Id).Node.gameObject.activeSelf,
                                Is.EqualTo(!part.BoltIds.All(removed.Contains)), "Owner " + part.Id);
                        }

                        var result = attempt.ReserveBolt(id);
                        Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
                        Assert.That(attempt.ConfirmDetachment(result.Turn), Is.True);
                        Assert.That(attempt.ConfirmArrival(result.Turn), Is.True);
                        Assert.That(attempt.CompleteTurn(result.Turn), Is.True);
                        Assert.That(attempt.PoolCount, Is.Zero);
                        yield return null;
                    }

                    Assert.That(session.IsComplete, Is.True);
                    Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Won));
                    Assert.That(attempt.CollectedBoltIds.Count, Is.EqualTo(9));
                    Assert.That(attempt.ContainerQueue, Is.Empty);
                    Assert.That(attempt.Containers.All(container => !container.IsActive), Is.True);
                }
            }
            finally
            {
                selection.Selected -= countSelection;
            }

            Assert.That(view.Bolts.All(bolt => bolt.Node.gameObject.activeSelf), Is.True);
            Assert.That(view.Parts.All(part => part.Node.gameObject.activeSelf), Is.True);
            view.ValidateBindings();
        }

        [UnityTest]
        public IEnumerator ActualDragRotatesAssemblyWithoutMovingCameraOrSelectingABolt()
        {
            var view = _scope.LevelView;
            var camera = _scope.SceneCamera;
            var input = camera.GetComponent<OrbitCamera>();
            var selection = _scope.Container.Resolve<BoltSelection>();
            var cameraPosition = camera.transform.position;
            var cameraRotation = camera.transform.rotation;
            var levelRotation = view.transform.rotation;
            var groundPosition = _scope.Ground.position;
            var gravity = Physics.gravity;
            var selected = 0;
            System.Action<LevelBoltBinding> onSelected = bolt => selected++;
            selection.Selected += onSelected;
            try
            {
                Vector2 start = camera.ViewportToScreenPoint(new Vector3(0.45f, 0.4f, 0f));
                var end = start + Vector2.right * (Mathf.Min(camera.pixelWidth, camera.pixelHeight) * 0.2f);
                input.BeginPointer(-1, start);
                input.UpdatePointer(-1, end, false);
                input.UpdatePointer(-1, end, true);
                Assert.That(Quaternion.Angle(view.transform.rotation, levelRotation), Is.GreaterThan(1f));
                Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
                Assert.That(camera.transform.rotation, Is.EqualTo(cameraRotation));
                Assert.That(_scope.Ground.position, Is.EqualTo(groundPosition));
                Assert.That(Physics.gravity, Is.EqualTo(gravity));
                Assert.That(selected, Is.Zero);
                view.ValidateBindings();
            }
            finally
            {
                selection.Selected -= onSelected;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator UnreachableRecordedPoseIsRejectedBeforeRemoval()
        {
            _route.Steps[0].LevelEulerDegrees[2] = 30f;
            var view = _scope.LevelView;
            using (var session = new LevelRouteSession(view, _scope.SceneCamera, _scope.Container.Resolve<BoltSelection>(), _route))
            {
                Assert.Throws<InvalidDataException>(() => session.PrepareNext());
                Assert.That(session.StepIndex, Is.Zero);
                Assert.That(view.Bolts.All(bolt => bolt.Node.gameObject.activeSelf), Is.True);
                Assert.That(view.Parts.All(part => part.Node.gameObject.activeSelf), Is.True);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RealStickBlocksAFacingHeadAndItsRemovalOpensTheRay()
        {
            var view = _scope.LevelView;
            var camera = _scope.SceneCamera;
            var input = camera.GetComponent<OrbitCamera>();
            var selection = _scope.Container.Resolve<BoltSelection>();
            camera.fieldOfView = _route.Camera.FieldOfViewDegrees;
            camera.aspect = _route.Camera.Aspect;
            input.FrameLevel();
            input.Rotate(new Vector2(2f - input.Yaw, 91f - input.Pitch));
            var target = view.GetBolt(3);
            Vector2 screen = camera.WorldToScreenPoint(target.Node.TransformPoint(target.Node.GetComponent<BoxCollider>().center));
            var ray = camera.ScreenPointToRay(screen);
            Assert.That(Vector3.Dot(target.Node.forward, -ray.direction), Is.GreaterThan(0.1f));
            Assert.That(selection.TrySelect(screen, out _), Is.False);
            var stick = view.GetPart(3).Node.gameObject;
            try
            {
                stick.SetActive(false);
                Assert.That(selection.TrySelect(screen, out var selected), Is.True);
                Assert.That(selected.Id, Is.EqualTo(3));
            }
            finally
            {
                stick.SetActive(true);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator WrongActualHeadDoesNotAdvanceOrHideAnything()
        {
            var view = _scope.LevelView;
            using (var session = new LevelRouteSession(view, _scope.SceneCamera, _scope.Container.Resolve<BoltSelection>(), _route))
            {
                session.PrepareNext();
                var other = view.GetBolt(1);
                Vector2 screen = _scope.SceneCamera.WorldToScreenPoint(other.Node.TransformPoint(other.Node.GetComponent<BoxCollider>().center));
                var input = _scope.SceneCamera.GetComponent<OrbitCamera>();
                input.BeginPointer(-1, screen);
                input.UpdatePointer(-1, screen, true);
                Assert.That(session.Message, Does.Contain("selected 1"));
                Assert.That(session.StepIndex, Is.Zero);
                Assert.That(view.Bolts.All(bolt => bolt.Node.gameObject.activeSelf), Is.True);
                Assert.That(view.Parts.All(part => part.Node.gameObject.activeSelf), Is.True);
            }

            yield return null;
        }
    }
}
