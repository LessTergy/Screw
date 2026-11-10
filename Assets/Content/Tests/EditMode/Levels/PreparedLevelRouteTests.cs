using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Screw.Editor.Levels;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Screw.Tests.Levels
{
    public sealed class PreparedLevelRouteTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedWitnessRestoresTheLevelAndRejectsIncompleteInput(bool hidePart)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PreparedLevelUtility.ViewPath(2));
            var hash = AssetDatabase.GetAssetDependencyHash(PreparedLevelUtility.ViewPath(2));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var cameraObject = new GameObject("GeneratedRouteCamera", typeof(Camera), typeof(OrbitCamera));
            try
            {
                instance.transform.position = Vector3.right * 100f;
                var view = instance.GetComponent<LevelView>();
                var camera = cameraObject.GetComponent<Camera>();
                camera.farClipPlane = 2000f;
                camera.aspect = 1f;
                using (var serialized = new SerializedObject(cameraObject.GetComponent<OrbitCamera>()))
                {
                    serialized.FindProperty("_levelRoot").objectReferenceValue = instance.transform;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                if (hidePart)
                {
                    view.Parts[0].Node.gameObject.SetActive(false);
                    Assert.Throws<System.InvalidOperationException>(() => PreparedLevelUtility.BuildRoute(view, camera));
                    Assert.That(view.Parts[0].Node.gameObject.activeSelf, Is.False);
                }
                else
                {
                    var route = PreparedLevelUtility.BuildRoute(view, camera);
                    Assert.That(route.Steps, Has.Length.EqualTo(view.Bolts.Count));
                    Assert.That(route.Steps.Select(step => step.BoltId).Distinct().Count(), Is.EqualTo(view.Bolts.Count));
                    Assert.That(instance.transform.position, Is.EqualTo(Vector3.right * 100f));
                    Assert.That(Quaternion.Angle(instance.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
                    Assert.That(camera.aspect, Is.EqualTo(1f));
                    Assert.That(camera.transform.position, Is.EqualTo(Vector3.zero));
                    PreparedLevelUtility.Replay(view, camera, route);
                    Assert.That(view.Parts.All(part => part.Node.gameObject.activeSelf), Is.True);
                    Assert.That(view.Bolts.All(bolt => bolt.Node.gameObject.activeSelf), Is.True);
                }

                Assert.That(AssetDatabase.GetAssetDependencyHash(PreparedLevelUtility.ViewPath(2)), Is.EqualTo(hash));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(instance);
            }
        }

        [TestCaseSource(typeof(PreparedLevelUtility), nameof(PreparedLevelUtility.PreparedNumbers))]
        public void SavedCollidersRespectTheBudgetAndHaveNoPenetration(int number)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PreparedLevelUtility.ViewPath(number));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                instance.transform.position = Vector3.right * 100f;
                var view = instance.GetComponent<LevelView>();
                view.ValidateBindings();
                foreach (var part in view.Parts)
                {
                    var shapes = part.Node.GetComponentsInChildren<Collider>();
                    Assert.That(shapes.Length, Is.InRange(1, PrimitiveColliderBaker.DefaultMaxBoxes), "Part " + part.Id);
                    Assert.That(shapes.All(shape => shape is BoxCollider), Is.True);
                }

                var colliders = view.Parts.SelectMany(part => part.Node.GetComponentsInChildren<BoxCollider>()).ToArray();
                Physics.SyncTransforms();
                for (var i = 0; i < colliders.Length; i++)
                {
                    for (var j = i + 1; j < colliders.Length; j++)
                    {
                        var a = colliders[i];
                        var b = colliders[j];
                        var penetrates = Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                            b, b.transform.position, b.transform.rotation, out _, out var depth);
                        Assert.That(penetrates && depth > 0.00001f, Is.False,
                            a.transform.parent.parent.name + " / " + b.transform.parent.parent.name);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [TestCaseSource(typeof(PreparedLevelUtility), nameof(PreparedLevelUtility.PreparedNumbers))]
        public void SavedWitnessReplaysThroughRealPointerSelectionAndRestoresBindings(int number)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PreparedLevelUtility.ViewPath(number));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var cameraObject = new GameObject("PreparedRouteCamera");
            try
            {
                instance.transform.position = Vector3.right * 100f;
                var view = instance.GetComponent<LevelView>();
                var source = view.ValidateBindings();
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(PreparedLevelUtility.RoutePath(number));
                var route = LevelRouteData.Read(json.text, source);
                var camera = cameraObject.AddComponent<Camera>();
                camera.farClipPlane = 2000f;
                var input = cameraObject.AddComponent<OrbitCamera>();
                using (var serialized = new SerializedObject(input))
                {
                    serialized.FindProperty("_levelRoot").objectReferenceValue = instance.transform;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                input.FrameLevel();
                using (var selection = new BoltSelection(view, camera))
                {
                    selection.Start();
                    var selected = new List<int>();
                    selection.Selected += bolt => selected.Add(bolt.Id);
                    using (var session = new LevelRouteSession(view, camera, selection, route))
                    {
                        var removed = new HashSet<int>();
                        foreach (var step in route.Steps)
                        {
                            session.ReplayNext();
                            removed.Add(step.BoltId);
                            CollectionAssert.AreEqual(route.Steps.Take(removed.Count).Select(item => item.BoltId), selected);
                            foreach (var part in source.Parts)
                            {
                                Assert.That(view.GetPart(part.Id).Node.gameObject.activeSelf,
                                    Is.EqualTo(!part.BoltIds.All(removed.Contains)), "Owner " + part.Id);
                            }
                        }

                        Assert.That(session.IsComplete, Is.True);
                        Assert.That(view.Bolts.All(bolt => !bolt.Node.gameObject.activeSelf), Is.True);
                    }
                }

                view.ValidateBindings();
                Assert.That(view.Parts.All(part => part.Node.gameObject.activeSelf), Is.True);
                Assert.That(view.Bolts.All(bolt => bolt.Node.gameObject.activeSelf), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(instance);
            }
        }
    }
}
