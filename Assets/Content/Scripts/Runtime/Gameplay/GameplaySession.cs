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
        [SerializeField] private Button _nextButton;

        private TurnCoordinator _coordinator;
        private BoltSelection _selection;
        private SceneNavigator _navigator;
        private CampaignProgress _campaign;
        private OrbitCamera _orbit;
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
        public void Construct(TurnCoordinator coordinator, BoltSelection selection, SceneNavigator navigator,
            CampaignProgress campaign, OrbitCamera orbit)
        {
            _coordinator = coordinator;
            _selection = selection;
            _navigator = navigator;
            _campaign = campaign;
            _orbit = orbit;
        }

        private void Start()
        {
            var pending = _campaign.PendingLevelNumber;
            if (pending.HasValue && pending.Value != Attempt.LevelNumber)
            {
                throw new InvalidOperationException("Loaded level does not match the pending campaign transition.");
            }

            _started = true;
            _resultPanel.SetActive(false);
            _nextButton.gameObject.SetActive(false);
            Subscribe();
            if (pending.HasValue)
            {
                _campaign.CompleteTransition();
            }
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
                _nextButton.onClick.RemoveListener(Next);
            }
        }

        public async UniTask PlayAsync(int boltId)
        {
            await _coordinator.PlayAsync(boltId);
            if (Attempt.Status == AttemptStatus.Active || _campaign.PendingLevelNumber.HasValue)
            {
                return;
            }

            _resultLabel.text = _campaign.IsComplete(Attempt.LevelNumber, Attempt.Status)
                ? "Кампания пройдена"
                : Attempt.Status == AttemptStatus.Won ? "Победа!" : "Нет места";
            _nextButton.gameObject.SetActive(_campaign.CanAdvance(Attempt.LevelNumber, Attempt.Status));
            _resultPanel.SetActive(true);
            _restartButton.gameObject.SetActive(false);
        }

        private void Subscribe()
        {
            _selection.Selected += OnSelected;
            _restartButton.onClick.AddListener(Restart);
            _retryButton.onClick.AddListener(Restart);
            _nextButton.onClick.AddListener(Next);
        }

        private void Restart()
        {
            if (_campaign.TryRetry(Attempt.LevelNumber))
            {
                ReloadLevel();
            }
        }

        private void Next()
        {
            if (_campaign.TryAdvance(Attempt.LevelNumber, Attempt.Status))
            {
                ReloadLevel();
            }
        }

        private void ReloadLevel()
        {
            var orbitEnabled = _orbit.enabled;
            _orbit.enabled = false;
            enabled = false;
            var started = false;
            try
            {
                started = _navigator.RestartGameplay();
            }
            finally
            {
                if (!started)
                {
                    _campaign.CancelTransition();
                    _orbit.enabled = orbitEnabled;
                    enabled = true;
                }
            }

            if (!started)
            {
                return;
            }

            _restartButton.interactable = false;
            _retryButton.interactable = false;
            _nextButton.interactable = false;
            _coordinator.Dispose();
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
