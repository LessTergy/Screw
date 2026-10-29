using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Screw.Gameplay;
using Screw.Tests.Levels;

namespace Screw.Tests.Gameplay
{
    public sealed class LevelAttemptTests
    {
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void InitialStateMatchesValidatedConfiguration(int colors)
        {
            var source = LevelTestFixtures.Source(colors);
            var gameplay = LevelTestFixtures.Gameplay(source);
            Array.Reverse(gameplay.ColorGroups);
            var configuration = JsonConvert.SerializeObject(new { source, gameplay });
            var attempt = new LevelAttempt(source, gameplay);

            Assert.That(attempt.LevelNumber, Is.EqualTo(source.Number));
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(attempt.Bolts.Count, Is.EqualTo(source.Bolts.Length));
            Assert.That(attempt.Reservations, Is.Empty);
            Assert.That(attempt.CurrentTurn, Is.Null);
            Assert.That(attempt.DetachedBoltIds, Is.Empty);
            Assert.That(attempt.CollectedBoltIds, Is.Empty);
            Assert.That(attempt.IsVictoryReady, Is.False);
            Assert.That(attempt.PoolSlots.Count, Is.EqualTo(5));
            Assert.That(attempt.PoolSlots.All(id => !id.HasValue), Is.True);
            Assert.That(attempt.PoolCount, Is.Zero);
            CollectionAssert.AreEqual(source.AllowedBoltPaletteIndices, attempt.PaletteIndices);
            CollectionAssert.AreEqual(gameplay.ContainerGroups.Skip(2), attempt.ContainerQueue);

            for (var i = 0; i < 2; i++)
            {
                Assert.That(attempt.Containers[i].ColorGroupId, Is.EqualTo(gameplay.ContainerGroups[i]));
                Assert.That(attempt.Containers[i].IsActive, Is.True);
                Assert.That(attempt.Containers[i].PaletteIndex, Is.EqualTo(source.AllowedBoltPaletteIndices[gameplay.ContainerGroups[i]]));
                Assert.That(attempt.Containers[i].Slots.Count, Is.EqualTo(3));
                Assert.That(attempt.Containers[i].Slots.All(id => !id.HasValue), Is.True);
                Assert.That(attempt.Containers[i].ReservedCount, Is.Zero);
            }

            foreach (var bolt in source.Bolts)
            {
                var state = attempt.Bolts[bolt.Id];
                var group = gameplay.ColorGroups.Single(color => color.BoltIds.Contains(bolt.Id));
                Assert.That(state.Id, Is.EqualTo(bolt.Id));
                Assert.That(state.OwnerId, Is.EqualTo(bolt.OwnerId));
                Assert.That(state.ColorGroupId, Is.EqualTo(group.Id));
                Assert.That(state.PaletteIndex, Is.EqualTo(source.AllowedBoltPaletteIndices[group.Id]));
            }

            foreach (var part in source.Parts)
            {
                Assert.That(attempt.RemainingBoltsByPart[part.Id], Is.EqualTo(part.BoltIds.Length));
            }

            Assert.That(JsonConvert.SerializeObject(new { source, gameplay }), Is.EqualTo(configuration));
        }

        [Test]
        public void InputMutationDoesNotChangeCreatedAttempt()
        {
            var source = LevelTestFixtures.Source(5);
            var gameplay = LevelTestFixtures.Gameplay(source);
            var attempt = new LevelAttempt(source, gameplay);
            var owner = attempt.Bolts[0].OwnerId;
            var group = attempt.Bolts[0].ColorGroupId;
            var palette = attempt.PaletteIndices.ToArray();
            var queue = attempt.ContainerQueue.ToArray();
            var remaining = attempt.RemainingBoltsByPart.ToDictionary(item => item.Key, item => item.Value);

            source.Number = 2;
            source.Bolts[0].OwnerId = 999;
            source.Parts[0].BoltIds[0] = 999;
            source.AllowedBoltPaletteIndices[0] = 11;
            gameplay.LevelNumber = 2;
            gameplay.ColorGroups[0].Id = 999;
            gameplay.ColorGroups[0].BoltIds[0] = 999;
            gameplay.ContainerGroups[0] = 999;
            gameplay.ContainerGroups[2] = 999;

            Assert.That(attempt.LevelNumber, Is.EqualTo(1));
            Assert.That(attempt.Bolts[0].OwnerId, Is.EqualTo(owner));
            Assert.That(attempt.Bolts[0].ColorGroupId, Is.EqualTo(group));
            CollectionAssert.AreEqual(palette, attempt.PaletteIndices);
            CollectionAssert.AreEqual(queue, attempt.ContainerQueue);
            CollectionAssert.AreEquivalent(remaining, attempt.RemainingBoltsByPart);
            Assert.That(attempt.Containers[0].ColorGroupId, Is.Zero);
        }

        [Test]
        public void AttemptsAndLevelsDoNotShareMutableCollections()
        {
            var source = LevelTestFixtures.Source();
            var gameplay = LevelTestFixtures.Gameplay(source);
            var first = new LevelAttempt(source, gameplay);
            var second = new LevelAttempt(source, gameplay);
            source.Number = 2;
            source.SourceCampaignIndex = 1;
            gameplay.LevelNumber = 2;
            var otherLevel = new LevelAttempt(source, gameplay);

            foreach (var other in new[] { second, otherLevel })
            {
                Assert.AreNotSame(first.Bolts, other.Bolts);
                Assert.AreNotSame(first.RemainingBoltsByPart, other.RemainingBoltsByPart);
                Assert.AreNotSame(first.Reservations, other.Reservations);
                Assert.AreNotSame(first.PaletteIndices, other.PaletteIndices);
                Assert.AreNotSame(first.ContainerQueue, other.ContainerQueue);
                Assert.AreNotSame(first.PoolSlots, other.PoolSlots);
                Assert.AreNotSame(first.Containers[0], other.Containers[0]);
                Assert.AreNotSame(first.Containers[0].Slots, other.Containers[0].Slots);
                Assert.AreNotSame(first.DetachedBoltIds, other.DetachedBoltIds);
                Assert.AreNotSame(first.CollectedBoltIds, other.CollectedBoltIds);
            }

            Assert.That(first.LevelNumber, Is.EqualTo(1));
            Assert.That(otherLevel.LevelNumber, Is.EqualTo(2));
        }

        [Test]
        public void PublicCollectionsCannotMutateAttempt()
        {
            var source = LevelTestFixtures.Source();
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            Assert.Throws<NotSupportedException>(() => ((IList<int>)attempt.PaletteIndices)[0] = 0);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)attempt.ContainerQueue)[0] = 0);
            Assert.Throws<NotSupportedException>(() => ((IList<int?>)attempt.PoolSlots)[0] = 0);
            Assert.Throws<NotSupportedException>(() => ((IList<int?>)attempt.Containers[0].Slots)[0] = 0);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<int, int>)attempt.RemainingBoltsByPart)[0] = 0);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<int, AttemptBolt>)attempt.Bolts).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<int>)attempt.DetachedBoltIds).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<int>)attempt.CollectedBoltIds).Clear());
        }

        [Test]
        public void InvalidConfigurationIsRejected()
        {
            var source = LevelTestFixtures.Source();
            var gameplay = LevelTestFixtures.Gameplay(source);
            gameplay.ColorGroups[0].BoltIds[0] = 999;
            Assert.Throws<InvalidDataException>(() => new LevelAttempt(source, gameplay));
        }
    }
}
