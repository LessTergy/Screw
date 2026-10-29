using System;
using System.Collections.Generic;

namespace Screw.Gameplay
{
    public abstract class SortingStep
    {
        public int ContainerIndex { get; }

        private protected SortingStep(int containerIndex)
        {
            ContainerIndex = containerIndex;
        }
    }

    public sealed class ContainerChange : SortingStep
    {
        public int PreviousColorGroupId { get; }
        public int? NextColorGroupId { get; }
        public IReadOnlyList<int> CollectedBoltIds { get; }

        internal ContainerChange(int containerIndex, int previousColorGroupId, int? nextColorGroupId, int[] boltIds)
            : base(containerIndex)
        {
            PreviousColorGroupId = previousColorGroupId;
            NextColorGroupId = nextColorGroupId;
            CollectedBoltIds = Array.AsReadOnly(boltIds);
        }
    }

    public sealed class PoolTransfer : SortingStep
    {
        public int BoltId { get; }
        public int PoolSlotIndex { get; }
        public BoltDestination Destination { get; }

        internal PoolTransfer(int boltId, int poolSlotIndex, BoltDestination destination)
            : base(destination.ContainerIndex.Value)
        {
            BoltId = boltId;
            PoolSlotIndex = poolSlotIndex;
            Destination = destination;
        }
    }
}
