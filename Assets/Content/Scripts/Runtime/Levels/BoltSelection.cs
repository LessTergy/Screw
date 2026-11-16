using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

namespace Screw.Levels
{
    public sealed class BoltSelection : IStartable, IDisposable
    {
        private const float FacingTolerance = 0.0001f;
        private readonly Camera _camera;
        private readonly OrbitCamera _input;
        private readonly PhysicsScene _physicsScene;
        private readonly Transform _modelRoot;
        private readonly int _raycastLayers = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("LevelGround");
        private readonly Dictionary<Collider, LevelBoltBinding> _bolts = new Dictionary<Collider, LevelBoltBinding>();

        public event Action<LevelBoltBinding> Selected;

        public BoltSelection(LevelView view, Camera camera)
        {
            _camera = camera;
            _input = camera.GetComponent<OrbitCamera>();
            _physicsScene = view.gameObject.scene.GetPhysicsScene();
            _modelRoot = view.ModelRoot;
            foreach (var bolt in view.Bolts)
            {
                _bolts.Add(bolt.Node.GetComponent<Collider>(), bolt);
            }

            foreach (var part in view.Parts)
            {
                var hasBlocker = false;
                foreach (var collider in part.Node.GetComponentsInChildren<Collider>())
                {
                    if (collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger
                        && (_raycastLayers & (1 << collider.gameObject.layer)) != 0)
                    {
                        hasBlocker = true;
                        break;
                    }
                }

                if (!hasBlocker)
                {
                    throw new InvalidOperationException("Missing enabled blocking collider for part " + part.Id + ".");
                }
            }
        }

        public void Start()
        {
            _input.PartAtPointer = FindPart;
            _input.Tapped += OnTapped;
        }

        public void Dispose()
        {
            _input.Tapped -= OnTapped;
            if (_input.PartAtPointer == FindPart)
            {
                _input.PartAtPointer = null;
            }
        }

        public bool TrySelect(Vector2 screenPosition, out LevelBoltBinding bolt)
        {
            bolt = null;
            if (!TryRaycast(screenPosition, out var hit, out var rayDirection)
                || !_bolts.TryGetValue(hit.collider, out var candidate)
                || Vector3.Dot(candidate.Node.forward, -rayDirection) <= FacingTolerance)
            {
                return false;
            }

            bolt = candidate;
            Selected?.Invoke(candidate);
            return true;
        }

        private Transform FindPart(Vector2 screenPosition)
        {
            if (TryRaycast(screenPosition, out var hit, out _))
            {
                var node = hit.collider.transform;
                while (node.parent != null)
                {
                    if (node.parent == _modelRoot)
                    {
                        return node;
                    }

                    node = node.parent;
                }
            }

            return null;
        }

        private bool TryRaycast(Vector2 screenPosition, out RaycastHit hit, out Vector3 direction)
        {
            hit = default;
            direction = default;
            if (!_camera.pixelRect.Contains(screenPosition))
            {
                return false;
            }

            var ray = _camera.ScreenPointToRay(screenPosition);
            direction = ray.direction;
            var distance = (_camera.farClipPlane - _camera.nearClipPlane)
                / Vector3.Dot(ray.direction, _camera.transform.forward);
            Physics.SyncTransforms();
            return _physicsScene.Raycast(ray.origin, ray.direction, out hit, distance,
                _raycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void OnTapped(Vector2 screenPosition)
        {
            if (TrySelect(screenPosition, out var bolt))
            {
                Debug.Log("Selected bolt " + bolt.Id + " (owner " + bolt.OwnerId + ").", bolt.Node);
            }
        }
    }
}
