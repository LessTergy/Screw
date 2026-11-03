using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Screw.Levels
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform _levelRoot;
        [SerializeField] private Vector2 _startAngles = new Vector2(0f, 15f);
        [SerializeField, Min(1f)] private float _framingPadding = 1.15f;
        [SerializeField, Min(0f)] private float _degreesPerScreen = 180f;
        [SerializeField, Min(1f)] private float _dragThresholdPixels = 10f;

        private readonly PointerGesture _gesture = new PointerGesture();
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        private Camera _camera;
        private Vector3 _pivot;
        private float _radius;
        private float _distance;
        private float _yaw;
        private float _pitch;
        private float _aspect;
        private float _fieldOfView;
        private bool _usingTouch;
        private bool _suppressTouches;
        private EventSystem _uiEventSystem;
        private PointerEventData _uiPointer;

        public event Action<Vector2> Tapped;

        public Transform LevelRoot
        {
            get { return _levelRoot; }
        }

        public Vector3 Pivot
        {
            get { return _pivot; }
        }

        public float Distance
        {
            get { return _distance; }
        }

        public float Yaw
        {
            get { return _yaw; }
        }

        public float Pitch
        {
            get { return _pitch; }
        }

        private void Start()
        {
            FrameLevel();
        }

        public void FrameLevel()
        {
            _camera = GetComponent<Camera>();
            if (_camera.orthographic)
            {
                throw new InvalidOperationException("Orbit camera requires perspective projection.");
            }

            var renderers = _levelRoot.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException("Orbit camera requires level renderers.");
            }

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            _pivot = bounds.center;
            _radius = bounds.extents.magnitude;
            ResetOrbit();
        }

        public void Rotate(Vector2 deltaDegrees)
        {
            _yaw = Mathf.Repeat(_yaw + deltaDegrees.x, 360f);
            _pitch = Mathf.Repeat(_pitch + deltaDegrees.y, 360f);
            ApplyPose();
        }

        public void ResetOrbit()
        {
            _gesture.Cancel();
            _yaw = Mathf.Repeat(_startAngles.x, 360f);
            _pitch = Mathf.Repeat(_startAngles.y, 360f);
            UpdateDistance();
            ApplyPose();
        }

        private void Update()
        {
            var touchCount = Input.touchCount;
            if (touchCount > 0)
            {
                _usingTouch = true;
                if (touchCount != 1)
                {
                    _suppressTouches = true;
                    _gesture.Cancel();
                    return;
                }

                if (_suppressTouches)
                {
                    return;
                }

                var touch = Input.GetTouch(0);
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        BeginPointer(touch.fingerId, touch.position);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        UpdatePointer(touch.fingerId, touch.position, false);
                        break;
                    case TouchPhase.Ended:
                        UpdatePointer(touch.fingerId, touch.position, true);
                        break;
                    case TouchPhase.Canceled:
                        _gesture.Cancel();
                        break;
                }

                return;
            }

            if (_usingTouch)
            {
                _gesture.Cancel();
                _usingTouch = false;
            }

            _suppressTouches = false;
            Vector2 mousePosition = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                BeginPointer(-1, mousePosition);
            }

            if (Input.GetMouseButtonUp(0))
            {
                UpdatePointer(-1, mousePosition, true);
            }
            else if (Input.GetMouseButton(0))
            {
                UpdatePointer(-1, mousePosition, false);
            }
            else
            {
                _gesture.Cancel();
            }
        }

        private void LateUpdate()
        {
            if (_camera.aspect != _aspect || _camera.fieldOfView != _fieldOfView)
            {
                UpdateDistance();
                ApplyPose();
            }
        }

        private void OnDisable()
        {
            _gesture.Cancel();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                _gesture.Cancel();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _gesture.Cancel();
            }
        }

        public void BeginPointer(int pointerId, Vector2 position)
        {
            _gesture.Begin(pointerId, position, _dragThresholdPixels, IsPointerBlocked(pointerId, position));
        }

        public void UpdatePointer(int pointerId, Vector2 position, bool released)
        {
            if (!_gesture.IsActive)
            {
                return;
            }

            var blocked = IsPointerBlocked(pointerId, position);
            var delta = _gesture.Move(pointerId, position, blocked);
            if (delta != Vector2.zero)
            {
                Drag(delta);
            }

            if (released && _gesture.End(pointerId, position, blocked))
            {
                Tapped?.Invoke(position);
            }
        }

        private bool IsPointerBlocked(int pointerId, Vector2 position)
        {
            if (!_camera.pixelRect.Contains(position))
            {
                return true;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            if (_uiEventSystem != eventSystem)
            {
                _uiEventSystem = eventSystem;
                _uiPointer = new PointerEventData(eventSystem);
            }

            _uiPointer.Reset();
            _uiPointer.pointerId = pointerId;
            _uiPointer.position = position;
            _uiHits.Clear();
            eventSystem.RaycastAll(_uiPointer, _uiHits);
            foreach (var hit in _uiHits)
            {
                if (hit.isValid && !(hit.module is PhysicsRaycaster))
                {
                    return true;
                }
            }

            return false;
        }

        private void Drag(Vector2 deltaPixels)
        {
            var screenSize = Mathf.Max(1, Mathf.Min(_camera.pixelWidth, _camera.pixelHeight));
            Rotate(-deltaPixels * (_degreesPerScreen / screenSize));
        }

        private void UpdateDistance()
        {
            _aspect = _camera.aspect;
            _fieldOfView = _camera.fieldOfView;
            var verticalHalfAngle = _fieldOfView * Mathf.Deg2Rad * 0.5f;
            var horizontalHalfAngle = Mathf.Atan(Mathf.Tan(verticalHalfAngle) * _aspect);
            var fitDistance = _radius * _framingPadding / Mathf.Sin(Mathf.Min(verticalHalfAngle, horizontalHalfAngle));
            _distance = Mathf.Max(fitDistance, _radius + _camera.nearClipPlane);
        }

        private void ApplyPose()
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_pivot + rotation * (Vector3.back * _distance), rotation);
        }
    }
}
