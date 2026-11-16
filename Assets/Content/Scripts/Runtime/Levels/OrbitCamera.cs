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
        private const float DragSmoothingRate = 28f;
        private const float InertiaDecayRate = 10f;
        private const float MaxInertiaSpeed = 360f;
        private const float MinInertiaSpeed = 1f;
        private const float ReleasePauseSeconds = 0.07f;
        private const float HoldDragScale = 0.2f;

        [SerializeField] private Transform _levelRoot;
        [SerializeField] private Vector2 _startAngles = new Vector2(0f, -15f);
        [SerializeField, Min(1f)] private float _framingPadding = 1.15f;
        [SerializeField, Min(0f)] private float _degreesPerScreen = 180f;
        [SerializeField, Min(1f)] private float _dragThresholdPixels = 10f;

        private readonly PointerGesture _gesture = new PointerGesture();
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private readonly PartPreview _partPreview = new PartPreview();

        private Camera _camera;
        private Func<Vector2, Transform> _partAtPointer;
        private Transform _pressedPart;
        private Vector3 _pivot;
        private float _radius;
        private float _distance;
        private float _yaw;
        private float _pitch;
        private Vector2 _pendingDrag;
        private Vector2 _angularVelocity;
        private float _stationaryTime;
        private float _aspect;
        private float _fieldOfView;
        private bool _isFramed;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private bool _usingTouch;
        private bool _suppressTouches;
        private EventSystem _uiEventSystem;
        private PointerEventData _uiPointer;

        public event Action<Vector2> Tapped;

        internal Func<Vector2, Transform> PartAtPointer
        {
            get { return _partAtPointer; }
            set
            {
                CancelPointer();
                _partAtPointer = value;
            }
        }

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

        internal void SetLevelRoot(Transform levelRoot)
        {
            CancelPointer();
            _levelRoot = levelRoot;
            _isFramed = false;
        }

        public void FrameLevel()
        {
            _camera = GetComponent<Camera>();
            if (_camera.orthographic)
            {
                throw new InvalidOperationException("Orbit camera requires perspective projection.");
            }

            if (!_isFramed)
            {
                var renderers = _levelRoot.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    throw new InvalidOperationException("Level orbit requires level renderers.");
                }

                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                }

                _pivot = bounds.center;
                _radius = bounds.extents.magnitude;
                _initialPosition = _levelRoot.position;
                _initialRotation = _levelRoot.rotation;
                _isFramed = true;
            }

            ResetOrbit();
        }

        public void Rotate(Vector2 deltaDegrees)
        {
            StopRotation();
            ApplyRotation(deltaDegrees);
        }

        private void ApplyRotation(Vector2 deltaDegrees)
        {
            _yaw = Mathf.Repeat(_yaw + deltaDegrees.x, 360f);
            _pitch = Mathf.Repeat(_pitch + deltaDegrees.y, 360f);
            ApplyPose();
        }

        public void ResetOrbit()
        {
            CancelPointer();
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
                    CancelPointer();
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
                        CancelPointer();
                        break;
                }

                return;
            }

            if (_usingTouch && _gesture.IsActive)
            {
                CancelPointer();
            }

            _usingTouch = false;
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
            else if (_gesture.IsActive)
            {
                CancelPointer();
            }
            else
            {
                AdvanceInertia(Time.unscaledDeltaTime);
            }
        }

        private void LateUpdate()
        {
            if (_gesture.IsHolding && !CanPreviewPressedPart())
            {
                CancelPointer();
            }

            if (_camera.aspect != _aspect || _camera.fieldOfView != _fieldOfView)
            {
                UpdateDistance();
            }
        }

        private void OnDisable()
        {
            CancelPointer();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                CancelPointer();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                CancelPointer();
            }
        }

        public void BeginPointer(int pointerId, Vector2 position)
        {
            CancelPointer();
            _gesture.Begin(pointerId, position, _dragThresholdPixels, IsPointerBlocked(pointerId, position));
            if (_gesture.IsActive)
            {
                _pressedPart = _partAtPointer?.Invoke(position);
            }
        }

        public void UpdatePointer(int pointerId, Vector2 position, bool released)
        {
            UpdatePointer(pointerId, position, released, Time.unscaledDeltaTime);
        }

        private void UpdatePointer(int pointerId, Vector2 position, bool released, float deltaTime)
        {
            if (!_gesture.IsActive)
            {
                return;
            }

            var blocked = IsPointerBlocked(pointerId, position);
            var delta = _gesture.Move(pointerId, position, blocked);
            if (!_gesture.IsActive || (_gesture.IsHolding && !CanPreviewPressedPart()))
            {
                CancelPointer();
                return;
            }

            if (_gesture.IsHolding)
            {
                if (!released)
                {
                    Drag(delta * HoldDragScale, deltaTime);
                }
            }
            else if (_gesture.IsDragging && (!released || delta != Vector2.zero))
            {
                Drag(delta, deltaTime);
            }
            else if (!released && CanPreviewPressedPart() && _gesture.TryHold(deltaTime))
            {
                _partPreview.Show(_pressedPart);
            }

            if (!released)
            {
                return;
            }

            _pendingDrag = Vector2.zero;
            if (_gesture.IsHolding || _stationaryTime >= ReleasePauseSeconds)
            {
                _angularVelocity = Vector2.zero;
            }

            var tapped = _gesture.End(pointerId, position, blocked);
            ClearPartPreview();
            if (tapped)
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

        private void Drag(Vector2 deltaPixels, float deltaTime)
        {
            var screenSize = Mathf.Max(1, Mathf.Min(_camera.pixelWidth, _camera.pixelHeight));
            var deltaDegrees = new Vector2(-deltaPixels.x, deltaPixels.y) * (_degreesPerScreen / screenSize);
            _pendingDrag += deltaDegrees;
            if (deltaTime <= 0f)
            {
                return;
            }

            if (deltaPixels == Vector2.zero)
            {
                _stationaryTime += deltaTime;
            }
            else
            {
                _stationaryTime = 0f;
                _angularVelocity = Vector2.ClampMagnitude(deltaDegrees / deltaTime, MaxInertiaSpeed);
            }

            if (_pendingDrag != Vector2.zero)
            {
                var step = _pendingDrag * (1f - Mathf.Exp(-DragSmoothingRate * deltaTime));
                _pendingDrag -= step;
                ApplyRotation(step);
            }
        }

        private void AdvanceInertia(float deltaTime)
        {
            if (_angularVelocity.sqrMagnitude < MinInertiaSpeed * MinInertiaSpeed)
            {
                _angularVelocity = Vector2.zero;
                return;
            }

            var decay = Mathf.Exp(-InertiaDecayRate * deltaTime);
            ApplyRotation(_angularVelocity * ((1f - decay) / InertiaDecayRate));
            _angularVelocity *= decay;
        }

        private void CancelPointer()
        {
            _gesture.Cancel();
            StopRotation();
            ClearPartPreview();
        }

        private void StopRotation()
        {
            _pendingDrag = Vector2.zero;
            _angularVelocity = Vector2.zero;
            _stationaryTime = 0f;
        }

        private bool CanPreviewPressedPart()
        {
            return _pressedPart != null && _pressedPart.gameObject.activeInHierarchy && _pressedPart.IsChildOf(_levelRoot);
        }

        private void ClearPartPreview()
        {
            _partPreview.Clear();
            _pressedPart = null;
        }

        private void UpdateDistance()
        {
            _aspect = _camera.aspect;
            _fieldOfView = _camera.fieldOfView;
            var verticalHalfAngle = _fieldOfView * Mathf.Deg2Rad * 0.5f;
            var horizontalHalfAngle = Mathf.Atan(Mathf.Tan(verticalHalfAngle) * _aspect);
            var fitDistance = _radius * _framingPadding / Mathf.Sin(Mathf.Min(verticalHalfAngle, horizontalHalfAngle));
            _distance = Mathf.Max(fitDistance, _radius + _camera.nearClipPlane);
            transform.SetPositionAndRotation(_pivot + Vector3.back * _distance, Quaternion.identity);
        }

        private void ApplyPose()
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _levelRoot.SetPositionAndRotation(_pivot + rotation * (_initialPosition - _pivot), rotation * _initialRotation);
        }
    }
}
