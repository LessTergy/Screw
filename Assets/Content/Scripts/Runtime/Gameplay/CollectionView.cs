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

        public Camera Camera
        {
            get { return _camera; }
        }

        public IReadOnlyList<CollectionContainer> Containers
        {
            get { return _containers; }
        }

        public IReadOnlyList<Transform> PoolSlots
        {
            get { return _poolSlots; }
        }

        public int PoolCount { get; private set; }

        public void Initialize(LevelAttempt attempt, LevelContentCatalog catalog)
        {
            for (var index = 0; index < _containers.Length; index++)
            {
                _containers[index].Panel.localPosition = Vector3.zero;
                _containers[index].Configure(attempt.Containers[index].ColorGroupId, catalog, attempt.PaletteIndices);
            }

            PoolCount = 0;
            RefreshLayout();
        }

        public Transform GetSlot(BoltDestination destination)
        {
            return destination.IsPool ? _poolSlots[destination.SlotIndex]
                : _containers[destination.ContainerIndex.Value].Slots[destination.SlotIndex];
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
            RefreshLayout();
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

            _poolRoot.localPosition = new Vector3(centerX, rowY - 0.425f * unit, _cameraDepth);
            _poolRoot.localScale = Vector3.one * unit;
        }
    }
}
