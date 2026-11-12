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
        [Test]
        public void DraftPrefabsMatchTheirCatalogsWithoutChangingAssets()
        {
            var drafts = AssetDatabase.FindAssets("t:LevelContentCatalog", new[] { "Assets/Content/GeneratedLevels" });
            Assert.That(drafts, Is.Not.Empty);
            foreach (var guid in drafts)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var before = AssetDatabase.GetAssetDependencyHash(path);
                var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(path);
                Assert.DoesNotThrow(() => LevelContentCatalogUtility.ValidateReferences(catalog), path);
                Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.EqualTo(before), path);
            }
        }

        [Test]
        public void GeneratedCampaignUsesOnlyItsLevelAndOwnBoltPalette()
        {
            var path = LevelContentCatalogUtility.CatalogAssetPath;
            var catalog = LevelTestFixtures.LoadCampaign();
            Assert.That(catalog.Levels.Select(level => level.Number), Is.EqualTo(new[] { 1 }));
            Assert.DoesNotThrow(() => LevelContentCatalogUtility.ValidateReferences(catalog));
            Assert.That(catalog.Palette.Select(color => color.Prefab).Distinct().Count(), Is.EqualTo(1));
            Assert.That(catalog.GetColor(1).Primary.name, Is.EqualTo("01_Yellow_Body"));
            Assert.That(catalog.GetColor(2).Primary.name, Is.EqualTo("02_Blue_Body"));
            Assert.That(AssetDatabase.GetDependencies(path, true)
                .Any(asset => asset.StartsWith("Assets/Content/3D/Levels/")), Is.False);
        }

        [TestCase("missingPrefab")]
        [TestCase("wrongJson")]
        [TestCase("missingMaterial")]
        [TestCase("invalidPaletteIndex")]
        [TestCase("duplicatePaletteIndex")]
        public void ReferenceValidationRejectsMissingOrMismatchedAssets(string defect)
        {
            var catalog = Object.Instantiate(LevelTestFixtures.LoadCampaign());
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
                                LevelTestFixtures.LoadCatDraft().GetLevel(1).Data;
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
            var entry = LevelTestFixtures.LoadCampaign().GetLevel(1);
            var source = LevelDataReader.ReadLevel(entry.Data.text, 1);
            var prefab = entry.Prefab;
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
            var entry = LevelTestFixtures.LoadCampaign().GetLevel(1);
            var path = AssetDatabase.GetAssetPath(entry.Prefab);
            var before = AssetDatabase.GetAssetDependencyHash(path);
            var source = LevelDataReader.ReadLevel(entry.Data.text, 1);
            var instance = Object.Instantiate(entry.Prefab);
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
