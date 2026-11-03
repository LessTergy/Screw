using System;
using Cysharp.Threading.Tasks;
using Screw.Levels;
using UnityEngine;
using VContainer;

namespace Screw.Gameplay
{
    public sealed class GameplaySession : MonoBehaviour
    {
        private TurnCoordinator _coordinator;
        private BoltSelection _selection;
        private bool _started;

        public TurnCoordinator Coordinator
        {
            get { return _coordinator; }
        }

        public LevelAttempt Attempt
        {
            get { return _coordinator.Attempt; }
        }

        [Inject]
        public void Construct(TurnCoordinator coordinator, BoltSelection selection)
        {
            _coordinator = coordinator;
            _selection = selection;
        }

        private void Start()
        {
            _started = true;
            _selection.Selected += OnSelected;
        }

        private void OnEnable()
        {
            if (_started)
            {
                _selection.Selected += OnSelected;
            }
        }

        private void OnDisable()
        {
            if (_started)
            {
                _selection.Selected -= OnSelected;
            }
        }

        private void OnSelected(LevelBoltBinding bolt)
        {
            _coordinator.PlayAsync(bolt.Id).Forget(exception =>
            {
                if (!(exception is OperationCanceledException))
                {
                    Debug.LogException(exception, this);
                }
            });
        }
    }
}
