using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Screw.Gameplay;
using Screw.Tests.Levels;

namespace Screw.Tests.Gameplay
{
    public sealed class AttemptRegressionTests
    {
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void FullPoolHasLosingAndWinningContinuations(int colors)
        {
            var source = LevelTestFixtures.Source(colors);
            var gameplay = LevelTestFixtures.Gameplay(source);
            if (colors == 2)
            {
                gameplay.ContainerGroups = new[] { 0, 0, 1, 1 };
            }

            var original = JsonConvert.SerializeObject(new { source, gameplay });
            var blockedGroup = colors == 2 ? 1 : 2;
            var blocked = gameplay.ColorGroups[blockedGroup].BoltIds;
            var matching = gameplay.ColorGroups[0].BoltIds;
            var losing = new LevelAttempt(source, gameplay);
            var winning = new LevelAttempt(source, gameplay);
            var prelude = blocked.Take(5).Concat(matching.Take(2)).ToArray();
            foreach (var attempt in new[] { losing, winning })
            {
                foreach (var id in prelude)
                {
                    AttemptTestSteps.Play(attempt, id);
                }

                Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
                Assert.That(attempt.PoolCount, Is.EqualTo(5));
                Assert.That(attempt.Containers[0].ReservedCount, Is.EqualTo(2));
                CollectionAssert.AreEqual(blocked.Take(5).Select(id => (int?)id), attempt.PoolSlots);
            }

            Assert.That(JsonConvert.SerializeObject(losing), Is.EqualTo(JsonConvert.SerializeObject(winning)));
            var blockedId = blocked[5];
            var attachments = losing.RemainingBoltsByPart.ToDictionary(item => item.Key, item => item.Value);
            var rejected = losing.ReserveBolt(blockedId);
            Assert.That(rejected.Outcome, Is.EqualTo(BoltReservationOutcome.PoolOverflow));
            Assert.That(rejected.Turn, Is.Null);
            Assert.That(rejected.Destination, Is.Null);
            Assert.That(losing.Status, Is.EqualTo(AttemptStatus.Lost));
            Assert.That(losing.DetachedBoltIds.Contains(blockedId), Is.False);
            CollectionAssert.AreEquivalent(attachments, losing.RemainingBoltsByPart);
            AttemptTestSteps.AssertConservation(losing);

            var rescue = AttemptTestSteps.Play(winning, matching[2]);
            Assert.That(rescue.Destination.ContainerIndex, Is.EqualTo(0));
            Assert.That(winning.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(winning.PoolCount, Is.EqualTo(colors == 2 ? 0 : 2));
            CollectionAssert.AreEqual(blocked.Take(colors == 2 ? 5 : 3),
                rescue.SortingSteps.OfType<PoolTransfer>().Select(step => step.BoltId));
            Assert.That(rescue.SortingSteps.OfType<ContainerChange>().Count(), Is.EqualTo(2));

            var accepted = AttemptTestSteps.Play(winning, blockedId);
            Assert.That(accepted.Destination.IsPool, Is.EqualTo(colors != 2));
            foreach (var bolt in source.Bolts.Where(bolt => !winning.DetachedBoltIds.Contains(bolt.Id)))
            {
                AttemptTestSteps.Play(winning, bolt.Id);
            }

            AssertVictory(winning);
            Assert.That(losing.Status, Is.EqualTo(AttemptStatus.Lost));
            Assert.That(losing.PoolCount, Is.EqualTo(5));
            Assert.That(JsonConvert.SerializeObject(new { source, gameplay }), Is.EqualTo(original));
        }

        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void LastWaitingPoolBoltMustReachCompletedContainerBeforeVictory(int colors)
        {
            var source = LevelTestFixtures.Source(colors);
            var gameplay = LevelTestFixtures.Gameplay(source);
            gameplay.ContainerGroups = Enumerable.Range(0, colors).SelectMany(group => new[] { group, group }).ToArray();
            var attempt = new LevelAttempt(source, gameplay);
            var waitingId = gameplay.ColorGroups[1].BoltIds[0];
            var waiting = AttemptTestSteps.Play(attempt, waitingId);
            Assert.That(waiting.Destination.IsPool, Is.True);
            Assert.That(attempt.PoolCount, Is.EqualTo(1));
            Assert.That(attempt.PoolSlots[0], Is.EqualTo(waitingId));
            Assert.That(attempt.IsVictoryReady, Is.False);
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));

            AttemptTestSteps.Play(attempt, gameplay.ColorGroups[0].BoltIds[0]);
            AttemptTestSteps.Play(attempt, gameplay.ColorGroups[0].BoltIds[1]);
            var replacement = AttemptTestSteps.Play(attempt, gameplay.ColorGroups[0].BoltIds[2]);
            var transfer = replacement.SortingSteps.OfType<PoolTransfer>().Single();
            Assert.That(transfer.BoltId, Is.EqualTo(waitingId));
            Assert.That(transfer.PoolSlotIndex, Is.Zero);
            Assert.That(transfer.ContainerIndex, Is.Zero);
            Assert.That(attempt.PoolCount, Is.Zero);
            Assert.That(attempt.Containers[0].Slots[0], Is.EqualTo(waitingId));
            Assert.That(attempt.CollectedBoltIds.Contains(waitingId), Is.False);
            Assert.That(attempt.IsVictoryReady, Is.False);
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));

            foreach (var id in gameplay.ColorGroups[1].BoltIds.Skip(1))
            {
                AttemptTestSteps.Play(attempt, id);
            }

            Assert.That(attempt.CollectedBoltIds.Count(id => id == waitingId), Is.EqualTo(1));
            Assert.That(attempt.IsVictoryReady, Is.False);
            foreach (var group in gameplay.ColorGroups.Where(group => group.Id != 1))
            {
                foreach (var id in group.BoltIds.Where(id => !attempt.DetachedBoltIds.Contains(id)))
                {
                    AttemptTestSteps.Play(attempt, id);
                }
            }

            AssertVictory(attempt);
        }

        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void UnequalColorPopulationsPreserveExactCapacityThroughWholeRoute(int colors)
        {
            var source = LevelTestFixtures.Source(colors);
            var gameplay = LevelTestFixtures.Gameplay(source);
            var lastGroup = gameplay.ColorGroups[colors - 1];
            gameplay.ColorGroups[0].BoltIds = gameplay.ColorGroups[0].BoltIds.Concat(lastGroup.BoltIds.Skip(3)).ToArray();
            lastGroup.BoltIds = lastGroup.BoltIds.Take(3).ToArray();
            gameplay.ContainerGroups[gameplay.ContainerGroups.Length - 1] = 0;
            var original = JsonConvert.SerializeObject(new { source, gameplay });
            var attempt = new LevelAttempt(source, gameplay);
            Assert.That(attempt.Bolts.Values.Count(bolt => bolt.ColorGroupId == 0), Is.EqualTo(9));
            Assert.That(attempt.Bolts.Values.Count(bolt => bolt.ColorGroupId == colors - 1), Is.EqualTo(3));

            foreach (var bolt in source.Bolts)
            {
                AttemptTestSteps.Play(attempt, bolt.Id);
            }

            foreach (var group in gameplay.ColorGroups)
            {
                CollectionAssert.AreEquivalent(group.BoltIds,
                    attempt.CollectedBoltIds.Where(id => attempt.Bolts[id].ColorGroupId == group.Id));
            }

            AssertVictory(attempt);
            Assert.That(JsonConvert.SerializeObject(new { source, gameplay }), Is.EqualTo(original));
        }

        private static void AssertVictory(LevelAttempt attempt)
        {
            AttemptTestSteps.AssertConservation(attempt);
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Won));
            Assert.That(attempt.IsVictoryReady, Is.True);
            Assert.That(attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
            Assert.That(attempt.Reservations, Is.Empty);
            Assert.That(attempt.PoolCount, Is.Zero);
            Assert.That(attempt.ContainerQueue, Is.Empty);
            Assert.That(attempt.Containers.All(container => !container.IsActive && container.ReservedCount == 0), Is.True);
            Assert.That(attempt.RemainingBoltsByPart.Values.All(count => count == 0), Is.True);
            CollectionAssert.AreEquivalent(attempt.Bolts.Keys, attempt.CollectedBoltIds);
        }
    }
}
