namespace Screw.Gameplay
{
    public readonly struct BoltDestination
    {
        public int? ContainerIndex { get; }
        public int SlotIndex { get; }

        public bool IsPool
        {
            get { return !ContainerIndex.HasValue; }
        }

        internal BoltDestination(int? containerIndex, int slotIndex)
        {
            ContainerIndex = containerIndex;
            SlotIndex = slotIndex;
        }
    }

    public enum BoltReservationOutcome
    {
        UnknownBolt,
        Reserved,
        AlreadyReserved,
        AttemptInactive,
        PoolOverflow,
        AlreadyRemoved
    }

    public readonly struct BoltReservationResult
    {
        public BoltReservationOutcome Outcome { get; }
        public BoltDestination? Destination { get; }
        public AttemptTurn Turn { get; }

        internal BoltReservationResult(BoltReservationOutcome outcome, AttemptTurn turn = null)
        {
            Outcome = outcome;
            Turn = turn;
            Destination = turn?.Destination;
        }
    }
}
