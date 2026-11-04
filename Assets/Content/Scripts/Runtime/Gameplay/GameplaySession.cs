using System;
using Cysharp.Threading.Tasks;
using Screw.Bootstrap;
using Screw.Levels;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Screw.Gameplay
{
    public sealed class GameplaySession : MonoBehaviour
    {
        [SerializeField] private Button _restartButton;
        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private Text _resultLabel;
        [SerializeField] private Button _retryButton;

        private TurnCoordinator _coordinator;
        private BoltSelection _selection;
        private SceneNavigator _navigator;
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
        public void Construct(TurnCoordinator coordinator, BoltSelection selection, SceneNavigator navigator)
        {
            _coordinator = coordinator;
            _selection = selection;
            _navigator = navigator;
        }

        private void Start()
        {
            _started = true;
            _resultPanel.SetActive(false);
            Subscribe();
        }

        private void OnEnable()
        {
            if (_started)
            {
                Subscribe();
            }
        }

        private void OnDisable()
        {
            if (_started)
            {
                _selection.Selected -= OnSelected;
                _restartButton.onClick.RemoveListener(Restart);
                _retryButton.onClick.RemoveListener(Restart);
            }
        }

        public async UniTask PlayAsync(int boltId)
        {
            await _coordinator.PlayAsync(boltId);
            if (Attempt.Status == AttemptStatus.Active)
            {
                return;
            }

            _resultLabel.text = Attempt.Status == AttemptStatus.Won ? "Победа!" : "Нет места";
            _resultPanel.SetActive(true);
            _restartButton.gameObject.SetActive(false);
        }

        private void Subscribe()
        {
            _selection.Selected += OnSelected;
            _restartButton.onClick.AddListener(Restart);
            _retryButton.onClick.AddListener(Restart);
        }

        private void Restart()
        {
            _restartButton.interactable = false;
            _retryButton.interactable = false;
            enabled = false;
            _coordinator.Dispose();
            _navigator.RestartGameplay();
        }

        private void OnSelected(LevelBoltBinding bolt)
        {
            PlayAsync(bolt.Id).Forget(exception =>
            {
                if (!(exception is OperationCanceledException))
                {
                    Debug.LogException(exception, this);
                }
            });
        }
    }
}
