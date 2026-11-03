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
        private readonly Dictionary<Collider, LevelBoltBinding> _bolts = new Dictionary<Collider, LevelBoltBinding>();

        public event Action<LevelBoltBinding> Selected;

        public BoltSelection(LevelView view, Camera camera)
        {
            _camera = camera;
            _input = camera.GetComponent<OrbitCamera>();
            _physicsScene = view.gameObject.scene.GetPhysicsScene();
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
                        && (Physics.DefaultRaycastLayers & (1 << collider.gameObject.layer)) != 0)
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
            _input.Tapped += OnTapped;
        }

        public void Dispose()
        {
            _input.Tapped -= OnTapped;
        }

        public bool TrySelect(Vector2 screenPosition, out LevelBoltBinding bolt)
        {
            bolt = null;
            if (!_camera.pixelRect.Contains(screenPosition))
            {
                return false;
            }

            var ray = _camera.ScreenPointToRay(screenPosition);
            var distance = (_camera.farClipPlane - _camera.nearClipPlane)
                / Vector3.Dot(ray.direction, _camera.transform.forward);
            Physics.SyncTransforms();
            if (!_physicsScene.Raycast(ray.origin, ray.direction, out var hit, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (!_bolts.TryGetValue(hit.collider, out var candidate)
                || Vector3.Dot(candidate.Node.forward, -ray.direction) <= FacingTolerance)
            {
                return false;
            }

            bolt = candidate;
            Selected?.Invoke(candidate);
            return true;
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
