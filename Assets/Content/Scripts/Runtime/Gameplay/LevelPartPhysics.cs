using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Screw.Levels;
using UnityEngine;

namespace Screw.Gameplay
{
    public sealed class LevelPartPhysics : IDisposable
    {
        private readonly Dictionary<int, (Rigidbody Body, Renderer[] Renderers, Collider[] Colliders)> _parts
            = new Dictionary<int, (Rigidbody Body, Renderer[] Renderers, Collider[] Colliders)>();
        private readonly HashSet<int> _released = new HashSet<int>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly CancellationToken _lifetimeToken;
        private readonly int _releasedLayer;
        private readonly Camera _camera;
        private readonly Transform _ground;
        private readonly BoxCollider _groundCollider;
        private readonly float _removalMargin;

        public LevelPartPhysics(LevelView view, Transform ground, Camera camera, float removalMargin)
        {
            if (removalMargin <= 0f || float.IsNaN(removalMargin) || float.IsInfinity(removalMargin))
            {
                throw new ArgumentOutOfRangeException(nameof(removalMargin));
            }

            _camera = camera;
            _ground = ground;
            _groundCollider = ground.GetComponent<BoxCollider>();
            _removalMargin = removalMargin;
            _lifetimeToken = _lifetime.Token;
            var attachedLayer = RequireLayer("AttachedParts");
            _releasedLayer = RequireLayer("ReleasedParts");
            var groundLayer = RequireLayer("LevelGround");
            for (var layer = 0; layer < 32; layer++)
            {
                Physics.IgnoreLayerCollision(_releasedLayer, layer,
                    layer != _releasedLayer && layer != groundLayer && layer != attachedLayer);
            }

            SetLayer(ground, groundLayer);
            foreach (var part in view.Parts)
            {
                var renderers = part.Node.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    throw new InvalidOperationException("Part has no visible geometry: " + part.Id);
                }

                SetLayer(part.Node, attachedLayer);
                var body = part.Node.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                _parts.Add(part.Id, (body, renderers, part.Node.GetComponentsInChildren<Collider>()));
            }
        }

        public async UniTask ReleaseAsync(int partId, CancellationToken cancellationToken)
        {
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken))
            {
                var token = cancellation.Token;
                token.ThrowIfCancellationRequested();
                var part = _parts[partId];
                var body = part.Body;
                if (!_released.Add(partId))
                {
                    throw new InvalidOperationException("Part has already been released: " + partId);
                }

                body.transform.SetParent(null, true);
                SetLayer(body.transform, _releasedLayer);
                Physics.SyncTransforms();
                body.isKinematic = false;
                body.useGravity = true;
                body.ResetCenterOfMass();
                body.ResetInertiaTensor();
                body.WakeUp();
                if (!IsBelowRemoval(part.Renderers, part.Colliders))
                {
                    await UniTask.WaitUntil(() => IsBelowRemoval(part.Renderers, part.Colliders),
                        timing: PlayerLoopTiming.LastPostLateUpdate, cancellationToken: token);
                }

                token.ThrowIfCancellationRequested();
                UnityEngine.Object.Destroy(body.gameObject);
                await UniTask.WaitUntil(() => body == null, cancellationToken: token);
            }
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private bool IsBelowRemoval(Renderer[] renderers, Collider[] colliders)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            foreach (var collider in colliders)
            {
                bounds.Encapsulate(collider.bounds);
            }

            var depth = Mathf.Max(_camera.nearClipPlane, bounds.max.z - _camera.transform.position.z);
            var removalY = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0f, depth)).y - _removalMargin;
            var groundTop = removalY - bounds.size.y - _removalMargin;
            var currentTop = _ground.TransformPoint(_groundCollider.center + Vector3.up * (_groundCollider.size.y * 0.5f)).y;
            if (currentTop > groundTop)
            {
                _ground.position += Vector3.up * (groundTop - currentTop);
            }

            return bounds.max.y < removalY;
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.gameObject.layer = layer;
            }
        }

        private static int RequireLayer(string name)
        {
            var layer = LayerMask.NameToLayer(name);
            if (layer < 0)
            {
                throw new InvalidOperationException("Required physics layer is missing: " + name);
            }

            return layer;
        }
    }
}
