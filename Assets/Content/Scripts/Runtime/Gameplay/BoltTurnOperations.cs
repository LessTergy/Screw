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
        private const float ContainerBoltScale = 0.25f;
        private const float PoolBoltScale = 0.18f;
        private const float LidSlideDuration = 0.16f;
        private const float ClosedHoldDuration = 0.09f;

        [SerializeField] private CollectionView _collection;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _unscrewClip;
        [SerializeField] private AudioClip _seatClip;
        [SerializeField] private AudioClip _releaseClip;
        [SerializeField] private AudioClip _closeClip;
        [SerializeField, Min(0.01f)] private float _detachDuration = 0.25f;
        [SerializeField, Min(0.01f)] private float _flightDuration = 0.25f;
        [SerializeField, Min(0.01f)] private float _seatDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float _replacementDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float _withdrawDistance = 1.25f;

        private readonly Dictionary<int, Transform> _bolts = new Dictionary<int, Transform>();
        private readonly Dictionary<Material, Material> _foregroundMaterials = new Dictionary<Material, Material>();
        private LevelAttempt _attempt;
        private LevelContentCatalog _catalog;
        private LevelPartPhysics _partPhysics;
        private Shader _foregroundShader;

        public CollectionView Collection
        {
            get { return _collection; }
        }

        [Inject]
        public void Construct(LevelView view, LevelAttempt attempt, LevelContentCatalog catalog, LevelPartPhysics partPhysics)
        {
            _foregroundShader = Resources.Load<Shader>("ForegroundBolt");
            if (_foregroundShader == null)
            {
                throw new InvalidOperationException("Missing foreground bolt shader.");
            }

            _attempt = attempt;
            _catalog = catalog;
            _partPhysics = partPhysics;
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
            _audio.PlayOneShot(_unscrewClip);
            var destination = bolt.localPosition + bolt.localRotation * (Vector3.forward * (_withdrawDistance * bolt.localScale.z));
            var sequence = DOTween.Sequence();
            sequence.Append(bolt.DOLocalMove(destination, _detachDuration).SetEase(Ease.InOutSine));
            sequence.Join(bolt.DOLocalRotate(new Vector3(0f, 0f, 720f), _detachDuration, RotateMode.LocalAxisAdd)
                .SetEase(Ease.InOutSine));
            await AnimateBolt(bolt, sequence, cancellationToken);
        }

        public UniTask ReleasePartAsync(AttemptTurn turn, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var release = _partPhysics.ReleaseAsync(turn.ReleasedPartId.Value, cancellationToken);
            _audio.PlayOneShot(_releaseClip);
            return release;
        }

        public async UniTask ArriveAsync(AttemptTurn turn, CancellationToken cancellationToken)
        {
            await Fly(_bolts[turn.BoltId], turn.Destination, cancellationToken);
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
                    await Fly(_bolts[transfer.BoltId], transfer.Destination, cancellationToken);
                    _collection.RecordArrival(transfer.Destination);
                }
                else
                {
                    throw new InvalidOperationException("Unknown sorting step: " + step.GetType().Name);
                }
            }
        }

        private async UniTask Fly(Transform bolt, BoltDestination destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slot = _collection.GetSlot(destination);
            var seatedScale = Vector3.one * (destination.IsPool ? PoolBoltScale : ContainerBoltScale);
            var seatedRotation = Quaternion.Euler(0f, 180f, 0f);
            var headCenter = bolt.GetComponent<BoxCollider>().center;
            var camera = _collection.Camera;
            bolt.SetParent(camera.transform, true);
            var start = camera.WorldToViewportPoint(bolt.TransformPoint(headCenter));
            var startRotation = bolt.rotation;
            // In perspective, the apparent size is proportional to scale divided by depth.
            var scalePerDepth = bolt.localScale / start.z;

            Vector3 AboveSlot()
            {
                var slotDepth = camera.WorldToViewportPoint(slot.position).z;
                var flightScale = scalePerDepth.z * slotDepth;
                var slotScale = seatedScale.z * slot.lossyScale.z;
                return slot.position - slot.forward * (_withdrawDistance * Mathf.Max(flightScale, slotScale));
            }

            void PlaceHead(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                bolt.localScale = scale;
                bolt.rotation = rotation;
                bolt.position = position - bolt.TransformVector(headCenter);
            }

            var sequence = DOTween.Sequence();
            sequence.Append(DOTween.To(() => 0f, progress =>
            {
                var target = camera.WorldToViewportPoint(AboveSlot());
                var point = Vector3.LerpUnclamped(start, target, progress);
                var arcHeight = Mathf.Min(0.055f, Mathf.Abs(target.x - start.x) * 0.25f + Mathf.Abs(target.y - start.y) * 0.12f);
                point.y += arcHeight * 4f * progress * (1f - progress);
                var rotation = Quaternion.Slerp(startRotation, slot.rotation * seatedRotation, progress);
                PlaceHead(camera.ViewportToWorldPoint(point), rotation, scalePerDepth * point.z);
            }, 1f, _flightDuration).SetEase(Ease.InOutSine));
            sequence.Append(DOTween.To(() => 0f, progress =>
            {
                var position = Vector3.LerpUnclamped(AboveSlot(), slot.position, progress);
                var depth = camera.WorldToViewportPoint(position).z;
                var targetScale = Vector3.Scale(seatedScale, slot.lossyScale);
                var scale = Vector3.LerpUnclamped(scalePerDepth * depth, targetScale, progress);
                var twist = Quaternion.AngleAxis(-360f * progress, Vector3.forward);
                PlaceHead(position, slot.rotation * seatedRotation * twist, scale);
            }, 1f, _seatDuration).SetEase(Ease.InOutSine));
            await AnimateBolt(bolt, sequence, cancellationToken);
            bolt.SetParent(slot, false);
            bolt.localPosition = -(seatedRotation * Vector3.Scale(headCenter, seatedScale));
            bolt.localRotation = seatedRotation;
            bolt.localScale = seatedScale;
            _audio.PlayOneShot(_seatClip);
        }

        private async UniTask Replace(ContainerChange change, CancellationToken cancellationToken)
        {
            var container = _collection.Containers[change.ContainerIndex];
            var lid = container.Lid.transform;
            var closedPosition = lid.localPosition;
            lid.localPosition = closedPosition + Vector3.up * 0.16f;
            lid.gameObject.SetActive(true);
            var outgoing = DOTween.Sequence();
            outgoing.Append(lid.DOLocalMoveY(closedPosition.y, LidSlideDuration).SetEase(Ease.OutCubic));
            outgoing.AppendCallback(() =>
            {
                foreach (var id in change.CollectedBoltIds)
                {
                    _bolts[id].gameObject.SetActive(false);
                }

                _audio.PlayOneShot(_closeClip);
            });
            outgoing.AppendInterval(ClosedHoldDuration);
            outgoing.Append(container.Panel.DOLocalMoveY(1f, _replacementDuration).SetEase(Ease.InQuad));
            try
            {
                await Animate(outgoing, cancellationToken);
            }
            finally
            {
                if (lid != null)
                {
                    lid.localPosition = closedPosition;
                    lid.gameObject.SetActive(false);
                }
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

        private async UniTask AnimateBolt(Transform bolt, Sequence sequence, CancellationToken cancellationToken)
        {
            var renderer = bolt.GetComponentInChildren<MeshRenderer>();
            var originals = renderer.sharedMaterials;
            renderer.sharedMaterials = new[] { GetForegroundMaterial(originals[0]), GetForegroundMaterial(originals[1]) };
            try
            {
                await Animate(sequence, cancellationToken);
            }
            finally
            {
                if (renderer != null)
                {
                    renderer.sharedMaterials = originals;
                }
            }
        }

        private Material GetForegroundMaterial(Material source)
        {
            if (!_foregroundMaterials.TryGetValue(source, out var material))
            {
                material = new Material(_foregroundShader);
                material.color = source.color;
                material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
                material.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
                _foregroundMaterials.Add(source, material);
            }

            return material;
        }

        private void OnDestroy()
        {
            foreach (var material in _foregroundMaterials.Values)
            {
                Destroy(material);
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
