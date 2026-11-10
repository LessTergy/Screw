using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Screw.Gameplay
{
    public sealed class TurnCoordinator : IDisposable
    {
        private readonly ITurnOperations _operations;
        private CancellationTokenSource _attemptCancellation = new CancellationTokenSource();
        private bool _disposed;
        private UniTaskCompletionSource _sortingCompletion;

        public LevelAttempt Attempt { get; private set; }

        public TurnCoordinator(LevelAttempt attempt, ITurnOperations operations)
        {
            Attempt = attempt;
            _operations = operations;
        }

        public async UniTask<BoltReservationResult> PlayAsync(int boltId)
        {
            ThrowIfDisposed();
            var attempt = Attempt;
            var token = _attemptCancellation.Token;
            var result = attempt.ReserveBolt(boltId);
            if (result.Outcome != BoltReservationOutcome.Reserved)
            {
                if (result.Outcome == BoltReservationOutcome.PoolOverflow)
                {
                    _attemptCancellation.Cancel();
                }

                return result;
            }

            var turn = result.Turn;
            await _operations.DetachAsync(turn, token).AttachExternalCancellation(token);
            EnsureCurrent(attempt, token);
            attempt.ConfirmDetachment(turn);

            var removal = turn.ReleasedPartId.HasValue
                ? _operations.ReleasePartAsync(turn, token)
                : UniTask.CompletedTask;
            await UniTask.WhenAll(removal, CollectAsync(attempt, turn, token)).AttachExternalCancellation(token);
            EnsureCurrent(attempt, token);
            attempt.CompleteTurn(turn);
            return result;
        }

        private async UniTask CollectAsync(LevelAttempt attempt, AttemptTurn turn, CancellationToken token)
        {
            await WaitForSortingAsync(token);
            EnsureCurrent(attempt, token);
            await _operations.ArriveAsync(turn, token).AttachExternalCancellation(token);
            await WaitForSortingAsync(token);
            EnsureCurrent(attempt, token);
            attempt.ConfirmArrival(turn);
            var completion = new UniTaskCompletionSource();
            _sortingCompletion = completion;
            try
            {
                await _operations.FinishAsync(turn, token).AttachExternalCancellation(token);
            }
            finally
            {
                if (ReferenceEquals(_sortingCompletion, completion))
                {
                    _sortingCompletion = null;
                }

                completion.TrySetResult();
            }
        }

        private async UniTask WaitForSortingAsync(CancellationToken token)
        {
            while (_sortingCompletion != null)
            {
                await _sortingCompletion.Task.AttachExternalCancellation(token);
            }

            token.ThrowIfCancellationRequested();
        }

        public void Restart()
        {
            ThrowIfDisposed();
            var previousCancellation = _attemptCancellation;
            Attempt = Attempt.Restart();
            _attemptCancellation = new CancellationTokenSource();
            _sortingCompletion = null;
            try
            {
                previousCancellation.Cancel();
            }
            finally
            {
                previousCancellation.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _attemptCancellation.Cancel();
            }
            finally
            {
                _attemptCancellation.Dispose();
            }
        }

        private void EnsureCurrent(LevelAttempt attempt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_disposed || !ReferenceEquals(attempt, Attempt))
            {
                throw new OperationCanceledException(token);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TurnCoordinator));
            }
        }
    }
}
