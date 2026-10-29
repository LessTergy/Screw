using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Screw.Editor.Levels;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Screw.Tests.Levels
{
    public sealed class LevelAssetTests
    {
        public static IEnumerable<int> LevelNumbers
        {
            get { return Enumerable.Range(1, 20); }
        }

        [TestCaseSource(nameof(LevelNumbers))]
        public void SourcePrefabMatchesItsData(int number)
        {
            var path = LevelContentCatalogUtility.LevelPrefabPath(number);
            var before = AssetDatabase.GetAssetDependencyHash(path);
            var source = LevelDataReader.ReadLevel(
                AssetDatabase.LoadAssetAtPath<TextAsset>(LevelContentCatalogUtility.LevelDataPath(number)).text, 1);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.DoesNotThrow(() => LevelContentCatalogUtility.ValidateLevelPrefab(source, prefab));
            Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.EqualTo(before));
        }

        [Test]
        public void ImportedReferencesUseCurrentPathsAndBothPaletteSlots()
        {
            var catalog = LevelContentCatalogUtility.CreateImportedCatalog();
            try
            {
                Assert.That(catalog.Levels.Count, Is.EqualTo(20));
                Assert.That(catalog.Palette.Count, Is.EqualTo(12));
                Assert.That(AssetDatabase.GetAssetPath(catalog.GetLevel(1).Prefab),
                    Is.EqualTo("Assets/Content/3D/Levels/Prefabs/Level_01.prefab"));
                Assert.That(catalog.GetColor(1).Primary.name, Is.EqualTo("2_yellow_1"));
                Assert.That(catalog.GetColor(1).Secondary.name, Is.EqualTo("2_yellow_2"));
                Assert.That(AssetDatabase.GetAssetPath(catalog), Is.Empty);
                Assert.DoesNotThrow(() => LevelContentCatalogUtility.ValidateReferences(catalog));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [TestCase("missingPrefab")]
        [TestCase("wrongJson")]
        [TestCase("duplicateLevel")]
        [TestCase("missingMaterial")]
        [TestCase("invalidPaletteIndex")]
        [TestCase("duplicatePaletteIndex")]
        public void ReferenceValidationRejectsMissingOrMismatchedAssets(string defect)
        {
            var catalog = LevelContentCatalogUtility.CreateImportedCatalog();
            try
            {
                using (var serialized = new SerializedObject(catalog))
                {
                    var level = serialized.FindProperty("_levels").GetArrayElementAtIndex(0);
                    switch (defect)
                    {
                        case "missingPrefab": { level.FindPropertyRelative("_prefab").objectReferenceValue = null; break; }
                        case "wrongJson":
                        {
                            level.FindPropertyRelative("_data").objectReferenceValue =
                                AssetDatabase.LoadAssetAtPath<TextAsset>(LevelContentCatalogUtility.LevelDataPath(2));
                            break;
                        }
                        case "duplicateLevel":
                        {
                            serialized.FindProperty("_levels").GetArrayElementAtIndex(1)
                                .FindPropertyRelative("_number").intValue = 1;
                            break;
                        }
                        case "missingMaterial":
                        {
                            serialized.FindProperty("_palette").GetArrayElementAtIndex(0)
                                .FindPropertyRelative("_primary").objectReferenceValue = null;
                            break;
                        }
                        case "invalidPaletteIndex":
                        {
                            serialized.FindProperty("_palette").GetArrayElementAtIndex(0)
                                .FindPropertyRelative("_index").intValue = 12;
                            break;
                        }
                        case "duplicatePaletteIndex":
                        {
                            serialized.FindProperty("_palette").GetArrayElementAtIndex(1)
                                .FindPropertyRelative("_index").intValue = 0;
                            break;
                        }
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                Assert.Throws<InvalidDataException>(() => LevelContentCatalogUtility.ValidateReferences(catalog));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void PrefabValidationRejectsWrongNodePathAndChildIndex()
        {
            var source = LevelDataReader.ReadLevel(
                AssetDatabase.LoadAssetAtPath<TextAsset>(LevelContentCatalogUtility.LevelDataPath(1)).text, 1);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelContentCatalogUtility.LevelPrefabPath(1));
            var firstIndex = source.Parts[0].ChildIndex;
            source.Parts[0].ChildIndex = source.Parts[1].ChildIndex;
            source.Parts[1].ChildIndex = firstIndex;
            Assert.Throws<InvalidDataException>(() => LevelContentCatalogUtility.ValidateLevelPrefab(source, prefab));
            source.Parts[0].ChildIndex = firstIndex;
            source.Parts[1].ChildIndex = 1;
            source.Parts[0].Name = "MissingPart";
            source.Parts[0].NodePath = "Assembly/Model/MissingPart";
            Assert.Throws<InvalidDataException>(() => LevelContentCatalogUtility.ValidateLevelPrefab(source, prefab));
        }

        [Test]
        public void PrefabValidationRejectsMissingMeshWithoutChangingSource()
        {
            var path = LevelContentCatalogUtility.LevelPrefabPath(1);
            var before = AssetDatabase.GetAssetDependencyHash(path);
            var source = LevelDataReader.ReadLevel(
                AssetDatabase.LoadAssetAtPath<TextAsset>(LevelContentCatalogUtility.LevelDataPath(1)).text, 1);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                var filter = instance.transform.Find(source.Parts[0].NodePath).GetComponent<MeshFilter>();
                Object.DestroyImmediate(filter);
                Assert.Throws<InvalidDataException>(() => LevelContentCatalogUtility.ValidateLevelPrefab(source, instance));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.EqualTo(before));
        }
    }
}
