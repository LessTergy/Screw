using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Screw.Levels;

namespace Screw.Tests.Levels
{
    public sealed class LevelDataTests
    {
        [TestCase(1, 4, 9)]
        [TestCase(5, 8, 21)]
        public void ReadRealLevelPreservesExportData(int number, int parts, int bolts)
        {
            var asset = LevelTestFixtures.LoadCampaign().GetLevel(number).Data;
            var json = asset.text;
            var level = LevelDataReader.ReadLevel(json, 1);
            Assert.That(level.Number, Is.EqualTo(number));
            Assert.That(level.Parts.Length, Is.EqualTo(parts));
            Assert.That(level.Bolts.Length, Is.EqualTo(bolts));
            Assert.That(JToken.DeepEquals(JObject.Parse(json), JObject.FromObject(level)), Is.True);
            Assert.That(asset.text, Is.EqualTo(json));
        }

        [Test]
        public void ReadCampaignCatalogPreservesTotalsAndPaletteOrder()
        {
            var asset = LevelTestFixtures.LoadCampaign().SourceCatalog;
            var catalog = LevelDataReader.ReadCatalog(asset.text);
            Assert.That(catalog.Levels.Length, Is.EqualTo(5));
            Assert.That(catalog.PartCount, Is.EqualTo(31));
            Assert.That(catalog.BoltCount, Is.EqualTo(75));
            CollectionAssert.AreEqual(new[] { 1, 2 }, catalog.Levels[0].AllowedBoltPaletteIndices);
            CollectionAssert.AreEqual(Enumerable.Range(0, 12), catalog.BoltPalette.Select(color => color.Index));
            Assert.That(catalog.BoltPalette[1].Primary, Does.EndWith("01_Yellow_Body.mat"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase("{broken")]
        public void ReadRejectsEmptyOrDamagedJson(string json)
        {
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadLevel(json, 1));
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadCatalog(json));
        }

        [TestCase("number")]
        [TestCase("allowedBoltPaletteIndices")]
        [TestCase("parts[0].childIndex")]
        [TestCase("parts[0].sourceSpin")]
        [TestCase("bolts[0].ownerId")]
        [TestCase("bolts[0].position")]
        public void ReadRejectsMissingRequiredFieldsIncludingZeroValuedFields(string path)
        {
            var json = JObject.FromObject(LevelTestFixtures.Source());
            var property = (JProperty)json.SelectToken(path).Parent;
            property.Remove();
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadLevel(json.ToString(), 1));
        }

        [Test]
        public void ReadRejectsNullRequiredField()
        {
            var json = JObject.FromObject(LevelTestFixtures.Source());
            json["bolts"][0]["sourceWhitelist"] = JValue.CreateNull();
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadLevel(json.ToString(), 1));
        }

        [Test]
        public void ReadRejectsDuplicatePropertiesAndAdditionalRootContent()
        {
            var json = JsonConvert.SerializeObject(LevelTestFixtures.Source());
            var duplicate = json.Insert(1, "\"number\":1,");
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadLevel(duplicate, 1));
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadLevel(json + "{}", 1));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void ReadRejectsUnsupportedSchemaVersion(int version)
        {
            var json = JsonConvert.SerializeObject(LevelTestFixtures.Source());
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadLevel(json, version));
            var asset = LevelTestFixtures.LoadCampaign().SourceCatalog;
            var catalog = JObject.Parse(asset.text);
            catalog["schemaVersion"] = version;
            Assert.Throws<InvalidDataException>(() => LevelDataReader.ReadCatalog(catalog.ToString()));
        }

        [Test]
        public void SeparateReadsHaveIndependentMutableData()
        {
            var json = JsonConvert.SerializeObject(LevelTestFixtures.Source());
            var first = LevelDataReader.ReadLevel(json, 1);
            var second = LevelDataReader.ReadLevel(json, 1);
            first.Bolts[0].OwnerId = 999;
            first.Parts[0].BoltIds[0] = 999;
            first.AllowedBoltPaletteIndices[0] = 0;
            Assert.That(second.Bolts[0].OwnerId, Is.EqualTo(0));
            Assert.That(second.Parts[0].BoltIds[0], Is.EqualTo(0));
            Assert.That(second.AllowedBoltPaletteIndices[0], Is.EqualTo(7));
        }

        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void GroupsAndAttachmentsAreIndependentAndValidationDoesNotMutateData(int colors)
        {
            var source = LevelTestFixtures.Source(colors);
            var gameplay = LevelTestFixtures.Gameplay(source);
            var before = JsonConvert.SerializeObject(new { source, gameplay });
            Assert.DoesNotThrow(() => LevelDataValidator.ValidateGameplay(source, gameplay));
            Assert.That(source.Parts.Any(part => part.BoltIds.Length == 1), Is.True);
            Assert.That(source.Parts.Any(part => part.BoltIds.Length == 11), Is.True);
            Assert.That(gameplay.ColorGroups[0].BoltIds.Select(id => source.Bolts.Single(bolt => bolt.Id == id).OwnerId).Distinct().Count(),
                Is.GreaterThan(1));
            Assert.That(source.Parts.Any(part => gameplay.ColorGroups.Count(group => group.BoltIds.Intersect(part.BoltIds).Any()) > 1), Is.True);
            Assert.That(JsonConvert.SerializeObject(new { source, gameplay }), Is.EqualTo(before));
        }

        [TestCase("duplicatePart")]
        [TestCase("duplicateBolt")]
        [TestCase("unknownOwner")]
        [TestCase("missingAttachment")]
        [TestCase("repeatedAttachment")]
        [TestCase("wrongOwner")]
        [TestCase("duplicateChildIndex")]
        [TestCase("badPath")]
        [TestCase("duplicateBoltPath")]
        [TestCase("shortPosition")]
        [TestCase("nonFiniteRotation")]
        [TestCase("zeroScale")]
        [TestCase("duplicatePalette")]
        [TestCase("outsidePalette")]
        [TestCase("tooFewColors")]
        [TestCase("wrongCampaignIndex")]
        public void SourceValidationRejectsBrokenStructure(string defect)
        {
            var source = LevelTestFixtures.Source();
            switch (defect)
            {
                case "duplicatePart": { source.Parts[1].Id = source.Parts[0].Id; break; }
                case "duplicateBolt": { source.Bolts[1].Id = source.Bolts[0].Id; break; }
                case "unknownOwner": { source.Bolts[0].OwnerId = 99; break; }
                case "missingAttachment": { source.Parts[1].BoltIds = source.Parts[1].BoltIds.Skip(1).ToArray(); break; }
                case "repeatedAttachment": { source.Parts[1].BoltIds[0] = source.Parts[1].BoltIds[1]; break; }
                case "wrongOwner": { source.Bolts[0].OwnerId = 1; break; }
                case "duplicateChildIndex": { source.Parts[1].ChildIndex = 0; break; }
                case "badPath": { source.Parts[0].NodePath = "Assembly/Model/../Other"; break; }
                case "duplicateBoltPath": { source.Bolts[1].NodePath = source.Bolts[0].NodePath; break; }
                case "shortPosition": { source.Bolts[0].Position = new double[2]; break; }
                case "nonFiniteRotation": { source.Bolts[0].EulerDegrees[0] = float.NaN; break; }
                case "zeroScale": { source.RootScale = 0; break; }
                case "duplicatePalette": { source.AllowedBoltPaletteIndices[1] = source.AllowedBoltPaletteIndices[0]; break; }
                case "outsidePalette": { source.AllowedBoltPaletteIndices[0] = 12; break; }
                case "tooFewColors": { source.AllowedBoltPaletteIndices = new[] { 1 }; break; }
                case "wrongCampaignIndex": { source.SourceCampaignIndex = 1; break; }
            }

            Assert.Throws<InvalidDataException>(() => LevelDataValidator.ValidateLevel(source));
        }

        [Test]
        public void CatalogValidationAcceptsOneLevelAndRejectsEmptyOrSkippedNumbers()
        {
            var json = LevelTestFixtures.LoadCampaign().SourceCatalog.text;
            var catalog = LevelDataReader.ReadCatalog(json);
            var first = catalog.Levels[0];
            catalog.Levels = new[] { first };
            catalog.PartCount = first.Parts.Length;
            catalog.BoltCount = first.Bolts.Length;
            Assert.DoesNotThrow(() => LevelDataValidator.ValidateCatalog(catalog));

            catalog.Levels[0].Number = 2;
            catalog.Levels[0].SourceCampaignIndex = 1;
            Assert.Throws<InvalidDataException>(() => LevelDataValidator.ValidateCatalog(catalog));

            catalog.Levels = Array.Empty<LevelSourceData>();
            catalog.PartCount = 0;
            catalog.BoltCount = 0;
            Assert.Throws<InvalidDataException>(() => LevelDataValidator.ValidateCatalog(catalog));
        }

        [TestCase("partCount")]
        [TestCase("boltCount")]
        [TestCase("duplicateLevel")]
        [TestCase("duplicatePalette")]
        [TestCase("defaultPalette")]
        [TestCase("nonFiniteColor")]
        public void CatalogValidationRejectsBrokenTotalsAndReferences(string defect)
        {
            var json = LevelTestFixtures.LoadCampaign().SourceCatalog.text;
            var source = LevelDataReader.ReadCatalog(json);
            switch (defect)
            {
                case "partCount": { source.PartCount++; break; }
                case "boltCount": { source.BoltCount++; break; }
                case "duplicateLevel": { source.Levels[1] = source.Levels[0]; break; }
                case "duplicatePalette": { source.BoltPalette[1].Index = source.BoltPalette[0].Index; break; }
                case "defaultPalette": { source.BoltDefaultPaletteIndex = 12; break; }
                case "nonFiniteColor": { source.BoltPalette[0].PrimarySrgb[0] = float.PositiveInfinity; break; }
            }

            Assert.Throws<InvalidDataException>(() => LevelDataValidator.ValidateCatalog(source));
        }

        [TestCase("sourceLevel")]
        [TestCase("missingGroup")]
        [TestCase("duplicateGroup")]
        [TestCase("unknownGroup")]
        [TestCase("emptyGroup")]
        [TestCase("nonMultipleOfThree")]
        [TestCase("repeatedBolt")]
        [TestCase("unknownBolt")]
        [TestCase("missingContainers")]
        [TestCase("unknownContainerGroup")]
        [TestCase("unbalancedCapacity")]
        public void GameplayValidationRejectsInvalidGroupsAndContainers(string defect)
        {
            var source = LevelTestFixtures.Source();
            var gameplay = LevelTestFixtures.Gameplay(source);
            switch (defect)
            {
                case "sourceLevel": { gameplay.LevelNumber = 2; break; }
                case "missingGroup": { gameplay.ColorGroups = gameplay.ColorGroups.Take(1).ToArray(); break; }
                case "duplicateGroup": { gameplay.ColorGroups[1].Id = 0; break; }
                case "unknownGroup": { gameplay.ColorGroups[1].Id = 2; break; }
                case "emptyGroup": { gameplay.ColorGroups[0].BoltIds = new int[0]; break; }
                case "nonMultipleOfThree": { gameplay.ColorGroups[0].BoltIds = gameplay.ColorGroups[0].BoltIds.Skip(1).ToArray(); break; }
                case "repeatedBolt": { gameplay.ColorGroups[1].BoltIds[0] = gameplay.ColorGroups[0].BoltIds[0]; break; }
                case "unknownBolt": { gameplay.ColorGroups[0].BoltIds[0] = 999; break; }
                case "missingContainers": { gameplay.ContainerGroups = gameplay.ContainerGroups.Skip(1).ToArray(); break; }
                case "unknownContainerGroup": { gameplay.ContainerGroups[0] = 999; break; }
                case "unbalancedCapacity": { gameplay.ContainerGroups[0] = 1; break; }
            }

            Assert.Throws<InvalidDataException>(() => LevelDataValidator.ValidateGameplay(source, gameplay));
        }
    }
}
