using System.Collections.Generic;
using Screw.Levels;
using TMPro;
using UnityEngine;

namespace Screw.Gameplay
{
    public sealed class CollectionView : MonoBehaviour
    {
        public const float BoltSlotScale = 0.18f;

        [SerializeField] private Camera _camera;
        [SerializeField] private CollectionContainer[] _containers;
        [SerializeField] private Transform _poolRoot;
        [SerializeField] private Transform[] _poolSlots;
        [SerializeField] private TMP_Text _poolCounter;
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
            _poolCounter.text = "0/5";
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
                _poolCounter.text = PoolCount + "/5";
            }
            else
            {
                _containers[destination.ContainerIndex.Value].RecordArrival();
            }
        }

        public void RecordPoolDeparture()
        {
            PoolCount--;
            _poolCounter.text = PoolCount + "/5";
        }

        private void LateUpdate()
        {
            RefreshLayout();
        }

        private void RefreshLayout()
        {
            var halfHeight = _cameraDepth * Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            var halfWidth = halfHeight * _camera.aspect;
            var unit = Mathf.Min(halfWidth, halfHeight * 0.65f);
            for (var index = 0; index < _containers.Length; index++)
            {
                var node = _containers[index].transform;
                node.localPosition = new Vector3((index == 0 ? -0.48f : 0.48f) * unit, 0.88f * halfHeight, _cameraDepth);
                node.localScale = Vector3.one * unit;
            }

            _poolRoot.localPosition = new Vector3(0f, 0.7f * halfHeight, _cameraDepth);
            _poolRoot.localScale = Vector3.one * unit;
        }
    }
}
