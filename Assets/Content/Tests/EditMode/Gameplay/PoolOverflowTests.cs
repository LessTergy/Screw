using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Screw.Gameplay;
using Screw.Tests.Levels;

namespace Screw.Tests.Gameplay
{
    public sealed class PoolOverflowTests
    {
        [Test]
        public void FifthUnmatchedBoltDoesNotLoseAttempt()
        {
            var attempt = AttemptWithFullPool();
            Assert.That(attempt.PoolCount, Is.EqualTo(5));
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(attempt.Reservations, Is.Empty);
            Assert.That(attempt.DetachedBoltIds.Count, Is.EqualTo(5));
            Assert.That(attempt.Containers.All(container => container.ReservedCount == 0), Is.True);
        }

        [Test]
        public void SixthUnmatchedBoltLosesWithoutReservationOrAttachmentChange()
        {
            var attempt = AttemptWithFullPool();
            var bolt = attempt.Bolts.Values.Single(item => item.ColorGroupId == 2 && !attempt.DetachedBoltIds.Contains(item.Id));
            var before = Storage(attempt);
            var ownerCount = attempt.RemainingBoltsByPart[bolt.OwnerId];

            var result = attempt.ReserveBolt(bolt.Id);

            Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.PoolOverflow));
            Assert.That(result.Destination.HasValue, Is.False);
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Lost));
            Assert.That(attempt.Reservations.ContainsKey(bolt.Id), Is.False);
            Assert.That(attempt.PoolSlots.Contains(bolt.Id), Is.False);
            Assert.That(attempt.PoolCount, Is.EqualTo(5));
            Assert.That(attempt.RemainingBoltsByPart[bolt.OwnerId], Is.EqualTo(ownerCount));
            Assert.That(Storage(attempt), Is.EqualTo(before));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void MatchingBoltIsAcceptedEvenWhenPoolIsFull(int group)
        {
            var attempt = AttemptWithFullPool();
            var pool = attempt.PoolSlots.ToArray();
            var bolt = attempt.Bolts.Values.First(item => item.ColorGroupId == group);
            var result = attempt.ReserveBolt(bolt.Id);
            Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
            Assert.That(result.Destination.Value.ContainerIndex, Is.EqualTo(group));
            Assert.That(result.Destination.Value.SlotIndex, Is.Zero);
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(attempt.PoolCount, Is.EqualTo(5));
            CollectionAssert.AreEqual(pool, attempt.PoolSlots);
            Assert.That(attempt.Reservations.Count, Is.EqualTo(1));
        }

        [Test]
        public void UnknownAndPreviouslyRemovedBoltsAtFullPoolDoNotCauseLoss()
        {
            var attempt = AttemptWithFullPool();
            var before = JsonConvert.SerializeObject(attempt);
            var reserved = attempt.PoolSlots[0].Value;
            Assert.That(attempt.ReserveBolt(-1).Outcome, Is.EqualTo(BoltReservationOutcome.UnknownBolt));
            Assert.That(attempt.ReserveBolt(reserved).Outcome, Is.EqualTo(BoltReservationOutcome.AlreadyRemoved));
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(before));
        }

        [Test]
        public void LostAttemptRejectsFurtherReservationsAndCancellation()
        {
            var attempt = AttemptWithFullPool();
            var overflowBolt = attempt.Bolts.Values.Single(bolt => bolt.ColorGroupId == 2 && !attempt.DetachedBoltIds.Contains(bolt.Id)).Id;
            attempt.ReserveBolt(overflowBolt);
            var before = JsonConvert.SerializeObject(attempt);
            var matchingBolt = attempt.Bolts.Values.First(bolt => bolt.ColorGroupId == 0).Id;

            foreach (var id in new[] { matchingBolt, overflowBolt, -1, attempt.PoolSlots[0].Value })
            {
                var result = attempt.ReserveBolt(id);
                Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.AttemptInactive));
                Assert.That(result.Destination.HasValue, Is.False);
            }

            Assert.That(attempt.CancelReservation(null), Is.False);
            Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(before));
        }

        [Test]
        public void ReusingCanceledReservationDoesNotOverflow()
        {
            var source = LevelTestFixtures.Source(5);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var bolts = attempt.Bolts.Values.Where(item => item.ColorGroupId == 2).OrderBy(item => item.Id).ToArray();
            foreach (var item in bolts.Take(4))
            {
                AttemptTestSteps.Play(attempt, item.Id);
            }

            var pending = attempt.ReserveBolt(bolts[4].Id);
            Assert.That(attempt.PoolCount, Is.EqualTo(5));
            Assert.That(attempt.CancelReservation(pending.Turn), Is.True);
            Assert.That(attempt.PoolCount, Is.EqualTo(4));
            var result = attempt.ReserveBolt(bolts[5].Id);
            Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
            Assert.That(result.Destination.Value.IsPool, Is.True);
            Assert.That(result.Destination.Value.SlotIndex, Is.EqualTo(4));
            Assert.That(attempt.PoolCount, Is.EqualTo(5));
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
        }

        [Test]
        public void LossDoesNotAffectOtherAttemptOrSourceConfiguration()
        {
            var source = LevelTestFixtures.Source(5);
            var gameplay = LevelTestFixtures.Gameplay(source);
            var before = JsonConvert.SerializeObject(new { source, gameplay });
            var first = new LevelAttempt(source, gameplay);
            var second = new LevelAttempt(source, gameplay);
            var unmatched = first.Bolts.Values.Where(bolt => bolt.ColorGroupId == 2).OrderBy(bolt => bolt.Id).ToArray();
            foreach (var bolt in unmatched.Take(5))
            {
                AttemptTestSteps.Play(first, bolt.Id);
            }

            first.ReserveBolt(unmatched[5].Id);
            Assert.That(first.Status, Is.EqualTo(AttemptStatus.Lost));
            Assert.That(second.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(second.PoolCount, Is.Zero);
            Assert.That(second.ReserveBolt(unmatched[0].Id).Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
            Assert.That(second.PoolCount, Is.EqualTo(1));
            Assert.That(JsonConvert.SerializeObject(new { source, gameplay }), Is.EqualTo(before));
        }

        private static LevelAttempt AttemptWithFullPool()
        {
            var source = LevelTestFixtures.Source(5);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            foreach (var bolt in attempt.Bolts.Values.Where(item => item.ColorGroupId == 2).OrderBy(item => item.Id).Take(5))
            {
                var result = attempt.ReserveBolt(bolt.Id);
                Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
                Assert.That(result.Destination.Value.IsPool, Is.True);
                Assert.That(attempt.ConfirmDetachment(result.Turn), Is.True);
                Assert.That(attempt.ConfirmArrival(result.Turn), Is.True);
                Assert.That(attempt.CompleteTurn(result.Turn), Is.True);
            }

            return attempt;
        }

        private static string Storage(LevelAttempt attempt)
        {
            return JsonConvert.SerializeObject(new
            {
                attempt.Bolts,
                attempt.RemainingBoltsByPart,
                attempt.Reservations,
                attempt.PaletteIndices,
                attempt.ContainerQueue,
                attempt.Containers,
                attempt.PoolSlots,
                attempt.PoolCount,
                attempt.DetachedBoltIds,
                attempt.CollectedBoltIds,
                attempt.CurrentTurn
            });
        }
    }
}
