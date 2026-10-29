using System;
using System.Collections.Generic;

namespace Screw.Gameplay
{
    public enum TurnPhase
    {
        Reserved,
        InTransit,
        AwaitingCompletion,
        Completed,
        Canceled
    }

    public sealed class AttemptTurn
    {
        public int BoltId { get; }
        public BoltDestination Destination { get; }
        public TurnPhase Phase { get; internal set; }
        public int? ReleasedPartId { get; internal set; }
        public IReadOnlyList<SortingStep> SortingSteps { get; internal set; } = Array.Empty<SortingStep>();

        internal AttemptTurn(int boltId, BoltDestination destination)
        {
            BoltId = boltId;
            Destination = destination;
        }
    }
}
