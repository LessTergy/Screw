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
            var turn = _coordinator.Attempt.CurrentTurn;
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
            Assert.That(_coordinator.Attempt.CurrentTurn, Is.SameAs(turn));

            waits.Finish.TrySetResult();
            Assert.That(Result(task).Outcome, Is.EqualTo(BoltReservationOutcome.Reserved));
            Assert.That(turn.Phase, Is.EqualTo(TurnPhase.Completed));
            Assert.That(_coordinator.Attempt.CurrentTurn, Is.Null);
            Assert.That(waits.Tokens, Has.Count.EqualTo(3));
            Assert.That(waits.Tokens.All(token => token == waits.Tokens[0] && token.CanBeCanceled), Is.True);
            AttemptTestSteps.AssertConservation(_coordinator.Attempt);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void RepeatedInputIsRejectedInEveryPhaseAndNeverQueued(int phase)
        {
            var task = _coordinator.PlayAsync(0).AsTask();
            var waits = AdvanceTo(phase);
            Assert.That(Result(_coordinator.PlayAsync(1).AsTask()).Outcome, Is.EqualTo(BoltReservationOutcome.TurnInProgress));
            Assert.That(Result(_coordinator.PlayAsync(0).AsTask()).Outcome, Is.EqualTo(
                phase == 2 ? BoltReservationOutcome.AlreadyRemoved : BoltReservationOutcome.AlreadyReserved));
            Finish(waits);
            Result(task);
            Assert.That(_operations.Turns, Has.Count.EqualTo(1));
            Assert.That(_coordinator.Attempt.CurrentTurn, Is.Null);
            CollectionAssert.AreEqual(new[] { 0 }, _coordinator.Attempt.DetachedBoltIds);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
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
            var current = _coordinator.Attempt.CurrentTurn;
            var newWaits = _operations.Turns.Last();
            Finish(oldWaits);
            Assert.That(_coordinator.Attempt.CurrentTurn, Is.SameAs(current));
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
        public void DisposalCancelsAllPhasesAndRejectsFurtherWork(int phase)
        {
            var task = _coordinator.PlayAsync(0).AsTask();
            var waits = AdvanceTo(phase);
            var turn = _coordinator.Attempt.CurrentTurn;
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
        public void OperationErrorsPropagateAndKeepTurnBlockedUntilRestart(int phase)
        {
            var task = _coordinator.PlayAsync(0).AsTask();
            var waits = AdvanceTo(phase);
            var error = new InvalidOperationException("Controlled operation failure.");
            var pending = phase == 0 ? waits.Detach : phase == 1 ? waits.Arrive : waits.Finish;
            pending.TrySetException(error);
            var observed = Assert.Throws<InvalidOperationException>(() => Result(task));
            Assert.That(observed, Is.SameAs(error));
            Assert.That(Result(_coordinator.PlayAsync(1).AsTask()).Outcome, Is.EqualTo(BoltReservationOutcome.TurnInProgress));
            _coordinator.Restart();
            Assert.That(_coordinator.Attempt.CurrentTurn, Is.Null);
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
            Assert.That(_coordinator.Attempt.CurrentTurn, Is.Null);
            Assert.That(_coordinator.Attempt.DetachedBoltIds, Is.Empty);
            Assert.That(_coordinator.Attempt.RemainingBoltsByPart[0], Is.EqualTo(1));
        }

        [Test]
        public void InvalidBoltDoesNotStartAnyOperation()
        {
            var result = Result(_coordinator.PlayAsync(999).AsTask());
            Assert.That(result.Outcome, Is.EqualTo(BoltReservationOutcome.UnknownBolt));
            Assert.That(_operations.Turns, Is.Empty);
            Assert.That(_coordinator.Attempt.CurrentTurn, Is.Null);
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

            return waits;
        }

        private static void Finish(ControlledTurn waits)
        {
            waits.Detach.TrySetResult();
            waits.Arrive.TrySetResult();
            waits.Finish.TrySetResult();
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
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public bool ArrivalRequested;
            public bool FinishRequested;
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
