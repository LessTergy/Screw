using System;
using NUnit.Framework;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Screw.Tests.Levels
{
    public sealed class OrbitCameraTests
    {
        private GameObject _level;
        private GameObject _cameraObject;
        private Renderer _renderer;
        private Camera _camera;
        private OrbitCamera _orbit;

        [SetUp]
        public void SetUp()
        {
            _level = new GameObject("OrbitLevelTest");
            _level.transform.position = new Vector3(3f, -2f, 1f);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(_level.transform, false);
            visual.transform.localPosition = new Vector3(1.5f, 1f, -2f);
            visual.transform.localScale = new Vector3(8f, 12f, 4f);
            _renderer = visual.GetComponent<Renderer>();
            _cameraObject = new GameObject("OrbitCameraTest", typeof(Camera), typeof(OrbitCamera));
            _camera = _cameraObject.GetComponent<Camera>();
            _camera.fieldOfView = 60f;
            _camera.aspect = 9f / 16f;
            _orbit = _cameraObject.GetComponent<OrbitCamera>();
            using (var serialized = new SerializedObject(_orbit))
            {
                serialized.FindProperty("_levelRoot").objectReferenceValue = _level.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_cameraObject != null)
            {
                Object.DestroyImmediate(_cameraObject);
            }

            if (_level != null)
            {
                Object.DestroyImmediate(_level);
            }
        }

        [Test]
        public void FrameLevelRotatesAroundTheBoundsCenterAndPreservesLocalGeometryAndGravity()
        {
            var position = _level.transform.position;
            var rotation = _level.transform.rotation;
            var scale = _level.transform.localScale;
            var gravity = Physics.gravity;
            var pivot = _renderer.bounds.center;
            var localPosition = _renderer.transform.localPosition;
            var localRotation = _renderer.transform.localRotation;
            _orbit.FrameLevel();

            Assert.That(Vector3.Distance(_orbit.Pivot, _renderer.bounds.center), Is.LessThan(0.0001f));
            Assert.That(_orbit.Yaw, Is.Zero);
            Assert.That(_orbit.Pitch, Is.EqualTo(345f));
            Assert.That(Vector3.Dot(_camera.transform.forward, (_orbit.Pivot - _camera.transform.position).normalized),
                Is.GreaterThan(0.9999f));
            var turn = Quaternion.Euler(-15f, 0f, 0f);
            Assert.That(Vector3.Distance(_level.transform.position, pivot + turn * (position - pivot)), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(_level.transform.rotation, turn * rotation), Is.LessThan(0.01f));
            Assert.That(_renderer.transform.localPosition, Is.EqualTo(localPosition));
            Assert.That(_renderer.transform.localRotation, Is.EqualTo(localRotation));
            Assert.That(_level.transform.localScale, Is.EqualTo(scale));
            Assert.That(Physics.gravity, Is.EqualTo(gravity));
        }

        [TestCase(765f, 1000f, 45f, 265f)]
        [TestCase(-765f, -1000f, 315f, 65f)]
        [TestCase(30f, -20f, 30f, 325f)]
        public void RotationWrapsBothAxesWithoutLimitingTheOrbit(float yawDelta, float pitchDelta, float yaw, float pitch)
        {
            _orbit.FrameLevel();
            var distance = _orbit.Distance;
            var cameraPosition = _camera.transform.position;
            var cameraRotation = _camera.transform.rotation;
            var levelRotation = _level.transform.rotation;
            var gravity = Physics.gravity;
            _orbit.Rotate(new Vector2(yawDelta, pitchDelta));

            Assert.That(_orbit.Yaw, Is.EqualTo(yaw).Within(0.0001f));
            Assert.That(_orbit.Pitch, Is.EqualTo(pitch).Within(0.0001f));
            Assert.That(_orbit.Distance, Is.EqualTo(distance));
            Assert.That(Vector3.Distance(_camera.transform.position, _orbit.Pivot), Is.EqualTo(distance).Within(0.0001f));
            Assert.That(_camera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(_camera.transform.rotation, Is.EqualTo(cameraRotation));
            Assert.That(Quaternion.Angle(_level.transform.rotation, levelRotation), Is.GreaterThan(1f));
            Assert.That(Physics.gravity, Is.EqualTo(gravity));
            Assert.That(Vector3.Distance(_renderer.bounds.center, _orbit.Pivot), Is.LessThan(0.0001f));
        }

        [TestCase(360f, 0f)]
        [TestCase(-360f, 0f)]
        [TestCase(0f, 360f)]
        [TestCase(0f, -360f)]
        public void FullTurnOnEitherAxisReturnsToTheSamePose(float yawDelta, float pitchDelta)
        {
            _orbit.FrameLevel();
            var position = _camera.transform.position;
            var rotation = _camera.transform.rotation;
            var levelPosition = _level.transform.position;
            var levelRotation = _level.transform.rotation;
            _orbit.Rotate(new Vector2(yawDelta, pitchDelta));

            Assert.That(Vector3.Distance(_camera.transform.position, position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(_camera.transform.rotation, rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(_level.transform.position, levelPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(_level.transform.rotation, levelRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void ResetRestoresTheInitialPoseAndDistance()
        {
            _orbit.FrameLevel();
            var position = _camera.transform.position;
            var rotation = _camera.transform.rotation;
            var distance = _orbit.Distance;
            var pivot = _orbit.Pivot;
            _orbit.Rotate(new Vector2(215f, 120f));
            _orbit.ResetOrbit();
            _orbit.ResetOrbit();

            Assert.That(_orbit.Yaw, Is.Zero);
            Assert.That(_orbit.Pitch, Is.EqualTo(345f));
            Assert.That(_orbit.Pivot, Is.EqualTo(pivot));
            Assert.That(_orbit.Distance, Is.EqualTo(distance));
            Assert.That(Vector3.Distance(_camera.transform.position, position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(_camera.transform.rotation, rotation), Is.LessThan(0.01f));
        }

        [TestCase(90f)]
        [TestCase(270f)]
        public void VerticalOrbitPassesThroughPolesWithoutFlippingAbruptly(float pole)
        {
            _orbit.FrameLevel();
            _orbit.Rotate(new Vector2(0f, pole - 1f - _orbit.Pitch));
            var rotation = _level.transform.rotation;
            _orbit.Rotate(new Vector2(0f, 2f));

            Assert.That(_orbit.Pitch, Is.EqualTo(pole + 1f).Within(0.0001f));
            Assert.That(Quaternion.Angle(_level.transform.rotation, rotation), Is.EqualTo(2f).Within(0.01f));
            Assert.That(Vector3.Dot(_camera.transform.forward, (_orbit.Pivot - _camera.transform.position).normalized),
                Is.GreaterThan(0.9999f));
        }

        [Test]
        public void StartingAnglesAndResetSupportAnInvertedPose()
        {
            using (var serialized = new SerializedObject(_orbit))
            {
                serialized.FindProperty("_startAngles").vector2Value = new Vector2(765f, 555f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            _orbit.FrameLevel();
            var position = _camera.transform.position;
            var rotation = _camera.transform.rotation;
            Assert.That(_orbit.Yaw, Is.EqualTo(45f));
            Assert.That(_orbit.Pitch, Is.EqualTo(195f));
            _orbit.Rotate(new Vector2(45f, 45f));
            _orbit.ResetOrbit();

            Assert.That(_orbit.Yaw, Is.EqualTo(45f));
            Assert.That(_orbit.Pitch, Is.EqualTo(195f));
            Assert.That(Vector3.Distance(_camera.transform.position, position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(_camera.transform.rotation, rotation), Is.LessThan(0.01f));
        }

        [TestCase(0.46153846f)]
        [TestCase(9f / 16f)]
        [TestCase(16f / 9f)]
        public void FramingKeepsBoundsInViewWithMarginThroughoutTheOrbit(float aspect)
        {
            _camera.aspect = aspect;
            _orbit.FrameLevel();
            var bounds = _renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
            foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
            {
                foreach (var pitch in new[] { 0f, 15f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
                {
                    _orbit.ResetOrbit();
                    _orbit.Rotate(new Vector2(yaw, pitch - _orbit.Pitch));
                    foreach (var x in new[] { bounds.min.x, bounds.max.x })
                    {
                        foreach (var y in new[] { bounds.min.y, bounds.max.y })
                        {
                            foreach (var z in new[] { bounds.min.z, bounds.max.z })
                            {
                                var point = _camera.WorldToViewportPoint(_renderer.transform.TransformPoint(new Vector3(x, y, z)));
                                Assert.That(point.x, Is.InRange(0.05f, 0.95f));
                                Assert.That(point.y, Is.InRange(0.05f, 0.95f));
                                Assert.That(point.z, Is.InRange(_camera.nearClipPlane, _camera.farClipPlane));
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void ResetRefitsTheCurrentAspectWithoutRecapturingTheLevelBounds()
        {
            _camera.aspect = 16f / 9f;
            _orbit.FrameLevel();
            var landscapeDistance = _orbit.Distance;
            var pivot = _orbit.Pivot;
            _level.transform.position += Vector3.right * 10f;
            _camera.aspect = 9f / 16f;
            _orbit.Rotate(new Vector2(90f, 30f));
            _orbit.ResetOrbit();

            Assert.That(_orbit.Distance, Is.GreaterThan(landscapeDistance));
            Assert.That(_orbit.Pivot, Is.EqualTo(pivot));
            Assert.That(_orbit.Yaw, Is.Zero);
            Assert.That(_orbit.Pitch, Is.EqualTo(345f));
        }

        [Test]
        public void ReframingAfterGeometryChangesKeepsTheOriginalCenterAndScale()
        {
            _orbit.FrameLevel();
            var pivot = _orbit.Pivot;
            var distance = _orbit.Distance;
            var position = _level.transform.position;
            var rotation = _level.transform.rotation;
            _renderer.transform.localPosition += Vector3.up * 3f;
            _renderer.transform.localScale *= 0.1f;
            _orbit.Rotate(new Vector2(75f, 135f));
            _orbit.FrameLevel();
            Assert.That(_orbit.Pivot, Is.EqualTo(pivot));
            Assert.That(_orbit.Distance, Is.EqualTo(distance));
            Assert.That(Vector3.Distance(_level.transform.position, position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(_level.transform.rotation, rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void FramingRejectsOrthographicProjection()
        {
            _camera.orthographic = true;
            Assert.Throws<InvalidOperationException>(() => _orbit.FrameLevel());
        }

        [Test]
        public void FramingRejectsAnEmptyLevel()
        {
            Object.DestroyImmediate(_renderer.gameObject);
            Assert.Throws<InvalidOperationException>(() => _orbit.FrameLevel());
        }
    }
}
