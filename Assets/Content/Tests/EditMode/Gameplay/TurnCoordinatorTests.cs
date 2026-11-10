using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Screw.Gameplay;
using Screw.Tests.Levels;

namespace Screw.Tests.Gameplay
{
    public sealed class TurnCoordinatorTests
    {
        private ControlledOperations _operations;
        private TurnCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            var source = LevelTestFixtures.Source();
            _operations = new ControlledOperations();
            _coordinator = new TurnCoordinator(new LevelAttempt(source, LevelTestFixtures.Gameplay(source)), _operations);
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator.Dispose();
        }

        [Test]
        public void PhasesAdvanceOnlyAfterTheirOwnOperationCompletes()
        {
            var task = _coordinator.PlayAsync(0).AsTask();
            var turn = _coordinator.Attempt.ActiveTurns.Values.SingleOrDefault();
            var waits = _operations.Turns.Single();
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.Reserved));
            Assert.That(_coordinator.Attempt.RemainingBoltsByPart[0], Is.EqualTo(1));
            Assert.That(waits.ArrivalRequested, Is.False);

            waits.Detach.TrySetResult();
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.InTransit));
            Assert.That(_coordinator.Attempt.RemainingBoltsByPart[0], Is.Zero);
            Assert.That(turn.ReleasedPartId, Is.EqualTo(0));
            Assert.That(waits.ArrivalRequested, Is.True);
            Assert.That(waits.FinishRequested, Is.False);

            waits.Arrive.TrySetResult();
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.AwaitingCompletion));
            Assert.That(waits.FinishRequested, Is.True);
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(_coordinator.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.SameAs(turn));

            waits.Finish.TrySetResult();
            Assert.That(task.IsCompleted, Is.False);
            waits.Removal.TrySetResult();
            Assert.That(Result(task).Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.Completed));
            Assert.That(_coordinator.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
            Assert.That(waits.Tokens, Has.Count.EqualTo(4));
            Assert.That(waits.Tokens.All(token => token == waits.Tokens[0] && token.CanBeCanceled), Is.True);
            AttemptTestSteps.AssertConservation(_coordinator.Attempt);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void NewBoltsStartImmediatelyInEveryPhaseWhileDuplicateInputIsRejected(int phase)
        {
            var task = _coordinator.PlayAsync(0).AsTask();
            var waits = AdvanceTo(phase);
            var next = _coordinator.PlayAsync(1).AsTask();
            var nextWaits = _operations.Turns.Last();
            Assert.That(_operations.Turns, Has.Count.EqualTo(2));
            Assert.That(nextWaits.Turn.Phase, Is.EqualTo(TurnPhase.Reserved));
            Assert.That(Result(_coordinator.PlayAsync(0).AsTask()).Outcome, Is.EqualTo(
                phase >= 2 ? BoltReservationOutcome.AlreadyRemoved : BoltReservationOutcome.AlreadyReserved));
            Finish(waits);
            Result(task);
            Finish(nextWaits);
            Result(next);
            Assert.That(_operations.Turns, Has.Count.EqualTo(2));
            Assert.That(_coordinator.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
            CollectionAssert.AreEqual(new[] { 0, 1 }, _coordinator.Attempt.DetachedBoltIds);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RestartCancelsPromptlyAndLateCompletionsCannotChangeNewAttempt(int phase)
        {
            var oldTask = _coordinator.PlayAsync(0).AsTask();
            var oldWaits = AdvanceTo(phase);
            var oldAttempt = _coordinator.Attempt;
            _coordinator.Restart();
            Assert.That(oldTask.IsCompleted, Is.True, "The operation deliberately ignores cancellation.");
            AssertCanceled(oldTask);
            Assert.That(oldWaits.Tokens.All(token => token.IsCancellationRequested), Is.True);
            Assert.That(_coordinator.Attempt, Is.Not.SameAs(oldAttempt));

            var newTask = _coordinator.PlayAsync(1).AsTask();
            var current = _coordinator.Attempt.ActiveTurns.Values.SingleOrDefault();
            var newWaits = _operations.Turns.Last();
            Finish(oldWaits);
            Assert.That(_coordinator.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.SameAs(current));
            Assert.That(current.Phase, Is.EqualTo(TurnPhase.Reserved));
            Assert.That(newTask.IsCompleted, Is.False);
            Assert.That(_coordinator.Attempt.DetachedBoltIds, Is.Empty);
            Finish(newWaits);
            Result(newTask);
            CollectionAssert.AreEqual(new[] { 1 }, _coordinator.Attempt.DetachedBoltIds);
            AttemptTestSteps.AssertConservation(_coordinator.Attempt);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void DisposalCancelsAllPhasesAndRejectsFurtherWork(int phase)
        {
            var task = _coordinator.PlayAsync(0).AsTask();
            var waits = AdvanceTo(phase);
            var turn = _coordinator.Attempt.ActiveTurns.Values.SingleOrDefault();
            var savedPhase = turn.Phase;
            _coordinator.Dispose();
            AssertCanceled(task);
            Finish(waits);
            Assert.That(turn.Phase, Is.EqualTo(savedPhase));
            Assert.Throws<ObjectDisposedException>(() => _coordinator.Restart());
            Assert.Throws<ObjectDisposedException>(() => Result(_coordinator.PlayAsync(1).AsTask()));
            _coordinator.Dispose();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void OperationErrorsPropagateWithoutGloballyRejectingOtherBolts(int phase)
        {
            var task = _coordinator.PlayAsync(0).AsTask();
            var waits = AdvanceTo(phase);
            var error = new InvalidOperationException("Controlled operation failure.");
            var pending = phase == 0 ? waits.Detach : phase == 1 ? waits.Arrive : phase == 2 ? waits.Finish : waits.Removal;
            pending.TrySetException(error);
            var observed = Assert.Throws<InvalidOperationException>(() => Result(task));
            Assert.That(observed, Is.SameAs(error));
            var next = _coordinator.PlayAsync(1).AsTask();
            Assert.That(_operations.Turns, Has.Count.EqualTo(2));
            Finish(_operations.Turns.Last());
            Result(next);
            _coordinator.Restart();
            Assert.That(_coordinator.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
            Assert.That(_coordinator.Attempt.DetachedBoltIds, Is.Empty);
        }

        [Test]
        public void SeveralRestartsDoNotAllowOldCompletionsIntoTheLatestAttempt()
        {
            var first = _coordinator.PlayAsync(0).AsTask();
            var firstWaits = _operations.Turns.Last();
            _coordinator.Restart();
            var second = _coordinator.PlayAsync(1).AsTask();
            var secondWaits = _operations.Turns.Last();
            _coordinator.Restart();
            AssertCanceled(first);
            AssertCanceled(second);
            Finish(secondWaits);
            Finish(firstWaits);
            Assert.That(_coordinator.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
            Assert.That(_coordinator.Attempt.DetachedBoltIds, Is.Empty);
            Assert.That(_coordinator.Attempt.RemainingBoltsByPart[0], Is.EqualTo(1));
        }

        [Test]
        public void InvalidBoltDoesNotStartAnyOperation()
        {
            var result = Result(_coordinator.PlayAsync(999).AsTask());
            Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.UnknownBolt));
            Assert.That(_operations.Turns, Is.Empty);
            Assert.That(_coordinator.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BothOperationsFinishTheirTurnWithoutBlockingAnotherDetachment(bool removalFirst)
        {
            var task = _coordinator.PlayAsync(0).AsTask();
            var waits = AdvanceTo(2);
            Assert.That(waits.RemovalRequested && waits.FinishRequested, Is.True);
            if (removalFirst)
            {
                waits.Removal.TrySetResult();
            }
            else
            {
                waits.Finish.TrySetResult();
            }

            Assert.That(task.IsCompleted, Is.False);
            var next = _coordinator.PlayAsync(1).AsTask();
            var nextWaits = _operations.Turns.Last();
            Assert.That(_operations.Turns, Has.Count.EqualTo(2));
            Assert.That(nextWaits.Turn.Phase, Is.EqualTo(TurnPhase.Reserved));

            Finish(waits);
            Result(task);
            Assert.That(_coordinator.Attempt.ActiveTurns.Keys, Is.EquivalentTo(new[] { 1 }));
            Finish(nextWaits);
            Result(next);
        }

        [Test]
        public void TurnWithoutReleasedPartDoesNotWaitForRemoval()
        {
            var task = _coordinator.PlayAsync(1).AsTask();
            var waits = AdvanceTo(2);
            Assert.That(waits.Turn.ReleasedPartId, Is.Null);
            Assert.That(waits.RemovalRequested, Is.False);
            waits.Finish.TrySetResult();
            Result(task);
            Assert.That(_coordinator.Attempt.ActiveTurns.Values.SingleOrDefault(), Is.Null);
        }

        [Test]
        public void VictoryWaitsForFinalPartRemovalAfterAllSortingCompletes()
        {
            var count = _coordinator.Attempt.Bolts.Count;
            for (var id = 0; id < count - 1; id++)
            {
                var task = _coordinator.PlayAsync(id).AsTask();
                Finish(_operations.Turns.Last());
                Result(task);
            }

            var final = _coordinator.PlayAsync(count - 1).AsTask();
            var waits = AdvanceTo(3);
            Assert.That(waits.Turn.ReleasedPartId.HasValue, Is.True);
            Assert.That(_coordinator.Attempt.CollectedBoltIds.Count, Is.EqualTo(count));
            Assert.That(_coordinator.Attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(final.IsCompleted, Is.False);
            waits.Removal.TrySetResult();
            Result(final);
            Assert.That(_coordinator.Attempt.Status, Is.EqualTo(AttemptStatus.Won));
        }

        [Test]
        public void ConcurrentFlightsKeepReservationsUntilArrivalAndWaitForContainerReplacement()
        {
            var tasks = Enumerable.Range(0, 3).Select(id => _coordinator.PlayAsync(id).AsTask()).ToArray();
            var waits = _operations.Turns.ToArray();
            foreach (var item in waits)
            {
                item.Detach.TrySetResult();
            }

            Assert.That(waits.All(item => item.ArrivalRequested), Is.True, "Flights must overlap.");
            waits[2].Arrive.TrySetResult();
            Assert.That(_coordinator.Attempt.CollectedBoltIds, Is.Empty);
            waits[1].Arrive.TrySetResult();
            Assert.That(waits[1].FinishRequested, Is.False);
            waits[2].Finish.TrySetResult();
            Assert.That(waits[1].FinishRequested, Is.True);
            waits[1].Finish.TrySetResult();
            waits[0].Arrive.TrySetResult();
            Assert.That(_coordinator.Attempt.CollectedBoltIds, Is.EquivalentTo(new[] { 0, 1, 2 }));
            var next = _coordinator.PlayAsync(6).AsTask();
            var nextWaits = _operations.Turns.Last();
            nextWaits.Detach.TrySetResult();
            Assert.That(nextWaits.Turn.Phase, Is.EqualTo(TurnPhase.InTransit));
            Assert.That(nextWaits.ArrivalRequested, Is.False, "The new container must finish replacing before receiving flights.");
            waits[0].Finish.TrySetResult();
            Assert.That(nextWaits.ArrivalRequested, Is.True);
            Finish(nextWaits);
            Result(next);
            foreach (var item in waits)
            {
                Finish(item);
            }

            foreach (var task in tasks)
            {
                Result(task);
            }

            AttemptTestSteps.AssertConservation(_coordinator.Attempt);
        }

        [Test]
        public void RestartCancelsConcurrentWaitersAndOldCompletionsCannotOpenANewSortingGate()
        {
            var first = _coordinator.PlayAsync(0).AsTask();
            var firstWaits = AdvanceTo(2);
            var second = _coordinator.PlayAsync(1).AsTask();
            var secondWaits = AdvanceTo(1);
            Assert.That(secondWaits.ArrivalRequested, Is.False);
            _coordinator.Restart();
            AssertCanceled(first);
            AssertCanceled(second);
            var current = _coordinator.PlayAsync(2).AsTask();
            var currentWaits = AdvanceTo(2);
            Finish(firstWaits);
            Finish(secondWaits);
            var next = _coordinator.PlayAsync(3).AsTask();
            var nextWaits = AdvanceTo(1);
            Assert.That(nextWaits.ArrivalRequested, Is.False);
            Finish(currentWaits);
            Result(current);
            Assert.That(nextWaits.ArrivalRequested, Is.True);
            Finish(nextWaits);
            Result(next);
            Assert.That(_coordinator.Attempt.DetachedBoltIds, Is.EquivalentTo(new[] { 2, 3 }));
        }

        [Test]
        public void OverflowCancelsAllInflightOperationsAndLateCompletionsCannotUndoLoss()
        {
            _coordinator.Dispose();
            var source = LevelTestFixtures.Source(5);
            _coordinator = new TurnCoordinator(new LevelAttempt(source, LevelTestFixtures.Gameplay(source)), _operations);
            var ids = _coordinator.Attempt.Bolts.Values.Where(bolt => bolt.ColorGroupId == 2).Select(bolt => bolt.Id).ToArray();
            var tasks = ids.Take(5).Select(id => _coordinator.PlayAsync(id).AsTask()).ToArray();
            Assert.That(_coordinator.Attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(Result(_coordinator.PlayAsync(ids.Last()).AsTask()).Outcome, Is.EqualTo(BoltReservationOutcome.PoolOverflow));
            foreach (var task in tasks)
            {
                AssertCanceled(task);
            }

            foreach (var item in _operations.Turns)
            {
                Finish(item);
            }

            Assert.That(_coordinator.Attempt.Status, Is.EqualTo(AttemptStatus.Lost));
            Assert.That(_coordinator.Attempt.DetachedBoltIds, Is.Empty);
            Assert.That(Result(_coordinator.PlayAsync(0).AsTask()).Outcome, Is.EqualTo(BoltReservationOutcome.AttemptInactive));
            Assert.That(_operations.Turns, Has.Count.EqualTo(5));
        }

        [Test]
        public void VictoryAlsoWaitsForAnEarlierReleasedPartAfterAllOtherBoltsComplete()
        {
            var first = _coordinator.PlayAsync(0).AsTask();
            var waits = AdvanceTo(3);
            for (var id = 1; id < _coordinator.Attempt.Bolts.Count; id++)
            {
                var task = _coordinator.PlayAsync(id).AsTask();
                Finish(_operations.Turns.Last());
                Result(task);
            }

            Assert.That(_coordinator.Attempt.IsVictoryReady, Is.True);
            Assert.That(_coordinator.Attempt.Status, Is.EqualTo(AttemptStatus.Active));
            Assert.That(_coordinator.Attempt.ActiveTurns.Keys, Is.EquivalentTo(new[] { 0 }));
            waits.Removal.TrySetResult();
            Result(first);
            Assert.That(_coordinator.Attempt.Status, Is.EqualTo(AttemptStatus.Won));
        }

        private ControlledTurn AdvanceTo(int phase)
        {
            var waits = _operations.Turns.Last();
            if (phase >= 1)
            {
                waits.Detach.TrySetResult();
            }

            if (phase >= 2)
            {
                waits.Arrive.TrySetResult();
            }

            if (phase >= 3)
            {
                waits.Finish.TrySetResult();
            }

            return waits;
        }

        private static void Finish(ControlledTurn waits)
        {
            waits.Detach.TrySetResult();
            waits.Arrive.TrySetResult();
            waits.Finish.TrySetResult();
            waits.Removal.TrySetResult();
        }

        private static T Result<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True, "The controlled operation must already be complete.");
            return task.GetAwaiter().GetResult();
        }

        private static void AssertCanceled(Task task)
        {
            Assert.That(task.IsCompleted, Is.True, "Cancellation must not wait for the operation callback.");
            Assert.That(() => task.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
        }

        private sealed class ControlledTurn
        {
            public AttemptTurn Turn;
            public readonly UniTaskCompletionSource Detach = new UniTaskCompletionSource();
            public readonly UniTaskCompletionSource Arrive = new UniTaskCompletionSource();
            public readonly UniTaskCompletionSource Finish = new UniTaskCompletionSource();
            public readonly UniTaskCompletionSource Removal = new UniTaskCompletionSource();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public bool ArrivalRequested;
            public bool FinishRequested;
            public bool RemovalRequested;
        }

        private sealed class ControlledOperations : ITurnOperations
        {
            public readonly List<ControlledTurn> Turns = new List<ControlledTurn>();

            public UniTask DetachAsync(AttemptTurn turn, CancellationToken cancellationToken)
            {
                var waits = new ControlledTurn { Turn = turn };
                waits.Tokens.Add(cancellationToken);
                Turns.Add(waits);
                return waits.Detach.Task;
            }

            public UniTask ReleasePartAsync(AttemptTurn turn, CancellationToken cancellationToken)
            {
                var waits = Turns.Single(item => ReferenceEquals(item.Turn, turn));
                waits.Tokens.Add(cancellationToken);
                waits.RemovalRequested = true;
                return waits.Removal.Task;
            }

            public UniTask ArriveAsync(AttemptTurn turn, CancellationToken cancellationToken)
            {
                var waits = Turns.Single(item => ReferenceEquals(item.Turn, turn));
                waits.Tokens.Add(cancellationToken);
                waits.ArrivalRequested = true;
                return waits.Arrive.Task;
            }

            public UniTask FinishAsync(AttemptTurn turn, CancellationToken cancellationToken)
            {
                var waits = Turns.Single(item => ReferenceEquals(item.Turn, turn));
                waits.Tokens.Add(cancellationToken);
                waits.FinishRequested = true;
                return waits.Finish.Task;
            }
        }
    }
}
