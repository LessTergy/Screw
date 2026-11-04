using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Screw.Editor.Levels;
using Screw.Gameplay;
using Screw.Levels;
using UnityEditor;
using UnityEngine;

namespace Screw.Tests.Levels
{
    public sealed class LevelPreparationTests
    {
        [TestCase(2, 3)]
        [TestCase(5, 5)]
        [TestCase(5, 6)]
        [TestCase(5, 10)]
        [TestCase(10, 10)]
        [TestCase(10, 11)]
        [TestCase(10, 20)]
        public void PreparationFollowsRouteAcrossPaletteBoundariesAndWins(int colors, int triples)
        {
            var source = LevelTestFixtures.Source(colors);
            source.Bolts = source.Bolts.Take(triples * 3).ToArray();
            foreach (var part in source.Parts)
            {
                part.BoltIds = part.BoltIds.Intersect(source.Bolts.Select(bolt => bolt.Id)).ToArray();
            }

            source.Parts = source.Parts.Where(part => part.BoltIds.Length > 0).ToArray();
            var route = source.Bolts.Select(bolt => bolt.Id).Reverse().ToArray();
            var before = JsonConvert.SerializeObject(source);
            var gameplay = LevelGameplayDefinitionUtility.Build(source, route);
            CollectionAssert.AreEqual(Enumerable.Range(0, triples).Select(index => index % colors), gameplay.ContainerGroups);
            var attempt = new LevelAttempt(source, gameplay);
            for (var index = 0; index < route.Length; index++)
            {
                var id = route[index];
                var bolt = attempt.Bolts[id];
                Assert.That(bolt.ColorGroupId, Is.EqualTo(index / 3 % colors));
                Assert.That(bolt.PaletteIndex, Is.EqualTo(source.AllowedBoltPaletteIndices[index / 3 % colors]));
                var remaining = attempt.RemainingBoltsByPart[bolt.OwnerId];
                var reserved = attempt.ReserveBolt(id);
                Assert.That(reserved.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
                Assert.That(reserved.Turn.Destination.IsPool, Is.False);
                Assert.That(attempt.ConfirmDetachment(reserved.Turn), Is.True);
                Assert.That(reserved.Turn.ReleasedPartId, Is.EqualTo(remaining == 1 ? (int?)bolt.OwnerId : null));
                Assert.That(attempt.ConfirmArrival(reserved.Turn), Is.True);
                Assert.That(attempt.PoolCount, Is.Zero);
                Assert.That(attempt.CompleteTurn(reserved.Turn), Is.True);
                Assert.That(attempt.CollectedBoltIds.Count, Is.EqualTo((index + 1) / 3 * 3));
                Assert.That(attempt.Status, Is.EqualTo(index == route.Length - 1 ? AttemptStatus.Won : AttemptStatus.Active));
            }

            Assert.That(attempt.ContainerQueue, Is.Empty);
            Assert.That(attempt.Containers.All(container => !container.IsActive), Is.True);
            Assert.That(attempt.RemainingBoltsByPart.Values.All(count => count == 0), Is.True);
            Assert.That(JsonConvert.SerializeObject(source), Is.EqualTo(before));
            Assert.That(JsonConvert.SerializeObject(LevelGameplayDefinitionUtility.Build(source, route)),
                Is.EqualTo(JsonConvert.SerializeObject(gameplay)));
        }

        [TestCase("missing")]
        [TestCase("repeated")]
        [TestCase("foreign")]
        [TestCase("null")]
        public void PreparationRejectsIncompleteOrInvalidOrder(string defect)
        {
            var source = LevelTestFixtures.Source();
            var route = source.Bolts.Select(bolt => bolt.Id).ToArray();
            switch (defect)
            {
                case "missing": route = route.Skip(1).ToArray(); break;
                case "repeated": route[1] = route[0]; break;
                case "foreign": route[0] = 999; break;
                case "null": route = null; break;
            }

            Assert.Throws<InvalidDataException>(() => LevelGameplayDefinitionUtility.Build(source, route));
        }

        [TestCase("short")]
        [TestCase("duplicate")]
        [TestCase("insufficientBolts")]
        [TestCase("notDivisible")]
        [TestCase("outOfRange")]
        public void PreparationRejectsInvalidSourcePaletteOrBoltCount(string defect)
        {
            var source = LevelTestFixtures.Source();
            switch (defect)
            {
                case "short": source.AllowedBoltPaletteIndices = new[] { 1 }; break;
                case "duplicate": source.AllowedBoltPaletteIndices = new[] { 1, 1 }; break;
                case "insufficientBolts": source.AllowedBoltPaletteIndices = new[] { 0, 1, 2, 3, 4 }; break;
                case "notDivisible": source.Bolts = source.Bolts.Skip(1).ToArray(); break;
                case "outOfRange": source.AllowedBoltPaletteIndices = new[] { 1, 12 }; break;
            }

            Assert.Throws<InvalidDataException>(() => LevelGameplayDefinitionUtility.Build(source, source.Bolts.Select(bolt => bolt.Id).ToArray()));
        }

        [TestCase("schema")]
        [TestCase("level")]
        [TestCase("missingStep")]
        [TestCase("duplicateId")]
        [TestCase("unknownId")]
        [TestCase("nullStep")]
        [TestCase("camera")]
        [TestCase("rotation")]
        [TestCase("missingRotation")]
        [TestCase("shortRotation")]
        [TestCase("nanRotation")]
        [TestCase("infiniteRotation")]
        [TestCase("viewport")]
        [TestCase("fov")]
        [TestCase("aspect")]
        public void RouteReaderRejectsDamagedWitness(string defect)
        {
            var source = LevelTestFixtures.Source();
            var json = JObject.FromObject(ValidRoute(source));
            var first = json["steps"][0];
            switch (defect)
            {
                case "schema": json["schemaVersion"] = 1; break;
                case "level": json["levelNumber"] = 2; break;
                case "missingStep": first.Remove(); break;
                case "duplicateId": json["steps"][1]["boltId"] = first["boltId"].DeepClone(); break;
                case "unknownId": first["boltId"] = 999; break;
                case "nullStep": json["steps"][0] = null; break;
                case "camera": json["camera"] = null; break;
                case "rotation": first["levelEulerDegrees"] = null; break;
                case "missingRotation": first["levelEulerDegrees"].Parent.Remove(); break;
                case "shortRotation": first["levelEulerDegrees"] = new JArray(0f, 0f); break;
                case "nanRotation": first["levelEulerDegrees"][0] = float.NaN; break;
                case "infiniteRotation": first["levelEulerDegrees"][0] = float.PositiveInfinity; break;
                case "viewport": first["viewportPoint"][0] = 1.01f; break;
                case "fov": json["camera"]["fieldOfViewDegrees"] = 180f; break;
                case "aspect": json["camera"]["aspect"] = 0f; break;
            }

            var text = json.ToString();
            Assert.Throws<InvalidDataException>(() => LevelRouteData.Read(text, source));
        }

        [Test]
        public void RouteReaderRequiresFieldsAndRejectsDuplicateProperties()
        {
            var source = LevelTestFixtures.Source();
            var json = JObject.FromObject(ValidRoute(source));
            json["camera"]["fieldOfViewDegrees"].Parent.Remove();
            Assert.Throws<InvalidDataException>(() => LevelRouteData.Read(json.ToString(), source));
            Assert.Throws<InvalidDataException>(() => LevelRouteData.Read("{\"schemaVersion\":1,\"schemaVersion\":1}", source));
        }

        [Test]
        public void SavedLevelOneDefinitionMatchesWitnessAndReloadsWithoutChangingSourceAssets()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
            var sourcePath = AssetDatabase.GetAssetPath(catalog.GetLevel(1).Data);
            var sourceText = File.ReadAllText(sourcePath);
            var catalogHash = AssetDatabase.GetAssetDependencyHash(LevelContentCatalogUtility.CatalogAssetPath);
            var source = LevelDataReader.ReadLevel(sourceText, LevelDataReader.SupportedSchemaVersion);
            var route = LevelRouteData.Read(File.ReadAllText(LevelRouteData.LevelOnePath), source);
            var expected = LevelGameplayDefinitionUtility.Build(source, route.Steps.Select(step => step.BoltId).ToArray());
            var definition = AssetDatabase.LoadAssetAtPath<LevelGameplayDefinition>(LevelGameplayDefinitionUtility.LevelOnePath);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.ContentCatalog, Is.SameAs(catalog));
            Assert.That(definition.ReadSource().Number, Is.EqualTo(1));
            Assert.That(JsonConvert.SerializeObject(definition.Gameplay), Is.EqualTo(JsonConvert.SerializeObject(expected)));
            CollectionAssert.AreEqual(new[] { 1, 2 }, source.AllowedBoltPaletteIndices);
            CollectionAssert.AreEqual(new[] { 6, 3 }, definition.Gameplay.ColorGroups.Select(group => group.BoltIds.Length));
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, definition.Gameplay.ContainerGroups);
            var before = JsonConvert.SerializeObject(definition.Gameplay);
            AssetDatabase.ImportAsset(LevelGameplayDefinitionUtility.LevelOnePath, ImportAssetOptions.ForceUpdate);
            definition = AssetDatabase.LoadAssetAtPath<LevelGameplayDefinition>(LevelGameplayDefinitionUtility.LevelOnePath);
            Assert.That(definition.ReadSource().Number, Is.EqualTo(1));
            Assert.That(JsonConvert.SerializeObject(definition.Gameplay), Is.EqualTo(before));
            Assert.That(AssetDatabase.GetAssetDependencyHash(LevelContentCatalogUtility.CatalogAssetPath), Is.EqualTo(catalogHash));
            Assert.That(File.ReadAllText(sourcePath), Is.EqualTo(sourceText));

            using (var serialized = new SerializedObject(definition))
            {
                Assert.That(serialized.FindProperty("_route"), Is.Null, "Production content must not depend on the witness.");
            }
        }

        private static LevelRouteData ValidRoute(LevelSourceData source)
        {
            return new LevelRouteData
            {
                SchemaVersion = 2,
                LevelNumber = source.Number,
                Camera = new RouteCameraData { FieldOfViewDegrees = 60f, Aspect = 9f / 16f },
                Steps = source.Bolts.Select(bolt => new RouteStepData
                {
                    BoltId = bolt.Id,
                    LevelEulerDegrees = new float[3],
                    ViewportPoint = new[] { 0.5f, 0.5f }
                }).ToArray()
            };
        }
    }
}
