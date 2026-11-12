using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Screw.Levels;
using UnityEditor;
using UnityEngine;

namespace Screw.Editor.Levels
{
    public static class LevelGameplayDefinitionUtility
    {
        public static string LevelOnePath
        {
            get { return PreparedLevelUtility.LevelFolder(1) + "/Level_Gameplay.asset"; }
        }

        public static LevelGameplayData Build(LevelSourceData source, IReadOnlyList<int> route)
        {
            LevelDataValidator.ValidateLevel(source);
            if (route == null || route.Count != source.Bolts.Length
                || route.Distinct().Count() != route.Count
                || !route.OrderBy(id => id).SequenceEqual(source.Bolts.Select(bolt => bolt.Id).OrderBy(id => id)))
            {
                throw new InvalidDataException("Gameplay preparation requires a complete unique route.");
            }

            var colors = source.AllowedBoltPaletteIndices.Length;
            var gameplay = new LevelGameplayData
            {
                LevelNumber = source.Number,
                ColorGroups = Enumerable.Range(0, colors).Select(group => new ColorGroupData
                {
                    Id = group,
                    BoltIds = route.Where((id, index) => index / LevelDataValidator.ContainerCapacity % colors == group).ToArray()
                }).ToArray(),
                ContainerGroups = Enumerable.Range(0, route.Count / LevelDataValidator.ContainerCapacity)
                    .Select(index => index % colors).ToArray()
            };
            LevelDataValidator.ValidateGameplay(source, gameplay);
            return gameplay;
        }

        public static LevelGameplayDefinition Create(LevelContentCatalog catalog, LevelGameplayData gameplay)
        {
            var definition = ScriptableObject.CreateInstance<LevelGameplayDefinition>();
            try
            {
                using (var serialized = new SerializedObject(definition))
                {
                    serialized.FindProperty("_contentCatalog").objectReferenceValue = catalog;
                    var data = serialized.FindProperty("_gameplay");
                    data.FindPropertyRelative("LevelNumber").intValue = gameplay.LevelNumber;
                    var groups = data.FindPropertyRelative("ColorGroups");
                    groups.arraySize = gameplay.ColorGroups.Length;
                    for (var index = 0; index < groups.arraySize; index++)
                    {
                        var group = groups.GetArrayElementAtIndex(index);
                        group.FindPropertyRelative("Id").intValue = gameplay.ColorGroups[index].Id;
                        SetArray(group.FindPropertyRelative("BoltIds"), gameplay.ColorGroups[index].BoltIds);
                    }

                    SetArray(data.FindPropertyRelative("ContainerGroups"), gameplay.ContainerGroups);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                definition.ReadSource();
                return definition;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(definition);
                throw;
            }
        }

        [MenuItem("Tools/Screw/Levels/Create Level 01 Gameplay")]
        public static void CreateLevelOne()
        {
            var path = LevelOnePath;
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                throw new InvalidOperationException("Gameplay asset already exists; it was not overwritten.");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
            var source = LevelDataReader.ReadLevel(catalog.GetLevel(1).Data.text, LevelDataReader.SupportedSchemaVersion);
            var route = LevelRouteData.Read(File.ReadAllText(LevelRouteData.LevelOnePath), source);
            var definition = Create(catalog, Build(source, route.Steps.Select(step => step.BoltId).ToArray()));
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssetIfDirty(definition);
            Selection.activeObject = definition;
            Debug.Log("Created Level_01 gameplay from the saved spatial route: " + path);
        }

        private static void SetArray(SerializedProperty property, int[] values)
        {
            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).intValue = values[index];
            }
        }
    }
}
