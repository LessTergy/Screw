using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Screw.Levels;
using UnityEngine;

namespace Screw.Editor.Levels
{
    public sealed class LevelRouteSession : IDisposable
    {
        private readonly LevelView _view;
        private readonly Camera _camera;
        private readonly OrbitCamera _input;
        private readonly BoltSelection _selection;
        private readonly LevelRouteData _route;
        private readonly LevelSourceData _source;
        private readonly HashSet<int> _removed = new HashSet<int>();
        private readonly float _originalAspect;
        private readonly float _originalFieldOfView;
        private readonly Vector2 _originalAngles;
        private bool _prepared;

        public int StepIndex { get; private set; }
        public string Message { get; private set; }
        public bool IsComplete
        {
            get { return StepIndex == _route.Steps.Length; }
        }

        public LevelRouteSession(LevelView view, Camera camera, BoltSelection selection, LevelRouteData route)
        {
            _source = view.ValidateBindings();
            route.Validate(_source);
            if (view.Parts.Any(part => !part.Node.gameObject.activeSelf)
                || view.Bolts.Any(bolt => !bolt.Node.gameObject.activeSelf))
            {
                throw new InvalidOperationException("Start the route with the complete level.");
            }

            _view = view;
            _camera = camera;
            _input = camera.GetComponent<OrbitCamera>();
            _selection = selection;
            _route = route;
            _originalAspect = camera.aspect;
            _originalFieldOfView = camera.fieldOfView;
            _originalAngles = new Vector2(_input.Yaw, _input.Pitch);
            camera.fieldOfView = route.Camera.FieldOfViewDegrees;
            camera.aspect = route.Camera.Aspect;
            _input.FrameLevel();
            _selection.Selected += OnSelected;
            Message = "Prepare the next step, then tap the expected head in Game view.";
        }

        public void PrepareNext()
        {
            if (IsComplete)
            {
                throw new InvalidOperationException("Route is already complete.");
            }

            _prepared = false;
            var step = _route.Steps[StepIndex];
            var rotation = _view.LevelRoot.rotation * Quaternion.Euler(LevelRouteData.Vector(step.CameraEulerDegrees));
            var angles = rotation.eulerAngles;
            _input.Rotate(new Vector2(angles.y - _input.Yaw, angles.x - _input.Pitch));
            var position = _view.LevelRoot.TransformPoint(LevelRouteData.Vector(step.CameraPosition));
            if (Vector3.Distance(position, _camera.transform.position) > 0.01f
                || Quaternion.Angle(rotation, _camera.transform.rotation) > 0.1f)
            {
                throw new InvalidDataException("Route pose is not reachable by the configured orbit at step " + StepIndex + ".");
            }

            _prepared = true;
            Message = "Step " + (StepIndex + 1) + ": tap bolt " + step.BoltId + ".";
        }

        public void ReplayNext()
        {
            PrepareNext();
            var previous = StepIndex;
            var point = _route.Steps[StepIndex].ViewportPoint;
            Vector2 screen = _camera.ViewportToScreenPoint(new Vector3(point[0], point[1], 0f));
            _input.BeginPointer(-1, screen);
            _input.UpdatePointer(-1, screen, true);
            if (StepIndex != previous + 1)
            {
                throw new InvalidDataException("Route input did not select the expected bolt. " + Message);
            }
        }

        private void OnSelected(LevelBoltBinding bolt)
        {
            if (!_prepared || IsComplete)
            {
                return;
            }

            var expected = _route.Steps[StepIndex].BoltId;
            if (bolt.Id != expected)
            {
                Message = "Expected bolt " + expected + ", selected " + bolt.Id + ". Nothing removed.";
                return;
            }

            _prepared = false;
            _removed.Add(bolt.Id);
            bolt.Node.gameObject.SetActive(false);
            var owner = _source.Parts.Single(part => part.Id == bolt.OwnerId);
            if (owner.BoltIds.All(_removed.Contains))
            {
                _view.GetPart(owner.Id).Node.gameObject.SetActive(false);
            }

            StepIndex++;
            Message = IsComplete ? "Route complete: all bolts and owners removed." : "Selected " + bolt.Id + ". Prepare the next step.";
        }

        public void Dispose()
        {
            _selection.Selected -= OnSelected;
            if (_view == null || _camera == null)
            {
                return;
            }

            foreach (var part in _view.Parts)
            {
                part.Node.gameObject.SetActive(true);
            }

            foreach (var bolt in _view.Bolts)
            {
                bolt.Node.gameObject.SetActive(true);
            }

            _camera.fieldOfView = _originalFieldOfView;
            _camera.aspect = _originalAspect;
            _input.FrameLevel();
            _input.Rotate(new Vector2(_originalAngles.x - _input.Yaw, _originalAngles.y - _input.Pitch));
        }
    }
}
