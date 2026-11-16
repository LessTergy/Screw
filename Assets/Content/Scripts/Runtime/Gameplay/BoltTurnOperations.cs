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
        private const float BoxSquashDuration = 0.055f;
        private const float BoxStretchDuration = 0.09f;
        private const float BoxSettleDuration = 0.075f;
        private const float ClosedHoldDuration = 0.05f;
        private const int CloseParticleCount = 12;
        private const int BoltParticleCount = 12;

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
        private ParticleSystem _closeParticles;
        private ParticleSystem _boltParticles;

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
            if (_closeParticles == null)
            {
                _closeParticles = CreateImpactParticles("Container Close Particles", CloseParticleCount);
            }

            var boltParticleCapacity = view.Bolts.Count * BoltParticleCount;
            if (_boltParticles == null)
            {
                var material = Resources.Load<Material>("BoltWithdrawalParticles");
                if (material == null)
                {
                    throw new InvalidOperationException("Missing bolt withdrawal particle material.");
                }

                _boltParticles = CreateImpactParticles("Bolt Withdrawal Particles", boltParticleCapacity);
                _boltParticles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            }

            _boltParticles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            _boltParticles.gameObject.layer = view.BoltsRoot.gameObject.layer;
            var boltParticlesMain = _boltParticles.main;
            boltParticlesMain.maxParticles = boltParticleCapacity;
            boltParticlesMain.customSimulationSpace = view.LevelRoot;
            boltParticlesMain.simulationSpace = ParticleSystemSimulationSpace.Custom;
        }

        public async UniTask DetachAsync(AttemptTurn turn, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bolt = _bolts[turn.BoltId];
            var head = bolt.GetComponent<BoxCollider>();
            head.enabled = false;
            _audio.PlayOneShot(_unscrewClip);
            var color = _catalog.GetColor(_attempt.Bolts[turn.BoltId].PaletteIndex).Primary.color;
            EmitBoltParticles(bolt, head, color);
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
            var panel = container.Panel;
            var lid = container.Lid.transform;
            var closedPosition = lid.localPosition;
            var restScale = panel.localScale;
            var squashScale = Vector3.Scale(restScale, new Vector3(1.13f, 0.8f, 1f));
            var stretchScale = Vector3.Scale(restScale, new Vector3(0.96f, 1.07f, 1f));
            lid.localPosition = closedPosition + Vector3.up * 0.16f;
            lid.gameObject.SetActive(true);
            var outgoing = DOTween.Sequence();
            outgoing.Append(lid.DOLocalMoveY(closedPosition.y, LidSlideDuration).SetEase(Ease.InCubic));
            outgoing.AppendCallback(() =>
            {
                foreach (var id in change.CollectedBoltIds)
                {
                    _bolts[id].gameObject.SetActive(false);
                }

                _audio.PlayOneShot(_closeClip);
                EmitCloseParticles(container.Lid);
            });
            outgoing.Append(panel.DOScale(squashScale, BoxSquashDuration).SetEase(Ease.OutQuad));
            outgoing.Append(panel.DOScale(stretchScale, BoxStretchDuration).SetEase(Ease.InOutSine));
            outgoing.Append(panel.DOScale(restScale, BoxSettleDuration).SetEase(Ease.OutQuad));
            outgoing.AppendInterval(ClosedHoldDuration);
            outgoing.Append(panel.DOLocalMoveY(1f, _replacementDuration).SetEase(Ease.InQuad));
            try
            {
                await Animate(outgoing, cancellationToken);
            }
            finally
            {
                if (_closeParticles != null)
                {
                    _closeParticles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                if (panel != null)
                {
                    panel.localScale = restScale;
                }

                if (lid != null)
                {
                    lid.localPosition = closedPosition;
                    lid.gameObject.SetActive(false);
                }
            }

            container.Configure(change.NextColorGroupId, _catalog, _attempt.PaletteIndices);
            if (change.NextColorGroupId.HasValue)
            {
                var incoming = DOTween.Sequence().Append(panel.DOLocalMove(Vector3.zero, _replacementDuration));
                await Animate(incoming, cancellationToken);
            }
            else
            {
                panel.localPosition = Vector3.zero;
            }
        }

        private ParticleSystem CreateImpactParticles(string name, int capacity)
        {
            var emitter = new GameObject(name);
            emitter.SetActive(false);
            emitter.transform.SetParent(transform, false);
            var particles = emitter.AddComponent<ParticleSystem>();

            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.25f;
            main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            emitter.SetActive(true);
            return particles;
        }

        private void EmitBoltParticles(Transform bolt, BoxCollider head, Color boltColor)
        {
            if (!_boltParticles.isPlaying)
            {
                _boltParticles.Play(false);
            }

            var particleSpace = _boltParticles.main.customSimulationSpace;
            var headFront = head.center + Vector3.forward * (head.size.z * 0.5f);
            var diameter = particleSpace.InverseTransformVector(bolt.TransformVector(Vector3.right * head.size.x)).magnitude;
            var screenRotation = Quaternion.Inverse(particleSpace.rotation) * _collection.Camera.transform.rotation;
            var towardsCamera = screenRotation * Vector3.back;
            var origin = particleSpace.InverseTransformPoint(bolt.TransformPoint(headFront)) + towardsCamera * (diameter * 0.12f);
            var color = Color.Lerp(boltColor, new Color(1f, 0.96f, 0.78f), 0.7f);
            for (var index = 0; index < BoltParticleCount; index++)
            {
                var angle = index * (2f * Mathf.PI / BoltParticleCount);
                var radial = screenRotation * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var variation = index % 3;
                var particle = new ParticleSystem.EmitParams
                {
                    position = origin + radial * (diameter * 0.46f),
                    velocity = (towardsCamera * 0.35f + radial * (2f + variation * 0.28f)) * diameter,
                    startLifetime = 0.18f + variation * 0.02f,
                    startSize = (0.09f + variation * 0.018f) * diameter,
                    startColor = color,
                    rotation = 45f
                };
                _boltParticles.Emit(particle, 1);
            }
        }

        private void EmitCloseParticles(SpriteRenderer lid)
        {
            var lidTransform = lid.transform;
            var bounds = lid.sprite.bounds;
            // The visible lid sits above the midpoint of its padded sprite.
            var outlineCenter = bounds.center + Vector3.up * (bounds.size.y * 0.085f);
            var worldCenter = lidTransform.TransformPoint(outlineCenter);
            var width = lidTransform.TransformVector(Vector3.right * bounds.size.x).magnitude;
            var color = Color.Lerp(lid.color, new Color(1f, 0.96f, 0.78f), 0.7f);
            var renderer = _closeParticles.GetComponent<ParticleSystemRenderer>();
            renderer.gameObject.layer = lid.gameObject.layer;
            renderer.sharedMaterial = lid.sharedMaterial;
            renderer.sortingLayerID = lid.sortingLayerID;
            renderer.sortingOrder = lid.sortingOrder + 1;

            _closeParticles.transform.position = worldCenter;
            _closeParticles.Play(false);
            for (var index = 0; index < CloseParticleCount; index++)
            {
                var angle = index * (2f * Mathf.PI / CloseParticleCount);
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var edgeScale = 0.29f / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
                var position = lidTransform.TransformPoint(outlineCenter + Vector3.Scale(direction, bounds.size) * edgeScale);
                var variation = index % 3;
                var particle = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = (position - worldCenter).normalized * (width * (0.45f + variation * 0.09f)),
                    startLifetime = 0.16f + variation * 0.0275f,
                    startSize = (0.024f + variation * 0.006f) * width,
                    startColor = color,
                    rotation = 45f
                };
                _closeParticles.Emit(particle, 1);
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
