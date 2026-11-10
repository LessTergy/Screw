using System.Linq;
using NUnit.Framework;
using Screw.Gameplay;

namespace Screw.Tests.Gameplay
{
    internal static class AttemptTestSteps
    {
        public static AttemptTurn Arrive(LevelAttempt attempt, int boltId)
        {
            var result = attempt.ReserveBolt(boltId);
            Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.Reserved), "Bolt " + boltId);
            Assert.That(attempt.ConfirmDetachment(result.Turn), Is.True);
            Assert.That(attempt.ConfirmArrival(result.Turn), Is.True);
            AssertConservation(attempt);
            return result.Turn;
        }

        public static AttemptTurn Play(LevelAttempt attempt, int boltId)
        {
            var turn = Arrive(attempt, boltId);
            Assert.That(attempt.CompleteTurn(turn), Is.True);
            return turn;
        }

        public static void AssertConservation(LevelAttempt attempt)
        {
            Assert.That(attempt.PoolSlots.Count, Is.EqualTo(5));
            Assert.That(attempt.PoolCount, Is.InRange(0, 5));
            Assert.That(attempt.Containers.Count, Is.EqualTo(2));
            var stored = attempt.PoolSlots.Concat(attempt.Containers.SelectMany(container => container.Slots))
                .Where(id => id.HasValue).Select(id => id.Value).Concat(attempt.CollectedBoltIds).ToArray();
            CollectionAssert.AreEquivalent(attempt.DetachedBoltIds.Union(attempt.Reservations.Keys), stored);
            Assert.That(stored.Distinct().Count(), Is.EqualTo(stored.Length));
            foreach (var part in attempt.RemainingBoltsByPart)
            {
                Assert.That(part.Value, Is.EqualTo(attempt.Bolts.Values.Count(bolt => bolt.OwnerId == part.Key
                    && !attempt.DetachedBoltIds.Contains(bolt.Id))));
            }

            foreach (var container in attempt.Containers)
            {
                Assert.That(container.Slots.Count, Is.EqualTo(3));
                Assert.That(container.ReservedCount, Is.InRange(0, 3));
                if (!container.IsActive)
                {
                    Assert.That(container.ReservedCount, Is.Zero);
                }

                foreach (var id in container.Slots.Where(id => id.HasValue))
                {
                    Assert.That(attempt.Bolts[id.Value].ColorGroupId, Is.EqualTo(container.ColorGroupId));
                }

                if (container.IsActive && container.ReservedCount < 3)
                {
                    Assert.That(attempt.PoolSlots.Where(id => id.HasValue && !attempt.Reservations.ContainsKey(id.Value))
                        .Any(id => attempt.Bolts[id.Value].ColorGroupId == container.ColorGroupId), Is.False);
                }
            }

            foreach (var group in attempt.Bolts.Values.GroupBy(bolt => bolt.ColorGroupId))
            {
                var collected = attempt.CollectedBoltIds.Count(id => attempt.Bolts[id].ColorGroupId == group.Key);
                var remainingContainers = attempt.Containers.Count(container => container.IsActive && container.ColorGroupId == group.Key)
                    + attempt.ContainerQueue.Count(id => id == group.Key);
                Assert.That(collected % 3, Is.Zero);
                Assert.That(collected + remainingContainers * 3, Is.EqualTo(group.Count()), "Capacity for group " + group.Key);
            }
        }
    }
}
