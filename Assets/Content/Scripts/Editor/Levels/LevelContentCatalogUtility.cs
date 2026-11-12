using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Screw.Levels;
using UnityEditor;
using UnityEngine;

namespace Screw.Editor.Levels
{
    public static class LevelContentCatalogUtility
    {
        public const string CatalogAssetPath = "Assets/Content/Settings/LevelContentCatalog.asset";
        internal const string LevelsRoot = "Assets/Content/Levels";

        [MenuItem("Tools/Screw/Levels/Validate Campaign Content")]
        public static void ValidateCampaignContent()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(CatalogAssetPath);
            ValidateReferences(catalog);
            catalog.ValidateCampaign();
            Debug.Log("Validated " + catalog.Levels.Count + " campaign levels and " + catalog.Palette.Count
                + " bolt palette entries.", catalog);
        }

        public static void ValidateReferences(LevelContentCatalog content)
        {
            ValidateReferences(content, draftLevel: null);
        }

        internal static void ValidateReferences(LevelContentCatalog content, int? draftLevel)
        {
            Require(content != null && content.SourceCatalog != null, "Source catalog reference is missing.");
            var source = LevelDataReader.ReadCatalog(content.SourceCatalog.text);
            Require(content.Levels.Count == source.Levels.Length && content.Palette.Count == source.BoltPalette.Length,
                "Content reference counts do not match the source catalog.");
            var rawCatalog = JObject.Parse(content.SourceCatalog.text);
            var numbers = new HashSet<int>();
            foreach (var entry in content.Levels)
            {
                Require(entry != null && entry.Data != null && entry.Prefab != null, "Level asset reference is missing.");
                Require(numbers.Add(entry.Number), "Duplicate level asset number.");
                var level = LevelDataReader.ReadLevel(entry.Data.text, source.SchemaVersion);
                Require(level.Number == entry.Number, "Referenced JSON has a different level number.");
                Require(entry.Number == draftLevel || AssetDatabase.GetAssetPath(entry.Prefab) == level.Prefab,
                    "Level prefab path does not match the source data.");

                var sourceToken = rawCatalog["levels"].Single(item => (int)item["number"] == entry.Number);
                Require(JToken.DeepEquals(sourceToken, JObject.Parse(entry.Data.text)),
                    "Catalog and separate JSON differ for level " + entry.Number + ".");
                ValidateLevelPrefab(level, entry.Prefab);
            }

            var indices = new HashSet<int>();
            var defaultColor = source.BoltPalette.Single(color => color.Index == source.BoltDefaultPaletteIndex);
            foreach (var entry in content.Palette)
            {
                Require(entry != null && entry.Prefab != null && entry.Primary != null && entry.Secondary != null,
                    "Palette asset reference is missing.");
                Require(entry.Index >= 0 && entry.Index < LevelDataValidator.PaletteSize && indices.Add(entry.Index),
                    "Invalid or duplicate palette asset index: " + entry.Index + ".");
                var color = source.BoltPalette.Single(item => item.Index == entry.Index);
                Require(color.Prefab == defaultColor.Prefab
                    && AssetDatabase.GetAssetPath(entry.Prefab) == color.Prefab
                    && AssetDatabase.GetAssetPath(entry.Primary) == color.Primary
                    && AssetDatabase.GetAssetPath(entry.Secondary) == color.Secondary,
                    "Palette asset paths do not match the source palette.");
                if (entry.Index != defaultColor.Index)
                {
                    continue;
                }

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
