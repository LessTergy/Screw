using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Screw.Levels;

namespace Screw.Gameplay
{
    public sealed class LevelAttempt
    {
        private readonly Dictionary<int, AttemptBolt> _bolts;
        private readonly Dictionary<int, int> _remainingBoltsByPart;
        private readonly Dictionary<int, BoltDestination> _reservations;
        private readonly int?[][] _containerSlots;
        private readonly int?[] _poolSlots;
        private readonly AttemptContainer[] _containers;
        private readonly List<int> _containerQueue;
        private readonly List<int> _poolArrivalOrder = new List<int>();
        private readonly List<int> _collectedBoltIds = new List<int>();
        private readonly List<int> _detachedBoltIds = new List<int>();
        private readonly Definition _definition;

        public int LevelNumber { get; }
        public AttemptStatus Status { get; private set; }
        public IReadOnlyDictionary<int, AttemptBolt> Bolts { get; }
        public IReadOnlyDictionary<int, int> RemainingBoltsByPart { get; }
        public IReadOnlyDictionary<int, BoltDestination> Reservations { get; }
        public IReadOnlyList<int> PaletteIndices { get; }
        public IReadOnlyList<int> ContainerQueue { get; }
        public IReadOnlyList<AttemptContainer> Containers { get; }
        public IReadOnlyList<int?> PoolSlots { get; }
        public IReadOnlyList<int> CollectedBoltIds { get; }
        public IReadOnlyList<int> DetachedBoltIds { get; }
        public AttemptTurn CurrentTurn { get; private set; }

        public bool IsVictoryReady
        {
            get
            {
                return _collectedBoltIds.Count == _bolts.Count && PoolCount == 0
                    && _remainingBoltsByPart.Values.All(count => count == 0);
            }
        }

        public int PoolCount
        {
            get { return _poolSlots.Count(id => id.HasValue); }
        }

        public LevelAttempt(LevelSourceData source, LevelGameplayData gameplay)
            : this(new Definition(source, gameplay))
        {
        }

        private LevelAttempt(Definition definition)
        {
            _definition = definition;
            LevelNumber = definition.LevelNumber;
            Status = AttemptStatus.Active;
            var palette = definition.Palette.ToArray();
            PaletteIndices = Array.AsReadOnly(palette);

            _bolts = definition.Bolts.ToDictionary(bolt => bolt.Id);
            Bolts = new ReadOnlyDictionary<int, AttemptBolt>(_bolts);

            _remainingBoltsByPart = definition.Attachments.ToDictionary(item => item.Key, item => item.Value);
            RemainingBoltsByPart = new ReadOnlyDictionary<int, int>(_remainingBoltsByPart);

            _reservations = new Dictionary<int, BoltDestination>();
            Reservations = new ReadOnlyDictionary<int, BoltDestination>(_reservations);
            _containerSlots = Enumerable.Range(0, 2)
                .Select(index => new int?[LevelDataValidator.ContainerCapacity]).ToArray();
            _containers = Enumerable.Range(0, 2).Select(index =>
            {
                var group = definition.ContainerGroups[index];
                return new AttemptContainer(group, palette[group], _containerSlots[index]);
            }).ToArray();
            Containers = Array.AsReadOnly(_containers);

            _containerQueue = new List<int>(definition.ContainerGroups.Skip(2));
            ContainerQueue = _containerQueue.AsReadOnly();
            _poolSlots = new int?[LevelDataValidator.PoolCapacity];
            PoolSlots = Array.AsReadOnly(_poolSlots);
            CollectedBoltIds = _collectedBoltIds.AsReadOnly();
            DetachedBoltIds = _detachedBoltIds.AsReadOnly();
        }

        public BoltReservationResult ReserveBolt(int boltId)
        {
            if (Status != AttemptStatus.Active)
            {
                return new BoltReservationResult(BoltReservationOutcome.AttemptInactive);
            }

            if (!_bolts.TryGetValue(boltId, out var bolt))
            {
                return new BoltReservationResult(BoltReservationOutcome.UnknownBolt);
            }

            if (_reservations.ContainsKey(boltId))
            {
                return new BoltReservationResult(BoltReservationOutcome.AlreadyReserved);
            }

            if (_detachedBoltIds.Contains(boltId))
            {
                return new BoltReservationResult(BoltReservationOutcome.AlreadyRemoved);
            }

            if (CurrentTurn != null)
            {
                return new BoltReservationResult(BoltReservationOutcome.TurnInProgress);
            }

            for (var containerIndex = 0; containerIndex < Containers.Count; containerIndex++)
            {
                if (!Containers[containerIndex].IsActive || Containers[containerIndex].ColorGroupId != bolt.ColorGroupId)
                {
                    continue;
                }

                var slot = Array.FindIndex(_containerSlots[containerIndex], id => !id.HasValue);
                if (slot >= 0)
                {
                    var destination = new BoltDestination(containerIndex, slot);
                    _containerSlots[containerIndex][slot] = boltId;
                    return StartTurn(boltId, destination);
                }
            }

            var poolSlot = Array.FindIndex(_poolSlots, id => !id.HasValue);
            if (poolSlot >= 0)
            {
                var destination = new BoltDestination(null, poolSlot);
                _poolSlots[poolSlot] = boltId;
                return StartTurn(boltId, destination);
            }

            Status = AttemptStatus.Lost;
            return new BoltReservationResult(BoltReservationOutcome.PoolOverflow);
        }

        private BoltReservationResult StartTurn(int boltId, BoltDestination destination)
        {
            _reservations.Add(boltId, destination);
            CurrentTurn = new AttemptTurn(boltId, destination);
            return new BoltReservationResult(BoltReservationOutcome.Reserved, CurrentTurn);
        }

        public bool CancelReservation(AttemptTurn turn)
        {
            if (!IsCurrentTurn(turn, TurnPhase.Reserved))
            {
                return false;
            }

            var destination = turn.Destination;
            if (destination.ContainerIndex.HasValue)
            {
                _containerSlots[destination.ContainerIndex.Value][destination.SlotIndex] = null;
            }
            else
            {
                _poolSlots[destination.SlotIndex] = null;
            }

            _reservations.Remove(turn.BoltId);
            turn.Phase = TurnPhase.Canceled;
            CurrentTurn = null;
            return true;
        }

        public bool ConfirmDetachment(AttemptTurn turn)
        {
            if (!IsCurrentTurn(turn, TurnPhase.Reserved))
            {
                return false;
            }

            var ownerId = _bolts[turn.BoltId].OwnerId;
            _remainingBoltsByPart[ownerId]--;
            _detachedBoltIds.Add(turn.BoltId);
            if (_remainingBoltsByPart[ownerId] == 0)
            {
                turn.ReleasedPartId = ownerId;
            }

            turn.Phase = TurnPhase.InTransit;
            return true;
        }

        public bool ConfirmArrival(AttemptTurn turn)
        {
            if (!IsCurrentTurn(turn, TurnPhase.InTransit))
            {
                return false;
            }

            _reservations.Remove(turn.BoltId);
            var destination = turn.Destination;
            if (destination.IsPool)
            {
                _poolArrivalOrder.Add(turn.BoltId);
            }

            turn.SortingSteps = ResolveSorting();
            turn.Phase = TurnPhase.AwaitingCompletion;
            return true;
        }

        public bool CompleteTurn(AttemptTurn turn)
        {
            if (!IsCurrentTurn(turn, TurnPhase.AwaitingCompletion))
            {
                return false;
            }

            turn.Phase = TurnPhase.Completed;
            CurrentTurn = null;
            if (IsVictoryReady)
            {
                Status = AttemptStatus.Won;
            }

            return true;
        }

        public LevelAttempt Restart()
        {
            return new LevelAttempt(_definition);
        }

        private bool IsCurrentTurn(AttemptTurn turn, TurnPhase phase)
        {
            return Status == AttemptStatus.Active && turn != null
                && ReferenceEquals(CurrentTurn, turn) && turn.Phase == phase;
        }

        private IReadOnlyList<SortingStep> ResolveSorting()
        {
            var steps = new List<SortingStep>();
            while (true)
            {
                var full = Array.FindIndex(_containers, container => container.IsActive
                    && container.Slots.All(id => id.HasValue));
                if (full >= 0)
                {
                    var ids = _containerSlots[full].Select(id => id.Value).ToArray();
                    _collectedBoltIds.AddRange(ids);
                    var previousGroup = _containers[full].ColorGroupId;
                    int? nextGroup = null;
                    Array.Clear(_containerSlots[full], 0, _containerSlots[full].Length);
                    if (_containerQueue.Count > 0)
                    {
                        nextGroup = _containerQueue[0];
                        _containerQueue.RemoveAt(0);
                        _containers[full].Replace(nextGroup.Value, PaletteIndices[nextGroup.Value]);
                    }
                    else
                    {
                        _containers[full].Deactivate();
                    }

                    steps.Add(new ContainerChange(full, previousGroup, nextGroup, ids));
                    continue;
                }

                var transferred = false;
                for (var index = 0; index < _containers.Length; index++)
                {
                    var slot = Array.FindIndex(_containerSlots[index], id => !id.HasValue);
                    if (!_containers[index].IsActive || slot < 0)
                    {
                        continue;
                    }

                    var arrival = _poolArrivalOrder.FindIndex(id => _bolts[id].ColorGroupId == _containers[index].ColorGroupId);
                    if (arrival < 0)
                    {
                        continue;
                    }

                    var boltId = _poolArrivalOrder[arrival];
                    var poolSlot = Array.IndexOf(_poolSlots, (int?)boltId);
                    _poolArrivalOrder.RemoveAt(arrival);
                    _poolSlots[poolSlot] = null;
                    _containerSlots[index][slot] = boltId;
                    steps.Add(new PoolTransfer(boltId, poolSlot, new BoltDestination(index, slot)));
                    transferred = true;
                    break;
                }

                if (!transferred)
                {
                    return steps.AsReadOnly();
                }
            }
        }

        private sealed class Definition
        {
            public int LevelNumber { get; }
            public int[] Palette { get; }
            public AttemptBolt[] Bolts { get; }
            public KeyValuePair<int, int>[] Attachments { get; }
            public int[] ContainerGroups { get; }

            public Definition(LevelSourceData source, LevelGameplayData gameplay)
            {
                LevelDataValidator.ValidateGameplay(source, gameplay);
                LevelNumber = source.Number;
                Palette = source.AllowedBoltPaletteIndices.ToArray();
                ContainerGroups = gameplay.ContainerGroups.ToArray();
                Attachments = source.Parts.Select(part => new KeyValuePair<int, int>(part.Id, part.BoltIds.Length)).ToArray();
                var groups = gameplay.ColorGroups.SelectMany(group => group.BoltIds.Select(id => new { BoltId = id, GroupId = group.Id }))
                    .ToDictionary(item => item.BoltId, item => item.GroupId);
                Bolts = source.Bolts.Select(bolt => new AttemptBolt(bolt.Id, bolt.OwnerId, groups[bolt.Id], Palette[groups[bolt.Id]]))
                    .ToArray();
            }
        }
    }

    public enum AttemptStatus
    {
        Active,
        Lost,
        Won
    }

    public readonly struct AttemptBolt
    {
        public int Id { get; }
        public int OwnerId { get; }
        public int ColorGroupId { get; }
        public int PaletteIndex { get; }

        internal AttemptBolt(int id, int ownerId, int colorGroupId, int paletteIndex)
        {
            Id = id;
            OwnerId = ownerId;
            ColorGroupId = colorGroupId;
            PaletteIndex = paletteIndex;
        }
    }

    public sealed class AttemptContainer
    {
        public bool IsActive { get; private set; } = true;
        public int ColorGroupId { get; private set; }
        public int PaletteIndex { get; private set; }
        public IReadOnlyList<int?> Slots { get; }

        public int ReservedCount
        {
            get { return Slots.Count(id => id.HasValue); }
        }

        internal AttemptContainer(int colorGroupId, int paletteIndex, int?[] slots)
        {
            ColorGroupId = colorGroupId;
            PaletteIndex = paletteIndex;
            Slots = Array.AsReadOnly(slots);
        }

        internal void Replace(int colorGroupId, int paletteIndex)
        {
            ColorGroupId = colorGroupId;
            PaletteIndex = paletteIndex;
        }

        internal void Deactivate()
        {
            IsActive = false;
        }
    }
}
