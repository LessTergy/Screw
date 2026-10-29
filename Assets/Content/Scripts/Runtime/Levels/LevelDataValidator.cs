using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Screw.Levels
{
    public static class LevelDataValidator
    {
        public const int CampaignLevelCount = 20;
        public const int PaletteSize = 12;
        public const int ContainerCapacity = 3;
        public const int PoolCapacity = 5;

        public static void ValidateCatalog(LevelSourceCatalogData catalog)
        {
            Require(catalog != null, "Catalog is missing.");
            Require(catalog.SchemaVersion == LevelDataReader.SupportedSchemaVersion, "Unsupported catalog schema.");
            Require(catalog.Levels != null && catalog.Levels.Length == CampaignLevelCount, "Expected 20 source levels.");
            Require(catalog.BoltPalette != null && catalog.BoltPalette.Length == PaletteSize, "Expected 12 palette entries.");
            Require(IsFinite(catalog.RootScreenRatio) && catalog.RootScreenRatio > 0, "Invalid root screen ratio.");
            Require(!string.IsNullOrWhiteSpace(catalog.SourceCommit)
                && !string.IsNullOrWhiteSpace(catalog.SourceUnityVersion)
                && !string.IsNullOrWhiteSpace(catalog.UnityRenderPipeline)
                && !string.IsNullOrWhiteSpace(catalog.CoordinateSystem)
                && !string.IsNullOrWhiteSpace(catalog.BoltColors), "Catalog provenance is incomplete.");

            var numbers = new HashSet<int>();
            foreach (var level in catalog.Levels)
            {
                ValidateLevel(level);
                Require(numbers.Add(level.Number), "Duplicate level number: " + level.Number + ".");
            }

            Require(catalog.PartCount == catalog.Levels.Sum(level => level.Parts.Length), "Catalog part count does not match.");
            Require(catalog.BoltCount == catalog.Levels.Sum(level => level.Bolts.Length), "Catalog bolt count does not match.");
            var paletteIndices = new HashSet<int>();
            foreach (var color in catalog.BoltPalette)
            {
                Require(color != null, "Palette entry is missing.");
                Require(color.Index >= 0 && color.Index < PaletteSize && paletteIndices.Add(color.Index), "Invalid or duplicate palette index.");
                Require(!string.IsNullOrWhiteSpace(color.Primary) && !string.IsNullOrWhiteSpace(color.Secondary)
                    && !string.IsNullOrWhiteSpace(color.Prefab) && !string.IsNullOrWhiteSpace(color.Glb), "Palette paths are missing.");
                ValidateVector(color.PrimarySrgb, 4, "primarySrgb");
                ValidateVector(color.SecondarySrgb, 4, "secondarySrgb");
                Require(color.PrimarySrgb.Concat(color.SecondarySrgb).All(value => value >= 0 && value <= 1), "Palette sRGB must be in [0, 1].");
            }

            Require(paletteIndices.Contains(catalog.BoltDefaultPaletteIndex), "Default palette index is missing.");
        }

        public static void ValidateLevel(LevelSourceData level)
        {
            Require(level != null, "Level is missing.");
            Require(level.Number >= 1 && level.Number <= CampaignLevelCount, "Level number must be in [1, 20].");
            var context = "Level " + level.Number + ": ";
            Require(level.SourceCampaignIndex == level.Number - 1, context + "invalid campaign index.");
            Require(level.SourceModelId > 0 && !string.IsNullOrWhiteSpace(level.SourceName), context + "model identity is missing.");
            Require(!string.IsNullOrWhiteSpace(level.SourcePrefab) && !string.IsNullOrWhiteSpace(level.Prefab)
                && !string.IsNullOrWhiteSpace(level.Glb), context + "source paths are missing.");
            ValidateVector(level.ModelPosition, 3, context + "modelPosition");
            ValidateVector(level.ModelEulerDegrees, 3, context + "modelEulerDegrees");
            ValidateVector(level.RootEulerDegrees, 3, context + "rootEulerDegrees");
            Require(IsFinite(level.ModelScale) && level.ModelScale > 0
                && IsFinite(level.RootScale) && level.RootScale > 0, context + "scales must be positive and finite.");
            Require(level.Parts != null && level.Parts.Length > 0, context + "parts are missing.");
            Require(level.Bolts != null && level.Bolts.Length > 0
                && level.Bolts.Length % ContainerCapacity == 0, context + "bolt count must be positive and divisible by three.");
            Require(level.AllowedBoltPaletteIndices != null && level.AllowedBoltPaletteIndices.Length >= 2
                && level.AllowedBoltPaletteIndices.Length <= level.Bolts.Length / ContainerCapacity, context + "invalid palette size.");
            var paletteIndices = new HashSet<int>();
            foreach (var index in level.AllowedBoltPaletteIndices)
            {
                Require(index >= 0 && index < PaletteSize && paletteIndices.Add(index), context + "invalid or duplicate palette index.");
            }

            var parts = new Dictionary<int, PartSourceData>();
            var childIndices = new HashSet<int>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in level.Parts)
            {
                Require(part != null && part.Id >= 0, context + "invalid part.");
                Require(!parts.ContainsKey(part.Id), context + "duplicate part ID: " + part.Id + ".");
                parts.Add(part.Id, part);
                Require(part.ChildIndex >= 0 && part.ChildIndex < level.Parts.Length
                    && childIndices.Add(part.ChildIndex), context + "invalid or duplicate childIndex.");
                Require(!string.IsNullOrWhiteSpace(part.Name) && part.Name.IndexOf('/') < 0
                    && part.Name != "." && part.Name != ".."
                    && part.NodePath == "Assembly/Model/" + part.Name
                    && paths.Add(part.NodePath), context + "invalid or duplicate part path.");
                Require(part.BoltIds != null && part.BoltIds.Length > 0, context + "part has no bolts.");
                ValidateVector(part.SourceReleaseForce, 3, context + "sourceReleaseForce");
            }

            var bolts = new Dictionary<int, BoltSourceData>();
            foreach (var bolt in level.Bolts)
            {
                Require(bolt != null && bolt.Id >= 0, context + "invalid bolt.");
                Require(!bolts.ContainsKey(bolt.Id), context + "duplicate bolt ID: " + bolt.Id + ".");
                bolts.Add(bolt.Id, bolt);
                Require(parts.ContainsKey(bolt.OwnerId), context + "unknown owner for bolt " + bolt.Id + ".");
                Require(IsDirectChildPath(bolt.NodePath, "Assembly/Bolts/")
                    && paths.Add(bolt.NodePath), context + "invalid or duplicate bolt path.");
                Require(bolt.SourceWhitelist != null, context + "sourceWhitelist is missing.");
                ValidateVector(bolt.Position, 3, context + "bolt position");
                ValidateVector(bolt.EulerDegrees, 3, context + "bolt rotation");
            }

            var attachedBolts = new HashSet<int>();
            foreach (var part in level.Parts)
            {
                foreach (var id in part.BoltIds)
                {
                    Require(bolts.TryGetValue(id, out var bolt), context + "part references unknown bolt " + id + ".");
                    Require(bolt.OwnerId == part.Id && attachedBolts.Add(id), context + "inconsistent or duplicate attachment for bolt " + id + ".");
                }
            }

            Require(attachedBolts.Count == bolts.Count, context + "some bolts are missing from their owners.");
        }

        public static void ValidateGameplay(LevelSourceData source, LevelGameplayData gameplay)
        {
            ValidateLevel(source);
            Require(gameplay != null && gameplay.LevelNumber == source.Number, "Gameplay source level does not match.");
            var colorCount = source.AllowedBoltPaletteIndices.Length;
            Require(gameplay.ColorGroups != null && gameplay.ColorGroups.Length == colorCount, "Gameplay color group count does not match palette.");
            var sourceIds = new HashSet<int>(source.Bolts.Select(bolt => bolt.Id));
            var assigned = new HashSet<int>();
            var counts = new Dictionary<int, int>();
            foreach (var group in gameplay.ColorGroups)
            {
                Require(group != null && group.Id >= 0 && group.Id < colorCount
                    && !counts.ContainsKey(group.Id), "Invalid or duplicate color group ID.");
                Require(group.BoltIds != null && group.BoltIds.Length > 0
                    && group.BoltIds.Length % ContainerCapacity == 0, "Color group bolt count must be positive and divisible by three.");
                foreach (var id in group.BoltIds)
                {
                    Require(sourceIds.Contains(id) && assigned.Add(id), "Unknown or repeated bolt in color groups: " + id + ".");
                }

                counts.Add(group.Id, group.BoltIds.Length);
            }

            Require(assigned.SetEquals(sourceIds), "Color groups must cover all source bolts.");
            Require(gameplay.ContainerGroups != null
                && gameplay.ContainerGroups.Length == source.Bolts.Length / ContainerCapacity, "Container count does not match source bolts.");
            foreach (var id in gameplay.ContainerGroups)
            {
                Require(counts.ContainsKey(id), "Container references unknown color group.");
                counts[id] -= ContainerCapacity;
                Require(counts[id] >= 0, "Too many containers for color group " + id + ".");
            }

            Require(counts.Values.All(count => count == 0), "Container capacity does not match each color group.");
        }

        private static bool IsDirectChildPath(string path, string prefix)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var name = path.Substring(prefix.Length);
            return name.Length > 0 && name != "." && name != ".." && name.IndexOf('/') < 0;
        }

        private static void ValidateVector(double[] values, int length, string name)
        {
            Require(values != null && values.Length == length && values.All(IsFinite), name + " must contain " + length + " finite values.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= float.MaxValue;
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
