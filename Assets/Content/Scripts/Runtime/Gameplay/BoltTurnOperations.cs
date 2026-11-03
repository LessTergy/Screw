using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Screw.Levels;
using UnityEngine;
using VContainer;

namespace Screw.Gameplay
{
    public sealed class BoltTurnOperations : MonoBehaviour, ITurnOperations
    {
        [SerializeField] private CollectionView _collection;
        [SerializeField, Min(0.01f)] private float _detachDuration = 0.25f;
        [SerializeField, Min(0.01f)] private float _flightDuration = 0.25f;
        [SerializeField, Min(0.01f)] private float _replacementDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float _withdrawDistance = 1.25f;

        private readonly Dictionary<int, Transform> _bolts = new Dictionary<int, Transform>();
        private LevelAttempt _attempt;
        private LevelContentCatalog _catalog;

        public CollectionView Collection
        {
            get { return _collection; }
        }

        [Inject]
        public void Construct(LevelView view, LevelAttempt attempt, LevelContentCatalog catalog)
        {
            _attempt = attempt;
            _catalog = catalog;
            _bolts.Clear();
            foreach (var binding in view.Bolts)
            {
                var node = binding.Node;
                var color = catalog.GetColor(attempt.Bolts[binding.Id].PaletteIndex);
                node.GetComponentInChildren<MeshRenderer>().sharedMaterials = new[] { color.Primary, color.Secondary };
                _bolts.Add(binding.Id, node);
            }

            _collection.Initialize(attempt, catalog);
        }

        public async UniTask DetachAsync(AttemptTurn turn, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bolt = _bolts[turn.BoltId];
            bolt.GetComponent<Collider>().enabled = false;
            var destination = bolt.position + bolt.TransformVector(Vector3.forward * _withdrawDistance);
            var sequence = DOTween.Sequence();
            sequence.Append(bolt.DOMove(destination, _detachDuration).SetEase(Ease.InOutSine));
            sequence.Join(bolt.DOLocalRotate(new Vector3(0f, 0f, 720f), _detachDuration, RotateMode.LocalAxisAdd)
                .SetEase(Ease.InOutSine));
            await Animate(sequence, cancellationToken);
        }

        public async UniTask ArriveAsync(AttemptTurn turn, CancellationToken cancellationToken)
        {
            await Fly(_bolts[turn.BoltId], _collection.GetSlot(turn.Destination), cancellationToken);
            _collection.RecordArrival(turn.Destination);
        }

        public async UniTask FinishAsync(AttemptTurn turn, CancellationToken cancellationToken)
        {
            foreach (var step in turn.SortingSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (step is ContainerChange change)
                {
                    await Replace(change, cancellationToken);
                }
                else if (step is PoolTransfer transfer)
                {
                    _collection.RecordPoolDeparture();
                    await Fly(_bolts[transfer.BoltId], _collection.GetSlot(transfer.Destination), cancellationToken);
                    _collection.RecordArrival(transfer.Destination);
                }
                else
                {
                    throw new InvalidOperationException("Unknown sorting step: " + step.GetType().Name);
                }
            }
        }

        private async UniTask Fly(Transform bolt, Transform slot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cameraRoot = _collection.Camera.transform;
            bolt.SetParent(cameraRoot, true);
            var start = bolt.localPosition;
            var rotation = Quaternion.Euler(0f, 180f, 0f);
            var position = -(rotation * (bolt.GetComponent<BoxCollider>().center * CollectionView.BoltSlotScale));
            var sequence = DOTween.Sequence();
            sequence.Append(DOTween.To(() => 0f, progress =>
            {
                var destination = cameraRoot.InverseTransformPoint(slot.TransformPoint(position));
                bolt.localPosition = Vector3.LerpUnclamped(start, destination, progress);
            }, 1f, _flightDuration).SetEase(Ease.InOutQuad));
            sequence.Join(bolt.DOLocalRotate(new Vector3(0f, 180f, 0f), _flightDuration).SetEase(Ease.InOutQuad));
            sequence.Join(bolt.DOScale(Vector3.one * (slot.lossyScale.x * CollectionView.BoltSlotScale), _flightDuration));
            await Animate(sequence, cancellationToken);
            bolt.SetParent(slot, false);
            bolt.localPosition = position;
            bolt.localRotation = rotation;
            bolt.localScale = Vector3.one * CollectionView.BoltSlotScale;
        }

        private async UniTask Replace(ContainerChange change, CancellationToken cancellationToken)
        {
            var container = _collection.Containers[change.ContainerIndex];
            var outgoing = DOTween.Sequence().Append(container.Panel.DOLocalMoveY(1f, _replacementDuration));
            await Animate(outgoing, cancellationToken);
            foreach (var id in change.CollectedBoltIds)
            {
                _bolts[id].gameObject.SetActive(false);
            }

            container.Configure(change.NextColorGroupId, _catalog, _attempt.PaletteIndices);
            if (change.NextColorGroupId.HasValue)
            {
                var incoming = DOTween.Sequence().Append(container.Panel.DOLocalMove(Vector3.zero, _replacementDuration));
                await Animate(incoming, cancellationToken);
            }
            else
            {
                container.Panel.localPosition = Vector3.zero;
            }
        }

        private async UniTask Animate(Sequence sequence, CancellationToken cancellationToken)
        {
            var completed = false;
            sequence.SetLink(gameObject);
            sequence.OnComplete(() => completed = true);
            using (cancellationToken.Register(() => sequence.Kill()))
            {
                try
                {
                    await UniTask.WaitUntil(() => !sequence.IsActive(), cancellationToken: cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!completed)
                    {
                        throw new InvalidOperationException("Turn animation was killed before completion.");
                    }
                }
                finally
                {
                    sequence.Kill();
                }
            }
        }
    }
}
