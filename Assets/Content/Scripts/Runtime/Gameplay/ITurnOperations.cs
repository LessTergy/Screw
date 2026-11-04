using System.Threading;
using Cysharp.Threading.Tasks;

namespace Screw.Gameplay
{
    public interface ITurnOperations
    {
        UniTask DetachAsync(AttemptTurn turn, CancellationToken cancellationToken);
        UniTask ReleasePartAsync(AttemptTurn turn, CancellationToken cancellationToken);
        UniTask ArriveAsync(AttemptTurn turn, CancellationToken cancellationToken);
        UniTask FinishAsync(AttemptTurn turn, CancellationToken cancellationToken);
    }
}
