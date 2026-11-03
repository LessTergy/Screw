using System;
using NUnit.Framework;
using Screw.Levels;
using UnityEngine;

namespace Screw.Tests.Levels
{
    public sealed class PointerGestureTests
    {
        private const float Threshold = 10f;
        private const int FingerId = 7;
        private PointerGesture _gesture;

        [SetUp]
        public void SetUp()
        {
            _gesture = new PointerGesture();
        }

        [TestCase(-1)]
        [TestCase(FingerId)]
        public void ReleaseWithoutDraggingProducesOneTapForMouseOrTouch(int pointerId)
        {
            _gesture.Begin(pointerId, Vector2.zero, Threshold, false);
            Assert.That(_gesture.Move(pointerId, Vector2.one, false), Is.EqualTo(Vector2.zero));
            Assert.That(_gesture.End(pointerId, Vector2.one, false), Is.True);
            Assert.That(_gesture.IsActive, Is.False);
            Assert.That(_gesture.End(pointerId, Vector2.one, false), Is.False);
        }

        [TestCase(9.99f, false)]
        [TestCase(10f, true)]
        [TestCase(10.01f, true)]
        [TestCase(-10f, true)]
        public void DragStartsAtTheThresholdInEitherDirection(float distance, bool dragging)
        {
            var position = new Vector2(distance, 0f);
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            var delta = _gesture.Move(FingerId, position, false);

            Assert.That(_gesture.IsDragging, Is.EqualTo(dragging));
            Assert.That(delta, Is.EqualTo(dragging ? position : Vector2.zero));
            Assert.That(_gesture.End(FingerId, position, false), Is.EqualTo(!dragging));
        }

        [TestCase(6f, 8f, true)]
        [TestCase(5.9f, 8f, false)]
        public void ThresholdUsesTwoDimensionalDistance(float x, float y, bool dragging)
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            _gesture.Move(FingerId, new Vector2(x, y), false);
            Assert.That(_gesture.IsDragging, Is.EqualTo(dragging));
        }

        [Test]
        public void DragAccumulatesFromThePressThenReportsOnlyNewMovement()
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            Assert.That(_gesture.Move(FingerId, new Vector2(6f, 0f), false), Is.EqualTo(Vector2.zero));
            Assert.That(_gesture.Move(FingerId, new Vector2(12f, 0f), false), Is.EqualTo(new Vector2(12f, 0f)));
            Assert.That(_gesture.Move(FingerId, new Vector2(15f, 0f), false), Is.EqualTo(new Vector2(3f, 0f)));
            Assert.That(_gesture.Move(FingerId, Vector2.zero, false), Is.EqualTo(new Vector2(-15f, 0f)));
            Assert.That(_gesture.End(FingerId, Vector2.zero, false), Is.False);
        }

        [Test]
        public void ReleaseAcrossTheThresholdCannotBecomeATapWithoutAMoveSample()
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            Assert.That(_gesture.End(FingerId, new Vector2(15f, 0f), false), Is.False);
        }

        [Test]
        public void CanceledPressCannotTapAndANewPressCanStart()
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            _gesture.Cancel();
            Assert.That(_gesture.Move(FingerId, Vector2.one, false), Is.EqualTo(Vector2.zero));
            Assert.That(_gesture.End(FingerId, Vector2.one, false), Is.False);

            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            Assert.That(_gesture.End(FingerId, Vector2.zero, false), Is.True);
        }

        [Test]
        public void PressStartedOverUiCannotResumeOutsideUi()
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, true);
            Assert.That(_gesture.IsActive, Is.False);
            Assert.That(_gesture.Move(FingerId, new Vector2(20f, 0f), false), Is.EqualTo(Vector2.zero));
            Assert.That(_gesture.End(FingerId, Vector2.zero, false), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EnteringUiCancelsBothPendingPressAndDragUntilANewPress(bool dragging)
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            _gesture.Move(FingerId, new Vector2(dragging ? 12f : 2f, 0f), false);
            Assert.That(_gesture.Move(FingerId, new Vector2(20f, 0f), true), Is.EqualTo(Vector2.zero));
            Assert.That(_gesture.IsActive, Is.False);
            Assert.That(_gesture.Move(FingerId, Vector2.zero, false), Is.EqualTo(Vector2.zero));
            Assert.That(_gesture.End(FingerId, Vector2.zero, false), Is.False);
        }

        [Test]
        public void ReleaseOverUiDoesNotTap()
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            Assert.That(_gesture.End(FingerId, Vector2.zero, true), Is.False);
        }

        [Test]
        public void DifferentPointerCannotContinueOrFinishTheGesture()
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            Assert.That(_gesture.Move(FingerId + 1, new Vector2(20f, 0f), false), Is.EqualTo(Vector2.zero));
            Assert.That(_gesture.IsActive, Is.False);
            Assert.That(_gesture.End(FingerId, Vector2.zero, false), Is.False);

            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            Assert.That(_gesture.End(FingerId + 1, Vector2.zero, false), Is.False);
        }

        [Test]
        public void NewPressDiscardsThePreviousDrag()
        {
            _gesture.Begin(FingerId, Vector2.zero, Threshold, false);
            _gesture.Move(FingerId, new Vector2(20f, 0f), false);
            _gesture.Begin(FingerId, Vector2.one, Threshold, false);

            Assert.That(_gesture.IsDragging, Is.False);
            Assert.That(_gesture.End(FingerId, Vector2.one, false), Is.True);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidThresholdIsRejected(float threshold)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _gesture.Begin(FingerId, Vector2.zero, threshold, false));
            Assert.That(_gesture.IsActive, Is.False);
        }

        [Test]
        public void MovementAndReleaseWithoutAPressAreIgnored()
        {
            Assert.That(_gesture.Move(FingerId, new Vector2(20f, 0f), false), Is.EqualTo(Vector2.zero));
            Assert.That(_gesture.End(FingerId, Vector2.zero, false), Is.False);
        }
    }
}
