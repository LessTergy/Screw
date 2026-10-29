using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Screw.Gameplay;
using Screw.Tests.Levels;

namespace Screw.Tests.Gameplay
{
    public sealed class TurnLifecycleTests
    {
        [Test]
        public void SingleBoltOwnerReleasesAtDetachmentEvenWhenDestinationIsPool()
        {
            var source = LevelTestFixtures.Source(5);
            var gameplay = LevelTestFixtures.Gameplay(source);
            gameplay.ContainerGroups[0] = 2;
            gameplay.ContainerGroups[2] = 0;
            var attempt = new LevelAttempt(source, gameplay);
            var turn = attempt.ReserveBolt(0).Turn;

            Assert.That(turn.Destination.IsPool, Is.True);
            Assert.That(attempt.RemainingBoltsByPart[0], Is.EqualTo(1));
            Assert.That(attempt.ConfirmDetachment(turn), Is.True);
            Assert.That(turn.ReleasedPartId, Is.EqualTo(0));
            Assert.That(attempt.RemainingBoltsByPart[0], Is.Zero);
            Assert.That(attempt.RemainingBoltsByPart[1], Is.EqualTo(11));
            Assert.That(attempt.ConfirmDetachment(turn), Is.False);
            Assert.That(attempt.CancelReservation(turn), Is.False);
            Assert.That(attempt.ConfirmArrival(turn), Is.True);
            Assert.That(attempt.PoolSlots[0], Is.EqualTo(0));
            Assert.That(attempt.CompleteTurn(turn), Is.True);
            AttemptTestSteps.AssertConservation(attempt);
        }

        [Test]
        public void ElevenBoltOwnerReleasesOnlyWhenItsOwnLastBoltExits()
        {
            var source = LevelTestFixtures.Source();
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var outside = AttemptTestSteps.Play(attempt, 0);
            Assert.That(outside.ReleasedPartId, Is.EqualTo(0));
            Assert.That(attempt.RemainingBoltsByPart[1], Is.EqualTo(11));

            foreach (var id in Enumerable.Range(1, 10))
            {
                var turn = AttemptTestSteps.Play(attempt, id);
                Assert.That(turn.ReleasedPartId, Is.Null);
                Assert.That(attempt.RemainingBoltsByPart[1], Is.EqualTo(11 - id));
            }

            var last = attempt.ReserveBolt(11).Turn;
            Assert.That(attempt.RemainingBoltsByPart[1], Is.EqualTo(1));
            Assert.That(attempt.ConfirmDetachment(last), Is.True);
            Assert.That(last.ReleasedPartId, Is.EqualTo(1));
            Assert.That(attempt.RemainingBoltsByPart[1], Is.Zero);
            Assert.That(attempt.IsVictoryReady, Is.False);
        }

        [Test]
        public void SameColorAcrossOwnersAndDifferentColorsOnOneOwnerRemainIndependent()
        {
            var source = LevelTestFixtures.Source();
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            Assert.That(attempt.Bolts[0].ColorGroupId, Is.EqualTo(attempt.Bolts[1].ColorGroupId));
            Assert.That(attempt.Bolts[1].ColorGroupId, Is.Not.EqualTo(attempt.Bolts[3].ColorGroupId));
            Assert.That(attempt.Bolts[1].OwnerId, Is.EqualTo(attempt.Bolts[3].OwnerId));
            AttemptTestSteps.Play(attempt, 1);
            AttemptTestSteps.Play(attempt, 3);
            Assert.That(attempt.RemainingBoltsByPart[0], Is.EqualTo(1));
            Assert.That(attempt.RemainingBoltsByPart[1], Is.EqualTo(9));
            AttemptTestSteps.Play(attempt, 0);
            Assert.That(attempt.RemainingBoltsByPart[0], Is.Zero);
            Assert.That(attempt.RemainingBoltsByPart[1], Is.EqualTo(9));
        }

        [Test]
        public void OutOfOrderRepeatedAndForeignCommandsDoNotMutateState()
        {
            var source = LevelTestFixtures.Source();
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var other = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var foreign = other.ReserveBolt(0).Turn;
            var turn = attempt.ReserveBolt(0).Turn;
            foreach (var phase in new[] { TurnPhase.Reserved, TurnPhase.InTransit, TurnPhase.AwaitingCompletion })
            {
                Assert.That(turn.Phase, Is.EqualTo(phase));
                var before = JsonConvert.SerializeObject(attempt);
                Assert.That(attempt.ReserveBolt(1).Outcome, Is.EqualTo(BoltReservationOutcome.TurnInProgress));
                Assert.That(attempt.ConfirmDetachment(foreign), Is.False);
                Assert.That(attempt.ConfirmArrival(foreign), Is.False);
                Assert.That(attempt.CompleteTurn(foreign), Is.False);
                Assert.That(attempt.CancelReservation(foreign), Is.False);
                Assert.That(attempt.ConfirmDetachment(null), Is.False);
                Assert.That(attempt.ConfirmArrival(null), Is.False);
                Assert.That(attempt.CompleteTurn(null), Is.False);
                if (phase == TurnPhase.Reserved)
                {
                    Assert.That(attempt.ConfirmArrival(turn), Is.False);
                    Assert.That(attempt.CompleteTurn(turn), Is.False);
                }
                else
                {
                    Assert.That(attempt.ConfirmDetachment(turn), Is.False);
                    Assert.That(attempt.CancelReservation(turn), Is.False);
                    if (phase == TurnPhase.InTransit)
                    {
                        Assert.That(attempt.CompleteTurn(turn), Is.False);
                    }
                    else
                    {
                        Assert.That(attempt.ConfirmArrival(turn), Is.False);
                    }
                }

                Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(before));
                if (phase == TurnPhase.Reserved)
                {
                    Assert.That(attempt.ConfirmDetachment(turn), Is.True);
                }
                else if (phase == TurnPhase.InTransit)
                {
                    Assert.That(attempt.ConfirmArrival(turn), Is.True);
                }
            }

            Assert.That(attempt.CompleteTurn(turn), Is.True);
            var completed = JsonConvert.SerializeObject(attempt);
            Assert.That(attempt.CompleteTurn(turn), Is.False);
            Assert.That(attempt.ConfirmDetachment(turn), Is.False);
            Assert.That(attempt.ConfirmArrival(turn), Is.False);
            Assert.That(attempt.CancelReservation(turn), Is.False);
            Assert.That(attempt.ReserveBolt(0).Outcome, Is.EqualTo(BoltReservationOutcome.AlreadyRemoved));
            Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(completed));
            Assert.That(attempt.ReserveBolt(1).Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
        }

        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void LogicalVictoryWaitsForFinalVisualCompletion(int colors)
        {
            var source = LevelTestFixtures.Source(colors);
            var gameplay = LevelTestFixtures.Gameplay(source);
            var originalData = JsonConvert.SerializeObject(new { source, gameplay });
            var attempt = new LevelAttempt(source, gameplay);
            foreach (var bolt in source.Bolts.Take(source.Bolts.Length - 1))
            {
                AttemptTestSteps.Play(attempt, bolt.Id);
                Assert.That(attempt.IsVictoryReady, Is.False);
                Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
            }

            var last = AttemptTestSteps.Arrive(attempt, source.Bolts.Last().Id);
            Assert.That(attempt.IsVictoryReady, Is.True);
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(attempt.CurrentTurn, Is.SameAs(last));
            Assert.That(last.Phase, Is.EqualTo(TurnPhase.AwaitingCompletion));
            Assert.That(attempt.RemainingBoltsByPart.Values.All(count => count == 0), Is.True);
            Assert.That(attempt.PoolCount, Is.Zero);
            Assert.That(attempt.Containers.All(container => !container.IsActive), Is.True);
            Assert.That(attempt.CompleteTurn(last), Is.True);
            Assert.That(attempt.Status, Is.EqualTo(AttemptStatus.Won));
            AssertTerminalCommands(attempt, last);
            Assert.That(JsonConvert.SerializeObject(new { source, gameplay }), Is.EqualTo(originalData));
            Assert.Throws<NotSupportedException>(() => ((IList<int>)attempt.DetachedBoltIds).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<int>)attempt.CollectedBoltIds).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<SortingStep>)last.SortingSteps).Clear());
            var change = last.SortingSteps.OfType<ContainerChange>().Single();
            Assert.Throws<NotSupportedException>(() => ((IList<int>)change.CollectedBoltIds)[0] = -1);
        }

        [TestCase(TurnPhase.Reserved)]
        [TestCase(TurnPhase.InTransit)]
        [TestCase(TurnPhase.AwaitingCompletion)]
        [TestCase(TurnPhase.Completed)]
        public void RestartRestoresSnapshotAndRejectsOldTurnEvenForSameBolt(TurnPhase phase)
        {
            var source = LevelTestFixtures.Source(5);
            var gameplay = LevelTestFixtures.Gameplay(source);
            var attempt = new LevelAttempt(source, gameplay);
            var initial = JsonConvert.SerializeObject(attempt);
            var old = attempt.ReserveBolt(0).Turn;
            if (phase != TurnPhase.Reserved)
            {
                attempt.ConfirmDetachment(old);
            }

            if (phase == TurnPhase.AwaitingCompletion || phase == TurnPhase.Completed)
            {
                attempt.ConfirmArrival(old);
            }

            if (phase == TurnPhase.Completed)
            {
                attempt.CompleteTurn(old);
            }

            source.Bolts[0].OwnerId = 999;
            source.Parts[0].BoltIds[0] = 999;
            source.Number = 2;
            source.AllowedBoltPaletteIndices[0] = 11;
            gameplay.ContainerGroups[0] = 4;
            gameplay.ColorGroups[0].BoltIds[0] = 999;
            var restarted = attempt.Restart();
            Assert.That(JsonConvert.SerializeObject(restarted), Is.EqualTo(initial));
            Assert.That(restarted.LevelNumber, Is.EqualTo(1));
            Assert.That(restarted.PoolSlots, Is.Not.SameAs(attempt.PoolSlots));
            Assert.That(restarted.RemainingBoltsByPart, Is.Not.SameAs(attempt.RemainingBoltsByPart));
            Assert.That(restarted.Containers[0], Is.Not.SameAs(attempt.Containers[0]));
            var current = restarted.ReserveBolt(0).Turn;
            var before = JsonConvert.SerializeObject(restarted);
            Assert.That(restarted.CancelReservation(old), Is.False);
            Assert.That(restarted.ConfirmDetachment(old), Is.False);
            Assert.That(restarted.ConfirmArrival(old), Is.False);
            Assert.That(restarted.CompleteTurn(old), Is.False);
            attempt.ConfirmDetachment(old);
            attempt.ConfirmArrival(old);
            attempt.CompleteTurn(old);
            Assert.That(JsonConvert.SerializeObject(restarted), Is.EqualTo(before));
            Assert.That(restarted.CancelReservation(current), Is.True);
            Assert.That(JsonConvert.SerializeObject(restarted), Is.EqualTo(initial));
        }

        [Test]
        public void CancellationCannotBeReplayedAgainstNewTurnForSameBolt()
        {
            var source = LevelTestFixtures.Source();
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var canceled = attempt.ReserveBolt(0).Turn;
            Assert.That(attempt.CancelReservation(canceled), Is.True);
            var current = attempt.ReserveBolt(0).Turn;
            var before = JsonConvert.SerializeObject(attempt);
            Assert.That(attempt.CancelReservation(canceled), Is.False);
            Assert.That(attempt.ConfirmDetachment(canceled), Is.False);
            Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(before));
            Assert.That(attempt.ConfirmDetachment(current), Is.True);
            Assert.That(attempt.RemainingBoltsByPart[0], Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestartFromResultRestoresExactInitialState(bool win)
        {
            var source = LevelTestFixtures.Source(5);
            var attempt = new LevelAttempt(source, LevelTestFixtures.Gameplay(source));
            var initial = JsonConvert.SerializeObject(attempt);
            AttemptTurn last = null;
            if (win)
            {
                foreach (var bolt in source.Bolts)
                {
                    last = AttemptTestSteps.Play(attempt, bolt.Id);
                }
            }
            else
            {
                var ids = attempt.Bolts.Values.Where(bolt => bolt.ColorGroupId == 2).Select(bolt => bolt.Id).ToArray();
                foreach (var id in ids.Take(5))
                {
                    last = AttemptTestSteps.Play(attempt, id);
                }

                Assert.That(attempt.ReserveBolt(ids[5]).Outcome, Is.EqualTo(BoltReservationOutcome.PoolOverflow));
            }

            Assert.That(attempt.Status, Is.EqualTo(win ? AttemptStatus.Won : AttemptStatus.Lost));
            AssertTerminalCommands(attempt, last);
            Assert.That(JsonConvert.SerializeObject(attempt.Restart()), Is.EqualTo(initial));
        }

        private static void AssertTerminalCommands(LevelAttempt attempt, AttemptTurn turn)
        {
            var before = JsonConvert.SerializeObject(attempt);
            foreach (var id in attempt.Bolts.Keys.Concat(new[] { -1 }))
            {
                var result = attempt.ReserveBolt(id);
                Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.AttemptInactive));
                Assert.That(result.Turn, Is.Null);
                Assert.That(result.Destination, Is.Null);
            }

            Assert.That(attempt.ConfirmDetachment(turn), Is.False);
            Assert.That(attempt.ConfirmArrival(turn), Is.False);
            Assert.That(attempt.CompleteTurn(turn), Is.False);
            Assert.That(attempt.CancelReservation(turn), Is.False);
            Assert.That(JsonConvert.SerializeObject(attempt), Is.EqualTo(before));
        }
    }
}
