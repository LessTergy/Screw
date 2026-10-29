using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Screw.Levels;

namespace Screw.Tests.Levels
{
    public sealed class LevelReaderPlayerTests
    {
        [Test]
        public void PlayerReaderPreservesOwnershipPaletteAndRequiredFields()
        {
            var json = JsonConvert.SerializeObject(Source());
            var first = LevelDataReader.ReadLevel(json, 1);
            var second = LevelDataReader.ReadLevel(json, 1);
            CollectionAssert.AreEqual(new[] { 1, 2 }, first.AllowedBoltPaletteIndices);
            Assert.That(first.Bolts.All(bolt => bolt.OwnerId == 0), Is.True);
            CollectionAssert.AreEqual(Enumerable.Range(0, 6), first.Parts[0].BoltIds);
            first.Parts[0].BoltIds[0] = 999;
            Assert.That(second.Parts[0].BoltIds[0], Is.EqualTo(0));

            var missingOwner = JObject.Parse(json);
            ((JProperty)missingOwner["bolts"][0]["ownerId"].Parent).Remove();
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadLevel(missingOwner.ToString(), 1));
        }

        private static LevelSourceData Source()
        {
            return new LevelSourceData
            {
                Number = 1,
                SourceCampaignIndex = 0,
                SourceModelId = 1,
                SourceName = "PlayerFixture",
                ModelPosition = new double[3],
                ModelEulerDegrees = new double[3],
                RootEulerDegrees = new double[3],
                ModelScale = 1,
                RootScale = 1,
                AllowedBoltPaletteIndices = new[] { 1, 2 },
                SourcePrefab = "source.prefab",
                Prefab = "level.prefab",
                Glb = "level.glb",
                Parts = new[]
                {
                    new PartSourceData
                    {
                        Id = 0, ChildIndex = 0, Name = "Part", NodePath = "Assembly/Model/Part",
                        SourceReleaseForce = new double[3], SourceSpin = true,
                        BoltIds = Enumerable.Range(0, 6).ToArray()
                    }
                },
                Bolts = Enumerable.Range(0, 6).Select(id => new BoltSourceData
                {
                    Id = id, OwnerId = 0, Position = new double[3], EulerDegrees = new double[3],
                    SourceWhitelist = "", NodePath = "Assembly/Bolts/Bolt_" + id
                }).ToArray()
            };
        }
    }
}
