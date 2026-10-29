using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Screw.Gameplay;
using Screw.Tests.Levels;

namespace Screw.Tests.Gameplay
{
    public sealed class BoltReservationTests
    {
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void MatchingColorReservesItsContainer(int colors)
        {
            var source = LevelTestFixtures.Source(colors);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            for (var group = 0; group < 2; group++)
            {
                var bolt = attempt.Bolts.Values.First(item => item.ColorGroupId == group);
                var result = attempt.ReserveBolt(bolt.Id);
                Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
                Assert.That(result.Destination.HasValue, Is.True);
                Assert.That(result.Destination.Value.ContainerIndex, Is.EqualTo(group));
                Assert.That(result.Destination.Value.SlotIndex, Is.Zero);
                Assert.That(attempt.Containers[group].Slots[0], Is.EqualTo(bolt.Id));
                Assert.That(attempt.Containers[group].PaletteIndex, Is.EqualTo(bolt.PaletteIndex));
                Assert.That(attempt.CancelReservation(result.Turn), Is.True);
            }

            Assert.That(attempt.PoolCount, Is.Zero);
            Assert.That(attempt.Reservations, Is.Empty);
        }

        [Test]
        public void TwoMatchingContainersPreferLeftThenRight()
        {
            var source = LevelTestFixtures.Source();
            var gameplay = LevelTestFixtures.Gameplay(source);
            gameplay.ContainerGroups = new[] { 0, 0, 1, 1 };
            var attempt = new LevelAttempt(source, gameplay);
            var bolts = attempt.Bolts.Values.Where(bolt => bolt.ColorGroupId == 0).OrderBy(bolt => bolt.Id).ToArray();

            for (var i = 0; i < bolts.Length; i++)
            {
                var result = attempt.ReserveBolt(bolts[i].Id);
                Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
                Assert.That(result.Destination.Value.ContainerIndex, Is.EqualTo(i / 3));
                Assert.That(result.Destination.Value.SlotIndex, Is.EqualTo(i % 3));
                Assert.That(attempt.ConfirmDetachment(result.Turn), Is.True);
                Assert.That(attempt.ConfirmArrival(result.Turn), Is.True);
                Assert.That(attempt.CompleteTurn(result.Turn), Is.True);
            }

            Assert.That(attempt.Containers.All(container => container.ColorGroupId == 1), Is.True);
            Assert.That(attempt.PoolCount, Is.Zero);
        }

        [Test]
        public void FullContainerBlocksFurtherInputUntilArrivalAndCompletion()
        {
            var source = LevelTestFixtures.Source();
            var gameplay = LevelTestFixtures.Gameplay(source);
            var attempt = new LevelAttempt(source, gameplay);
            var bolts = attempt.Bolts.Values.Where(bolt => bolt.ColorGroupId == 0).OrderBy(bolt => bolt.Id).ToArray();
            foreach (var bolt in bolts.Take(2))
            {
                AttemptTestSteps.Play(attempt, bolt.Id);
            }

            var pending = attempt.ReserveBolt(bolts[2].Id);
            var result = attempt.ReserveBolt(bolts[3].Id);
            Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.TurnInProgress));
            Assert.That(result.Turn, Is.Null);
            Assert.That(attempt.Containers[0].ReservedCount, Is.EqualTo(3));
            CollectionAssert.AreEqual(gameplay.ContainerGroups.Skip(2), attempt.ContainerQueue);
            Assert.That(attempt.CancelReservation(pending.Turn), Is.True);
        }

        [Test]
        public void GroupIdentityIsNotConfusedWithPaletteIndex()
        {
            var source = LevelTestFixtures.Source(10);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var bolt = attempt.Bolts.Values.First(item => item.ColorGroupId == 7);
            Assert.That(attempt.Containers[0].PaletteIndex, Is.EqualTo(bolt.ColorGroupId));
            Assert.That(attempt.Containers[0].PaletteIndex, Is.Not.EqualTo(bolt.PaletteIndex));
            var result = attempt.ReserveBolt(bolt.Id);
            Assert.That(result.Destination.Value.IsPool, Is.True);
            Assert.That(attempt.Containers.All(container => container.ReservedCount == 0), Is.True);
        }

        [Test]
        public void UnknownAndRepeatedBoltDoNotChangeState()
        {
            var source = LevelTestFixtures.Source(5);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var before = JsonConvert.SerializeObject(attempt);
            var unknown = attempt.ReserveBolt(-1);
            Assert.That(unknown.Outcome, Is.EqualTo(BoltReservationOutcome.UnknownBolt));
            Assert.That(unknown.Destination.HasValue, Is.False);
            Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(before));

            attempt.ReserveBolt(0);
            before = JsonConvert.SerializeObject(attempt);
            var repeated = attempt.ReserveBolt(0);
            Assert.That(repeated.Outcome, Is.EqualTo(BoltReservationOutcome.AlreadyReserved));
            Assert.That(repeated.Destination.HasValue, Is.False);
            Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(before));
        }

        [Test]
        public void CanceledPoolReservationReusesFirstVacancyWithoutReorderingOthers()
        {
            var source = LevelTestFixtures.Source(5);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var bolts = attempt.Bolts.Values.Where(bolt => bolt.ColorGroupId == 2).OrderBy(bolt => bolt.Id).ToArray();
            AttemptTestSteps.Play(attempt, bolts[0].Id);
            var pending = attempt.ReserveBolt(bolts[1].Id);
            Assert.That(attempt.CancelReservation(pending.Turn), Is.True);
            Assert.That(attempt.CancelReservation(pending.Turn), Is.False);
            Assert.That(attempt.CancelReservation(null), Is.False);
            Assert.That(attempt.Reservations.ContainsKey(bolts[1].Id), Is.False);
            Assert.That(attempt.PoolCount, Is.EqualTo(1));
            Assert.That(attempt.PoolSlots[1], Is.Null);

            var replacement = attempt.Bolts.Values.First(bolt => bolt.ColorGroupId == 3);
            var result = attempt.ReserveBolt(replacement.Id);
            Assert.That(result.Destination.Value.IsPool, Is.True);
            Assert.That(result.Destination.Value.SlotIndex, Is.EqualTo(1));
            CollectionAssert.AreEqual(new int?[] { bolts[0].Id, replacement.Id, null, null, null }, attempt.PoolSlots);
            Assert.That(attempt.PoolCount, Is.EqualTo(2));
            Assert.That(attempt.ConfirmDetachment(result.Turn), Is.True);
            Assert.That(attempt.ConfirmArrival(result.Turn), Is.True);
            Assert.That(attempt.CompleteTurn(result.Turn), Is.True);

            var canceledBolt = attempt.ReserveBolt(bolts[1].Id);
            Assert.That(canceledBolt.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
            Assert.That(canceledBolt.Destination.Value.SlotIndex, Is.EqualTo(2));
        }

        [Test]
        public void CanceledContainerReservationReusesVacancy()
        {
            var source = LevelTestFixtures.Source();
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var bolts = attempt.Bolts.Values.Where(bolt => bolt.ColorGroupId == 0).OrderBy(bolt => bolt.Id).ToArray();
            AttemptTestSteps.Play(attempt, bolts[0].Id);
            var pending = attempt.ReserveBolt(bolts[1].Id);
            Assert.That(attempt.CancelReservation(pending.Turn), Is.True);
            var result = attempt.ReserveBolt(bolts[3].Id);
            Assert.That(result.Destination.Value.ContainerIndex, Is.EqualTo(0));
            Assert.That(result.Destination.Value.SlotIndex, Is.EqualTo(1));
            CollectionAssert.AreEqual(new int?[] { bolts[0].Id, bolts[3].Id, null }, attempt.Containers[0].Slots);
        }

        [Test]
        public void ReservationsDoNotDetachBoltsOrMutateConfiguration()
        {
            var source = LevelTestFixtures.Source(5);
            var gameplay = LevelTestFixtures.Gameplay(source);
            var before = JsonConvert.SerializeObject(new { source, gameplay });
            var attempt = new LevelAttempt(source, gameplay);
            var remaining = attempt.RemainingBoltsByPart.ToDictionary(item => item.Key, item => item.Value);

            var first = attempt.ReserveBolt(0);
            Assert.That(attempt.CancelReservation(first.Turn), Is.True);
            var other = attempt.Bolts.Values.First(bolt => bolt.ColorGroupId == 2).Id;
            var next = attempt.ReserveBolt(other);
            Assert.That(attempt.CancelReservation(next.Turn), Is.True);
            attempt.ReserveBolt(0);

            CollectionAssert.AreEquivalent(remaining, attempt.RemainingBoltsByPart);
            Assert.That(JsonConvert.SerializeObject(new { source, gameplay }), Is.EqualTo(before));
            Assert.That(attempt.Reservations.Count, Is.EqualTo(1));
            Assert.Throws<System.NotSupportedException>(() => ((IDictionary<int, BoltDestination>)attempt.Reservations).Clear());
        }

        [Test]
        public void MutatingOneAttemptDoesNotAffectAnotherAttemptOrLevel()
        {
            var source = LevelTestFixtures.Source(5);
            var gameplay = LevelTestFixtures.Gameplay(source);
            var first = new LevelAttempt(source, gameplay);
            var second = new LevelAttempt(source, gameplay);
            source.Number = 2;
            source.SourceCampaignIndex = 1;
            gameplay.LevelNumber = 2;
            var otherLevel = new LevelAttempt(source, gameplay);
            var expectedSecond = JsonConvert.SerializeObject(second);
            var expectedOtherLevel = JsonConvert.SerializeObject(otherLevel);

            first.ReserveBolt(0);
            first.ReserveBolt(first.Bolts.Values.First(bolt => bolt.ColorGroupId == 2).Id);

            Assert.That(first.Reservations.Count, Is.EqualTo(1));
            Assert.That(JsonConvert.SerializeObject(second), Is.EqualTo(expectedSecond));
            Assert.That(JsonConvert.SerializeObject(otherLevel), Is.EqualTo(expectedOtherLevel));
        }

        [Test]
        public void RoutingUsesExplicitIdsRatherThanArrayPositions()
        {
            var source = LevelTestFixtures.Source();
            var gameplay = LevelTestFixtures.Gameplay(source);
            source.Bolts[0].Id = 1000;
            source.Bolts[0].OwnerId = 500;
            source.Parts[0].Id = 500;
            source.Parts[0].BoltIds[0] = 1000;
            gameplay.ColorGroups[0].BoltIds[0] = 1000;
            var attempt = new LevelAttempt(source, gameplay);

            Assert.That(attempt.ReserveBolt(1000).Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
            Assert.That(attempt.ReserveBolt(0).Outcome, Is.EqualTo(BoltReservationOutcome.UnknownBolt));
            Assert.That(attempt.Bolts[1000].OwnerId, Is.EqualTo(500));
            Assert.That(attempt.RemainingBoltsByPart[500], Is.EqualTo(1));
        }
    }
}
