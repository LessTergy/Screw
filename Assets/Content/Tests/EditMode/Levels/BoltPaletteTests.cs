using System.Linq;
using NUnit.Framework;
using Screw.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Screw.Tests.Levels
{
    public sealed class BoltPaletteTests
    {
        private const string PalettePath = "Assets/Content/3D/Bolts/BoltPalette.asset";

        [Test]
        public void PaletteKeepsEveryIndexAndUsesTheDefaultPairOnTheCommonPrefab()
        {
            var palette = AssetDatabase.LoadAssetAtPath<BoltPalette>(PalettePath);
            Assert.That(palette, Is.Not.Null);
            Assert.That(palette.Colors.Select(entry => entry.Index),
                Is.EquivalentTo(Enumerable.Range(0, LevelDataValidator.PaletteSize)));
            Assert.That(palette.DefaultIndex, Is.EqualTo(7));

            var names = new[] { "Red", "Yellow", "Blue", "Green", "Purple", "Orange",
                "Pink", "White", "LightBlue", "Brown", "BlueGray", "Gray" };
            foreach (var entry in palette.Colors)
            {
                Assert.That(entry.Primary, Is.Not.Null);
                Assert.That(entry.Secondary, Is.Not.Null);
                Assert.That(entry.Primary.name, Does.Contain(names[entry.Index]));
                Assert.That(entry.Secondary.name, Does.Contain(names[entry.Index]));
                foreach (var material in new[] { entry.Primary, entry.Secondary })
                {
                    Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                    Assert.That(material.GetFloat("_WorkflowMode"), Is.EqualTo(1f));
                }

                Assert.That(entry.Secondary.GetColor("_BaseColor").grayscale,
                    Is.LessThan(entry.Primary.GetColor("_BaseColor").grayscale));
            }

            var defaultColor = palette.Colors.Single(entry => entry.Index == palette.DefaultIndex);
            Assert.That(palette.Prefab, Is.Not.Null);
            var mesh = palette.Prefab.transform.Find("ShowTrans/TurnTrans/Mesh");
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.GetComponent<MeshFilter>().sharedMesh.subMeshCount, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { defaultColor.Primary, defaultColor.Secondary },
                mesh.GetComponent<MeshRenderer>().sharedMaterials);
            Assert.That(AssetDatabase.GetDependencies(PalettePath, true)
                .Any(path => path.StartsWith("Assets/Content/3D/Levels/")), Is.False);
        }

        [Test]
        public void HeadBoxHitsTheFaceAndEdgeAndMissesTheShaftAndNearbySpace()
        {
            var palette = AssetDatabase.LoadAssetAtPath<BoltPalette>(PalettePath);
            Assert.That(palette, Is.Not.Null);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var bolt = (GameObject)PrefabUtility.InstantiatePrefab(palette.Prefab, scene);
                var head = bolt.GetComponent<BoxCollider>();
                Assert.That(head, Is.Not.Null);
                Assert.That(head.enabled && !head.isTrigger, Is.True);
                CollectionAssert.AreEqual(new Collider[] { head }, bolt.GetComponentsInChildren<Collider>(true));
                Assert.That(bolt.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                Physics.SyncTransforms();

                Assert.That(head.Raycast(new Ray(Vector3.forward, Vector3.back), out var hit, 2f), Is.True);
                Assert.That(hit.point.z, Is.EqualTo(0.249943f).Within(0.001f));
                Assert.That(head.Raycast(new Ray(new Vector3(0.38f, 0f, 1f), Vector3.back), out _, 2f), Is.True);
                Assert.That(head.Raycast(new Ray(new Vector3(0.42f, 0f, 1f), Vector3.back), out _, 2f), Is.False);
                Assert.That(head.Raycast(new Ray(new Vector3(1f, 0f, -0.2f), Vector3.left), out _, 2f), Is.False);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
