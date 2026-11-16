using System;
using System.Collections.Generic;
using Screw.Levels;
using UnityEngine;

namespace Screw.Gameplay
{
    public sealed class CollectionView : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private RectTransform _safeArea;
        [SerializeField] private CollectionContainer[] _containers;
        [SerializeField] private Transform[] _lockedContainers;
        [SerializeField] private Transform _poolRoot;
        [SerializeField] private Transform[] _poolSlots;
        [SerializeField, Min(0.5f)] private float _cameraDepth = 2f;

        private LevelAttempt _attempt;
        private Transform _extraPoolSlot;
        [NonSerialized] private Transform[] _expandedPoolSlots;

        public Camera Camera
        {
            get { return _camera; }
        }

        internal RectTransform SafeArea => _safeArea;

        public IReadOnlyList<CollectionContainer> Containers
        {
            get { return _containers; }
        }

        public IReadOnlyList<Transform> PoolSlots
        {
            get { return _attempt != null && _attempt.HasExtraPoolSlot ? _expandedPoolSlots : _poolSlots; }
        }

        public int PoolCount { get; private set; }

        public void Initialize(LevelAttempt attempt, LevelContentCatalog catalog)
        {
            if (_poolSlots.Length != LevelDataValidator.PoolCapacity)
            {
                throw new InvalidOperationException($"CollectionView requires {LevelDataValidator.PoolCapacity} pool slots.");
            }

            var last = _poolSlots[_poolSlots.Length - 1];
            if (_extraPoolSlot == null)
            {
                // Prepare the socket before the last anchor can hold a seated bolt.
                _extraPoolSlot = Instantiate(last, last.parent, false);
                _extraPoolSlot.name = "Extra Pool Slot";
            }

            var previous = _poolSlots[_poolSlots.Length - 2];
            _extraPoolSlot.localPosition = last.localPosition + (last.localPosition - previous.localPosition);
            _expandedPoolSlots = new Transform[_poolSlots.Length + 1];
            _poolSlots.CopyTo(_expandedPoolSlots, 0);
            _expandedPoolSlots[_poolSlots.Length] = _extraPoolSlot;

            for (var index = 0; index < _containers.Length; index++)
            {
                _containers[index].Panel.localPosition = Vector3.zero;
                _containers[index].Configure(attempt.Containers[index].ColorGroupId, catalog, attempt.PaletteIndices);
            }

            _attempt = attempt;
            PoolCount = 0;
            RefreshLayout();
        }

        public Transform GetSlot(BoltDestination destination)
        {
            if (!destination.IsPool)
            {
                return _containers[destination.ContainerIndex.Value].Slots[destination.SlotIndex];
            }

            var slot = PoolSlots[destination.SlotIndex];
            if (!slot.gameObject.activeSelf)
            {
                RefreshLayout();
            }

            return slot;
        }

        public void RecordArrival(BoltDestination destination)
        {
            if (destination.IsPool)
            {
                PoolCount++;
            }
            else
            {
                _containers[destination.ContainerIndex.Value].RecordArrival();
            }
        }

        public void RecordPoolDeparture()
        {
            PoolCount--;
        }

        private void LateUpdate()
        {
            if (_attempt != null)
            {
                RefreshLayout();
            }
        }

        private void RefreshLayout()
        {
            var halfHeight = _cameraDepth * Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            var halfSize = new Vector2(halfHeight * _camera.aspect, halfHeight);
            var safeMin = Vector2.Scale(_safeArea.anchorMin * 2f - Vector2.one, halfSize);
            var safeMax = Vector2.Scale(_safeArea.anchorMax * 2f - Vector2.one, halfSize);
            var safeSize = safeMax - safeMin;
            var centerX = (safeMin.x + safeMax.x) * 0.5f;
            var rowY = safeMax.y - safeSize.y * 0.17f;
            var count = _containers.Length + _lockedContainers.Length;
            var rowWidth = Mathf.Min(safeSize.x * 0.96f, safeSize.y * 0.57f);
            var cellWidth = rowWidth / count;
            var unit = cellWidth / 0.64f;
            for (var index = 0; index < count; index++)
            {
                var node = index < _containers.Length ? _containers[index].transform : _lockedContainers[index - _containers.Length];
                var x = centerX + (index + 0.5f) * cellWidth - rowWidth * 0.5f;
                node.localPosition = new Vector3(x, rowY, _cameraDepth);
                node.localScale = Vector3.one * unit;
            }

            var poolY = rowY - 0.425f * unit;
            var poolScale = unit;
            if (_extraPoolSlot != null)
            {
                var expanded = _attempt.HasExtraPoolSlot;
                if (_extraPoolSlot.gameObject.activeSelf != expanded)
                {
                    _extraPoolSlot.gameObject.SetActive(expanded);
                }

                if (expanded)
                {
                    poolScale *= (float)_poolSlots.Length / (_poolSlots.Length + 1);
                    var offset = (_extraPoolSlot.localPosition.x - _poolSlots[_poolSlots.Length - 1].localPosition.x) * 0.5f;
                    centerX -= offset * poolScale;
                }
            }

            _poolRoot.localPosition = new Vector3(centerX, poolY, _cameraDepth);
            _poolRoot.localScale = Vector3.one * poolScale;
        }
    }
}
