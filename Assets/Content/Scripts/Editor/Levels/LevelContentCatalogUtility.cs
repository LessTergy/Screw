using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Screw.Editor.Levels
{
    public static class LevelContentCatalogUtility
    {
        public const string ContentRoot = "Assets/Content/3D/Levels";
        public const string CatalogAssetPath = "Assets/Content/Settings/LevelContentCatalog.asset";

        [MenuItem("Tools/Screw/Levels/Validate Imported Content")]
        public static void ValidateImportedContent()
        {
            Debug.Log(ValidateImportedAssets());
        }

        [MenuItem("Tools/Screw/Levels/Create Content Catalog")]
        public static void CreateContentCatalog()
        {
            var absolutePath = Path.Combine(Application.dataPath, "Content/Settings/LevelContentCatalog.asset");
            if (File.Exists(absolutePath) || Directory.Exists(absolutePath)
                || AssetDatabase.LoadMainAssetAtPath(CatalogAssetPath) != null)
            {
                throw new InvalidOperationException("Content catalog already exists. It was not overwritten: " + CatalogAssetPath);
            }

            var catalog = CreateImportedCatalog();
            try
            {
                if (!AssetDatabase.IsValidFolder("Assets/Content/Settings"))
                {
                    AssetDatabase.CreateFolder("Assets/Content", "Settings");
                }

                AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = catalog;
                Debug.Log("Created content references without modifying source levels: " + CatalogAssetPath);
            }
            catch
            {
                if (!AssetDatabase.Contains(catalog))
                {
                    Object.DestroyImmediate(catalog);
                }

                throw;
            }
        }

        public static string ValidateImportedAssets()
        {
            var catalog = CreateImportedCatalog();
            try
            {
                var source = LevelDataReader.ReadCatalog(catalog.SourceCatalog.text);
                return "Validated " + source.Levels.Length + " source levels, "
                    + source.PartCount + " parts, " + source.BoltCount + " bolts and "
                    + source.BoltPalette.Length + " palette references. Source assets were not modified.";
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        public static LevelContentCatalog CreateImportedCatalog()
        {
            var sourceAsset = Load<TextAsset>(ContentRoot + "/Data/catalog.json");
            var source = LevelDataReader.ReadCatalog(sourceAsset.text);
            var result = ScriptableObject.CreateInstance<LevelContentCatalog>();
            try
            {
                using (var serialized = new SerializedObject(result))
                {
                    serialized.FindProperty("_sourceCatalog").objectReferenceValue = sourceAsset;
                    var levels = serialized.FindProperty("_levels");
                    levels.arraySize = source.Levels.Length;
                    var orderedLevels = source.Levels.OrderBy(level => level.Number).ToArray();
                    for (var i = 0; i < orderedLevels.Length; i++)
                    {
                        var number = orderedLevels[i].Number;
                        var entry = levels.GetArrayElementAtIndex(i);
                        entry.FindPropertyRelative("_number").intValue = number;
                        entry.FindPropertyRelative("_data").objectReferenceValue = Load<TextAsset>(LevelDataPath(number));
                        entry.FindPropertyRelative("_prefab").objectReferenceValue = Load<GameObject>(LevelPrefabPath(number));
                    }

                    var palette = serialized.FindProperty("_palette");
                    palette.arraySize = source.BoltPalette.Length;
                    var orderedColors = source.BoltPalette.OrderBy(color => color.Index).ToArray();
                    for (var i = 0; i < orderedColors.Length; i++)
                    {
                        var color = orderedColors[i];
                        var entry = palette.GetArrayElementAtIndex(i);
                        entry.FindPropertyRelative("_index").intValue = color.Index;
                        entry.FindPropertyRelative("_prefab").objectReferenceValue = Load<GameObject>(BoltPrefabPath(color.Index));
                        entry.FindPropertyRelative("_primary").objectReferenceValue = Load<Material>(MaterialPath(color.Primary));
                        entry.FindPropertyRelative("_secondary").objectReferenceValue = Load<Material>(MaterialPath(color.Secondary));
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                ValidateReferences(result);
                return result;
            }
            catch
            {
                Object.DestroyImmediate(result);
                throw;
            }
        }

        public static void ValidateReferences(LevelContentCatalog content)
        {
            ValidateReferences(content, draftLevel: null);
        }

        internal static void ValidateReferences(LevelContentCatalog content, int? draftLevel)
        {
            var requireCampaignPaths = !draftLevel.HasValue;
            Require(content != null && content.SourceCatalog != null, "Source catalog reference is missing.");
            var source = LevelDataReader.ReadCatalog(content.SourceCatalog.text);
            Require(content.Levels.Count == source.Levels.Length && content.Palette.Count == source.BoltPalette.Length,
                "Content reference counts do not match the source catalog.");
            Require(!requireCampaignPaths || AssetDatabase.GetAssetPath(content.SourceCatalog) == ContentRoot + "/Data/catalog.json",
                "Content catalog must reference the current imported source catalog.");
            var rawCatalog = JObject.Parse(content.SourceCatalog.text);
            var numbers = new HashSet<int>();
            foreach (var entry in content.Levels)
            {
                Require(entry != null && entry.Data != null && entry.Prefab != null, "Level asset reference is missing.");
                Require(numbers.Add(entry.Number), "Duplicate level asset number.");
                Require(!requireCampaignPaths || (AssetDatabase.GetAssetPath(entry.Data) == LevelDataPath(entry.Number)
                    && AssetDatabase.GetAssetPath(entry.Prefab) == LevelPrefabPath(entry.Number)), "Level asset paths do not match their number.");
                var level = LevelDataReader.ReadLevel(entry.Data.text, source.SchemaVersion);
                Require(level.Number == entry.Number, "Referenced JSON has a different level number.");
                var sourceToken = rawCatalog["levels"].Single(item => (int)item["number"] == entry.Number);
                Require(JToken.DeepEquals(sourceToken, JObject.Parse(entry.Data.text)),
                    "Catalog and separate JSON differ for level " + entry.Number + ".");
                if (requireCampaignPaths || entry.Number == draftLevel)
                {
                    ValidateLevelPrefab(level, entry.Prefab);
                }
            }

            var indices = new HashSet<int>();
            foreach (var entry in content.Palette)
            {
                Require(entry != null && entry.Prefab != null && entry.Primary != null && entry.Secondary != null,
                    "Palette asset reference is missing.");
                Require(entry.Index >= 0 && entry.Index < LevelDataValidator.PaletteSize && indices.Add(entry.Index),
                    "Invalid or duplicate palette asset index: " + entry.Index + ".");
                var color = source.BoltPalette.Single(item => item.Index == entry.Index);
                Require(AssetDatabase.GetAssetPath(entry.Prefab) == BoltPrefabPath(entry.Index)
                    && AssetDatabase.GetAssetPath(entry.Primary) == MaterialPath(color.Primary)
                    && AssetDatabase.GetAssetPath(entry.Secondary) == MaterialPath(color.Secondary),
                    "Palette asset paths do not match the source palette.");
                var renderer = entry.Prefab.GetComponentInChildren<MeshRenderer>(true);
                Require(renderer != null && renderer.sharedMaterials.Length == 2
                    && renderer.sharedMaterials[0] == entry.Primary && renderer.sharedMaterials[1] == entry.Secondary,
                    "Bolt palette must reference both renderer material slots.");
                ValidateMesh(renderer.gameObject, "Palette " + entry.Index);
            }
        }

        public static void ValidateLevelPrefab(LevelSourceData source, GameObject prefab)
        {
            LevelDataValidator.ValidateLevel(source);
            Require(prefab != null, "Level prefab reference is missing.");
            var root = prefab.transform;
            var assembly = root.Find("Assembly");
            var model = root.Find("Assembly/Model");
            var bolts = root.Find("Assembly/Bolts");
            Require(assembly != null && model != null && bolts != null, "Level hierarchy is incomplete: " + source.Number + ".");
            Require(model.childCount == source.Parts.Length && bolts.childCount == source.Bolts.Length,
                "Prefab child counts do not match source data.");
            Require(Vector3.Distance(root.localPosition, Vector3.zero) < 0.0001f
                && Quaternion.Angle(root.localRotation, Quaternion.identity) < 0.01f
                && Vector3.Distance(root.localScale, Vector3.one) < 0.0001f, "Imported level root transform changed.");
            CheckTransform(assembly, Vector3.zero, Vector(source.RootEulerDegrees), Vector3.one * (float)source.RootScale);
            CheckTransform(model, Vector(source.ModelPosition), Vector(source.ModelEulerDegrees), Vector3.one * (float)source.ModelScale);
            foreach (var part in source.Parts)
            {
                var node = root.Find(part.NodePath);
                Require(node != null && node == model.GetChild(part.ChildIndex) && node.name == part.Name,
                    "Part path/childIndex mismatch in level " + source.Number + ", part " + part.Id + ".");
                ValidateMesh(node.gameObject, "Part " + part.Id);
            }

            foreach (var bolt in source.Bolts)
            {
                var node = root.Find(bolt.NodePath);
                Require(node != null && node.parent == bolts, "Missing bolt node: " + bolt.NodePath + ".");
                Require(Vector3.Distance(node.localPosition, Vector(bolt.Position)) < 0.0001f
                    && Quaternion.Angle(node.localRotation, Quaternion.Euler(Vector(bolt.EulerDegrees))) < 0.01f,
                    "Bolt pose differs from JSON: " + bolt.NodePath + ".");
                var mesh = node.Find("ShowTrans/TurnTrans/Mesh");
                Require(mesh != null, "Bolt visual hierarchy is missing: " + bolt.NodePath + ".");
                ValidateMesh(mesh.gameObject, "Bolt " + bolt.Id);
            }
        }

        public static string LevelDataPath(int number)
        {
            return ContentRoot + "/Data/Level_" + number.ToString("D2") + ".json";
        }

        public static string LevelPrefabPath(int number)
        {
            return ContentRoot + "/Prefabs/Level_" + number.ToString("D2") + ".prefab";
        }

        private static string BoltPrefabPath(int index)
        {
            return ContentRoot + "/Bolts/Prefabs/Bolt_" + index.ToString("D2") + ".prefab";
        }

        private static string MaterialPath(string sourcePath)
        {
            return ContentRoot + "/Bolts/Materials/" + Path.GetFileName(sourcePath);
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Require(asset != null, "Missing imported asset: " + path);
            return asset;
        }

        private static void ValidateMesh(GameObject node, string context)
        {
            var filter = node.GetComponent<MeshFilter>();
            var renderer = node.GetComponent<MeshRenderer>();
            Require(filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0
                && renderer != null && renderer.sharedMaterials.Length > 0
                && renderer.sharedMaterials.All(material => material != null), context + ": mesh or material reference is missing.");
        }

        private static void CheckTransform(Transform node, Vector3 position, Vector3 rotation, Vector3 scale)
        {
            Require(Vector3.Distance(node.localPosition, position) < 0.0001f
                && Quaternion.Angle(node.localRotation, Quaternion.Euler(rotation)) < 0.01f
                && Vector3.Distance(node.localScale, scale) < 0.0001f, "Source transform differs on " + node.name + ".");
        }

        private static Vector3 Vector(double[] value)
        {
            return new Vector3((float)value[0], (float)value[1], (float)value[2]);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
