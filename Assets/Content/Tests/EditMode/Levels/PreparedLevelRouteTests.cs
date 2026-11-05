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
        [Test]
        public void LevelTwoSavedCollidersRespectTheBudgetAndHaveNoPenetration()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/Prefabs/Levels/Level_02_View.prefab");
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

        [Test]
        public void LevelTwoWitnessReplaysThroughRealPointerSelectionAndRestoresBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/Prefabs/Levels/Level_02_View.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var cameraObject = new GameObject("PreparedRouteCamera");
            try
            {
                instance.transform.position = Vector3.right * 100f;
                var view = instance.GetComponent<LevelView>();
                var source = view.ValidateBindings();
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Content/Tests/Fixtures/LevelRoutes/Level_02.json");
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
