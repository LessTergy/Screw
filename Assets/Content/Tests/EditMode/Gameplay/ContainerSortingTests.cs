using System.Linq;
using NUnit.Framework;
using Screw.Gameplay;
using Screw.Tests.Levels;

namespace Screw.Tests.Gameplay
{
    public sealed class ContainerSortingTests
    {
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void FullRouteExhaustsQueueAndRemovesBothContainers(int colors)
        {
            var source = LevelTestFixtures.Source(colors);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            foreach (var bolt in source.Bolts)
            {
                AttemptTestSteps.Play(attempt, bolt.Id);
            }

            CollectionAssert.AreEquivalent(source.Bolts.Select(bolt => bolt.Id), attempt.CollectedBoltIds);
            Assert.That(attempt.CollectedBoltIds.Distinct().Count(), Is.EqualTo(source.Bolts.Length));
            Assert.That(attempt.ContainerQueue, Is.Empty);
            Assert.That(attempt.Containers.All(container => !container.IsActive && container.ReservedCount == 0), Is.True);
            Assert.That(attempt.PoolCount, Is.Zero);
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Won));
        }

        [Test]
        public void ReplacementSkipsBlockingPoolColorAndChainsUntilStable()
        {
            var source = LevelTestFixtures.Source(5);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            foreach (var id in new[] { 9, 6, 10, 7, 8, 0, 1 })
            {
                AttemptTestSteps.Play(attempt, id);
            }

            var steps = AttemptTestSteps.Play(attempt, 2).SortingSteps;
            CollectionAssert.AreEqual(new[] { typeof(ContainerChange), typeof(PoolTransfer), typeof(PoolTransfer),
                typeof(PoolTransfer), typeof(ContainerChange), typeof(PoolTransfer), typeof(PoolTransfer) },
                steps.Select(step => step.GetType()));
            CollectionAssert.AreEqual(new[] { 6, 7, 8, 9, 10 }, steps.OfType<PoolTransfer>().Select(step => step.BoltId));
            CollectionAssert.AreEqual(new[] { 2, 3 }, steps.OfType<ContainerChange>().Select(step => step.NextColorGroupId));
            Assert.That(steps.All(step => step.ContainerIndex == 0), Is.True);
            Assert.That(attempt.PoolCount, Is.Zero);
            Assert.That(attempt.Containers[0].ColorGroupId, Is.EqualTo(3));
            Assert.That(attempt.Containers[0].ReservedCount, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { 4, 0, 1, 2, 3, 4 }, attempt.ContainerQueue);
        }

        [Test]
        public void ReusedPoolHoleDoesNotChangeArrivalOrder()
        {
            var source = LevelTestFixtures.Source(5);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            foreach (var id in new[] { 9, 6, 10, 0, 1, 2 })
            {
                AttemptTestSteps.Play(attempt, id);
            }

            AttemptTestSteps.Play(attempt, 11);
            Assert.That(attempt.PoolSlots[1], Is.EqualTo(11));
            foreach (var id in new[] { 3, 4 })
            {
                AttemptTestSteps.Play(attempt, id);
            }

            var transfers = AttemptTestSteps.Play(attempt, 5).SortingSteps.OfType<PoolTransfer>().ToArray();
            CollectionAssert.AreEqual(new[] { 9, 10, 11 }, transfers.Select(step => step.BoltId));
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, transfers.Select(step => step.PoolSlotIndex));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, transfers.Select(step => step.Destination.SlotIndex));
        }

        [Test]
        public void TwoMatchingContainersUseLeftThenRightAndConsumeSharedQueue()
        {
            var source = LevelTestFixtures.Source();
            var gameplay = LevelTestFixtures.Gameplay(source);
            gameplay.ContainerGroups = new[] { 0, 0, 1, 1 };
            var attempt = new LevelAttempt(source, gameplay);
            var changes = new[] { 0, 1, 2, 6, 7, 8 }.Select(id => AttemptTestSteps.Play(attempt, id))
                .SelectMany(turn => turn.SortingSteps).OfType<ContainerChange>().ToArray();
            CollectionAssert.AreEqual(new[] { 0, 1 }, changes.Select(step => step.ContainerIndex));
            Assert.That(changes.All(step => step.NextColorGroupId == 1), Is.True);
            Assert.That(attempt.ContainerQueue, Is.Empty);
        }

        [Test]
        public void ReservationDoesNotCompleteContainerBeforeArrival()
        {
            var source = LevelTestFixtures.Source();
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            AttemptTestSteps.Play(attempt, 0);
            AttemptTestSteps.Play(attempt, 1);
            var result = attempt.ReserveBolt(2);
            Assert.That(attempt.CollectedBoltIds, Is.Empty);
            Assert.That(attempt.ConfirmArrival(result.Turn), Is.False);
            Assert.That(attempt.ConfirmDetachment(result.Turn), Is.True);
            Assert.That(attempt.CollectedBoltIds, Is.Empty);
            Assert.That(attempt.Containers[0].ReservedCount, Is.EqualTo(3));
            Assert.That(attempt.ConfirmArrival(result.Turn), Is.True);
            Assert.That(result.Turn.SortingSteps.OfType<ContainerChange>().Count(), Is.EqualTo(1));
            Assert.That(attempt.ConfirmArrival(result.Turn), Is.False);
        }
    }
}
