using System.Linq;
using Screw.Editor.Levels;
using Screw.Levels;
using UnityEditor;

namespace Screw.Tests.Levels
{
    internal static class LevelTestFixtures
    {
        public static LevelContentCatalog LoadCampaign()
        {
            return AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
        }

        public static LevelSourceData Source(int colors = 2)
        {
            var boltCount = colors * 6;
            var bolts = Enumerable.Range(0, boltCount).Select(id => new BoltSourceData
            {
                Id = id,
                OwnerId = id == 0 ? 0 : 1 + (id - 1) / 11,
                Position = new double[3],
                EulerDegrees = new double[3],
                SourceWhitelist = "",
                NodePath = "Assembly/Bolts/Bolt_" + id.ToString("D3")
            }).ToArray();
            var parts = bolts.GroupBy(bolt => bolt.OwnerId).Select(group => new PartSourceData
            {
                Id = group.Key,
                ChildIndex = group.Key,
                Name = "Owner_" + group.Key.ToString("D3") + "_Part",
                NodePath = "Assembly/Model/Owner_" + group.Key.ToString("D3") + "_Part",
                BoltIds = group.Select(bolt => bolt.Id).ToArray(),
                SourceLayer = 0,
                SourceReleaseForce = new double[3],
                SourceSpin = true
            }).ToArray();

            return new LevelSourceData
            {
                Number = 1,
                SourceCampaignIndex = 0,
                SourceModelId = 1,
                SourceName = "Fixture",
                ModelPosition = new double[3],
                ModelEulerDegrees = new double[3],
                RootEulerDegrees = new double[3],
                ModelScale = 1,
                RootScale = 1,
                AllowedBoltPaletteIndices = new[] { 7, 6, 8, 5, 3, 0, 1, 2, 4, 9 }.Take(colors).ToArray(),
                Parts = parts,
                Bolts = bolts,
                SourcePrefab = "Assets/Fixtures/Fixture.prefab",
                Prefab = "Assets/Fixtures/Level_01.prefab",
                Glb = "GLB/Level_01.glb"
            };
        }

        public static LevelGameplayData Gameplay(LevelSourceData source)
        {
            var colors = source.AllowedBoltPaletteIndices.Length;
            return new LevelGameplayData
            {
                LevelNumber = source.Number,
                ColorGroups = Enumerable.Range(0, colors).Select(group => new ColorGroupData
                {
                    Id = group,
                    BoltIds = source.Bolts.Where((bolt, index) => index / 3 % colors == group)
                        .Select(bolt => bolt.Id).ToArray()
                }).ToArray(),
                ContainerGroups = Enumerable.Range(0, source.Bolts.Length / 3)
                    .Select(index => index % colors).ToArray()
            };
        }
    }
}
