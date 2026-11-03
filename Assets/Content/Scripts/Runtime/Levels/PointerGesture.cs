using System;
using UnityEngine;

namespace Screw.Levels
{
    public sealed class PointerGesture
    {
        private bool _isActive;
        private bool _isDragging;
        private int _pointerId;
        private Vector2 _origin;
        private Vector2 _previousPosition;
        private float _thresholdSquared;

        public bool IsActive
        {
            get { return _isActive; }
        }

        public bool IsDragging
        {
            get { return _isDragging; }
        }

        public void Begin(int pointerId, Vector2 position, float dragThreshold, bool blocked)
        {
            Cancel();
            if (dragThreshold <= 0f || float.IsNaN(dragThreshold) || float.IsInfinity(dragThreshold))
            {
                throw new ArgumentOutOfRangeException(nameof(dragThreshold));
            }

            if (blocked)
            {
                return;
            }

            _pointerId = pointerId;
            _origin = position;
            _previousPosition = position;
            _thresholdSquared = dragThreshold * dragThreshold;
            _isActive = true;
        }

        public Vector2 Move(int pointerId, Vector2 position, bool blocked)
        {
            if (!_isActive)
            {
                return Vector2.zero;
            }

            if (pointerId != _pointerId || blocked)
            {
                Cancel();
                return Vector2.zero;
            }

            var delta = Vector2.zero;
            if (!_isDragging && (position - _origin).sqrMagnitude >= _thresholdSquared)
            {
                _isDragging = true;
                delta = position - _origin;
            }
            else if (_isDragging)
            {
                delta = position - _previousPosition;
            }

            _previousPosition = position;
            return delta;
        }

        public bool End(int pointerId, Vector2 position, bool blocked)
        {
            Move(pointerId, position, blocked);
            var tapped = _isActive && !_isDragging;
            Cancel();
            return tapped;
        }

        public void Cancel()
        {
            _isActive = false;
            _isDragging = false;
        }
    }
}
