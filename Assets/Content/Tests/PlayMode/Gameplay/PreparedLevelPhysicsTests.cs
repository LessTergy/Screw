#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Screw.Editor.Levels;
using Screw.Gameplay;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Screw.Tests.Gameplay
{
    public sealed class PreparedLevelPhysicsTests
    {
        private Scene _scene;
        private LevelPartPhysics _physics;

        private static IEnumerable<int> PreparedNumbers
        {
            get { return PreparedLevelUtility.PreparedNumbers; }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _physics?.Dispose();
            _physics = null;
            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        [UnityTest]
        public IEnumerator PreparedModelReleasesAllPartsAndRestoresItsOriginalState([ValueSource(nameof(PreparedNumbers))] int number)
        {
            var path = PreparedLevelUtility.ViewPath(number);
            var hash = AssetDatabase.GetAssetDependencyHash(path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            for (var pass = 0; pass < 2; pass++)
            {
                _scene = SceneManager.CreateScene("PreparedPhysics", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var instance = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(instance, _scene);
                var view = instance.GetComponent<LevelView>();
                var source = view.ValidateBindings();
                var route = LevelRouteData.Read(File.ReadAllText(PreparedLevelUtility.RoutePath(number)), source);
                var cameraObject = new GameObject("PreparedCamera", typeof(Camera), typeof(OrbitCamera));
                SceneManager.MoveGameObjectToScene(cameraObject, _scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.farClipPlane = 2000f;
                camera.aspect = route.Camera.Aspect;
                camera.fieldOfView = route.Camera.FieldOfViewDegrees;
                var input = cameraObject.GetComponent<OrbitCamera>();
                input.enabled = false;
                using (var serialized = new SerializedObject(input))
                {
                    serialized.FindProperty("_levelRoot").objectReferenceValue = view.transform;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                input.FrameLevel();
                var ground = new GameObject("PreparedGround", typeof(BoxCollider));
                SceneManager.MoveGameObjectToScene(ground, _scene);
                ground.GetComponent<BoxCollider>().size = new Vector3(100f, 0.1f, 100f);
                Assert.That(view.Parts.All(part => part.Node.GetComponent<Rigidbody>() == null), Is.True);
                var positions = view.Parts.ToDictionary(part => part.Id, part => part.Node.localPosition);
                var remaining = source.Parts.ToDictionary(part => part.Id, part => part.BoltIds.Length);
                var ownerByBolt = source.Bolts.ToDictionary(bolt => bolt.Id, bolt => bolt.OwnerId);
                _physics = new LevelPartPhysics(view, ground.transform, camera, 1f);
                var physicsScene = _scene.GetPhysicsScene();
                var releases = new List<Task>();
                var probes = new List<PartContactProbe>();
                foreach (var step in route.Steps)
                {
                    var angles = LevelRouteData.Vector(step.LevelEulerDegrees);
                    input.Rotate(new Vector2(angles.y - input.Yaw, angles.x - input.Pitch));
                    view.GetBolt(step.BoltId).Node.gameObject.SetActive(false);
                    var owner = ownerByBolt[step.BoltId];
                    remaining[owner]--;
                    if (remaining[owner] == 0)
                    {
                        var node = view.GetPart(owner).Node;
                        probes.Add(node.gameObject.AddComponent<PartContactProbe>());
                        releases.Add(_physics.ReleaseAsync(owner, CancellationToken.None).AsTask());
                        Assert.That(node.parent, Is.Null);
                        Assert.That(node.GetComponent<Rigidbody>().isKinematic, Is.False);
                        Assert.That(node.GetComponent<Rigidbody>().interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
                    }

                    Physics.SyncTransforms();
                    for (var tick = 0; tick < 10; tick++)
                    {
                        physicsScene.Simulate(0.02f);
                    }

                    yield return null;
                    foreach (var part in view.Parts.Where(part => remaining[part.Id] > 0))
                    {
                        Assert.That(Vector3.Distance(part.Node.localPosition, positions[part.Id]), Is.LessThan(0.0001f));
                        Assert.That(part.Node.GetComponent<Rigidbody>().isKinematic, Is.True);
                    }
                }

                if (pass == 1)
                {
                    camera.aspect = 1.5f;
                    camera.fieldOfView = 75f;
                    input.ResetOrbit();
                }

                for (var frame = 0; frame < 20 && releases.Any(task => !task.IsCompleted); frame++)
                {
                    Physics.SyncTransforms();
                    for (var tick = 0; tick < 50; tick++)
                    {
                        physicsScene.Simulate(0.02f);
                    }

                    yield return null;
                }

                Assert.That(releases, Has.Count.EqualTo(source.Parts.Length));
                Assert.That(releases.All(task => task.IsCompleted), Is.True, "Parts did not leave the removal boundary in level " + number);
                foreach (var task in releases)
                {
                    task.GetAwaiter().GetResult();
                }

                Assert.That(view.Parts.All(part => part.Node == null), Is.True);
                TestContext.WriteLine("Level " + number + ", pass " + pass + ": " + releases.Count
                    + " parts removed; contacts: " + probes.Sum(probe => probe.Contacts.Count));
                _physics.Dispose();
                _physics = null;
                yield return SceneManager.UnloadSceneAsync(_scene);
                Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.EqualTo(hash));
            }
        }
    }
}
#endif
