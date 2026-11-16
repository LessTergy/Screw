using System;
using Cysharp.Threading.Tasks;
using Screw.Bootstrap;
using Screw.Levels;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VContainer;

namespace Screw.Gameplay
{
    public sealed class GameplaySession : MonoBehaviour
    {
        [SerializeField] private Text _levelLabel;
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
        private CollectionView _collection;
        private ScreenUi _screenUi;
        private Button _pauseButton;
        private Button _resumeButton;
        private Button _menuButton;
        private Image _pauseOverlay;
        private Button _extraSlotButton;
        private CanvasGroup _extraSlotTile;
        private Text _extraSlotCount;
        private RectTransform _rewardPanel;
        private Text _rewardLabel;
        private CoinRewardAnimation _coinReward;
        private float _previousTimeScale;
        private bool _previousAudioPause;
        private bool _previousOrbitEnabled;
        private bool _paused;
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
            CampaignProgress campaign, OrbitCamera orbit, BoltTurnOperations operations, ScreenUi screenUi)
        {
            _coordinator = coordinator;
            _selection = selection;
            _navigator = navigator;
            _campaign = campaign;
            _orbit = orbit;
            _collection = operations.Collection;
            _screenUi = screenUi;
        }

        private void Start()
        {
            var pending = _campaign.PendingLevelNumber;
            if (pending.HasValue && pending.Value != Attempt.LevelNumber)
            {
                throw new InvalidOperationException("Loaded level does not match the pending campaign transition.");
            }

            _levelLabel.text = $"Уровень {Attempt.LevelNumber}";
            _resultPanel.SetActive(false);
            _nextButton.gameObject.SetActive(false);
            if (pending.HasValue)
            {
                _campaign.CompleteTransition();
            }

            CreateHud();
            _started = true;
            RefreshExtraSlotButton();
            Subscribe();
        }

        private void OnEnable()
        {
            if (_started)
            {
                RefreshExtraSlotButton();
                Subscribe();
            }
        }

        private void OnDisable()
        {
            if (_started)
            {
                _coinReward.Stop();
                SetPaused(false);
                _selection.Selected -= OnSelected;
                _pauseButton.onClick.RemoveListener(Pause);
                _resumeButton.onClick.RemoveListener(Resume);
                _restartButton.onClick.RemoveListener(Restart);
                _retryButton.onClick.RemoveListener(Restart);
                _nextButton.onClick.RemoveListener(Next);
                _menuButton.onClick.RemoveListener(ReturnToMenu);
                _extraSlotButton.onClick.RemoveListener(ApplyExtraSlot);
            }
        }

        public async UniTask PlayAsync(int boltId)
        {
            if (_paused)
            {
                return;
            }

            await _coordinator.PlayAsync(boltId);
            if (Attempt.Status == AttemptStatus.Active || _campaign.PendingLevelNumber.HasValue || _resultPanel.activeSelf)
            {
                return;
            }

            var complete = _campaign.IsComplete(Attempt.LevelNumber, Attempt.Status);
            var won = Attempt.Status == AttemptStatus.Won;
            var reward = _campaign.RecordVictory(Attempt.LevelNumber, Attempt.Status);
            _resultLabel.text = complete
                ? "Кампания пройдена"
                : won ? "Победа!" : "Нет места";
            _resultLabel.color = complete ? new Color32(255, 216, 142, 255)
                : won ? new Color32(147, 242, 213, 255) : new Color32(255, 168, 156, 255);
            _nextButton.gameObject.SetActive(_campaign.CanAdvance(Attempt.LevelNumber, Attempt.Status));
            _rewardLabel.text = $"+{reward}";
            _rewardPanel.gameObject.SetActive(reward > 0);
            _resultPanel.SetActive(true);
            _extraSlotButton.gameObject.SetActive(false);
            if (reward > 0)
            {
                _coinReward.Play(_campaign.Coins - reward, reward);
            }
        }

        private void CreateHud()
        {
            var font = _levelLabel.font;
            var hud = ScreenUi.Area(_collection.SafeArea, "Gameplay HUD", new Rect(0f, 0f, 1f, 1f));
            var canvas = hud.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = _levelLabel.canvas.rootCanvas.sortingOrder + 1;
            hud.gameObject.AddComponent<GraphicRaycaster>();

            const float headerBottom = 0.912f;
            const float headerHeight = 0.056f;
            _pauseButton = _screenUi.Button(hud, "Pause", new Rect(0.05f, headerBottom, 0.105f, headerHeight),
                font, "", 26, Color.clear);
            _pauseButton.targetGraphic.canvasRenderer.cullTransparentMesh = false;
            for (var index = 0; index < 2; index++)
            {
                var bar = _screenUi.Surface(_pauseButton.transform, index == 0 ? "Left Bar" : "Right Bar",
                    new Rect(0.31f + index * 0.255f, 0.26f, 0.125f, 0.48f), Color.white);
                var outline = bar.gameObject.AddComponent<Outline>();
                outline.effectColor = ScreenUi.PanelColor;
                outline.effectDistance = Vector2.one;
            }

            var balance = _screenUi.CoinCounter(hud, "Balance", new Rect(0.716f, headerBottom, 0.234f, headerHeight),
                font, 32, out var balanceCoin);
            balance.text = _campaign.Coins.ToString();

            _extraSlotButton = _screenUi.Button(hud, "Apply Extra Slot", new Rect(0.399f, 0.043f, 0.202f, 0.101f),
                font, "", 24, ScreenUi.IvoryColor);
            _extraSlotTile = _extraSlotButton.gameObject.AddComponent<CanvasGroup>();
            ScreenUi.Icon(_extraSlotButton.transform, "ExtraSlot", new Rect(0.09f, 0.10f, 0.79f, 0.82f), _screenUi.ExtraSlotIcon);
            var badge = _screenUi.Surface(_extraSlotButton.transform, "Stock Badge", new Rect(0.63f, 0.015f, 0.36f, 0.31f),
                new Color32(135, 66, 129, 255));
            _extraSlotCount = ScreenUi.Label(badge.rectTransform, "Stock Amount", new Rect(0f, 0f, 1f, 1f), font, "", 22, Color.white);
            _extraSlotCount.fontStyle = FontStyle.Bold;

            _rewardPanel = ScreenUi.Area(_resultLabel.rectTransform, "Reward", new Rect(0.27f, 1f, 0.46f, 0f));
            _rewardPanel.sizeDelta = new Vector2(0f, 56f);
            _rewardPanel.anchoredPosition = new Vector2(0f, 32f);
            _rewardLabel = _screenUi.CoinCounter(_rewardPanel, "Reward", new Rect(0f, 0f, 1f, 1f), font, 29, out var rewardCoin);
            _rewardPanel.gameObject.SetActive(false);

            _coinReward = new CoinRewardAnimation(hud, rewardCoin, balanceCoin, balance, _screenUi.CoinSpinTexture);
            CreatePauseMenu(hud, font);
        }

        private void CreatePauseMenu(RectTransform hud, Font font)
        {
            _pauseOverlay = ScreenUi.Area(hud, "Pause Overlay", new Rect(0f, 0f, 1f, 1f)).gameObject.AddComponent<Image>();
            _pauseOverlay.color = new Color32(12, 22, 36, 178);
            var card = _screenUi.Surface(_pauseOverlay.rectTransform, "Pause Card", new Rect(0.11f, 0.29f, 0.78f, 0.42f),
                ScreenUi.PanelColor).rectTransform;
            var title = ScreenUi.Label(card, "Pause Title", new Rect(0.08f, 0.74f, 0.84f, 0.20f), font, "Пауза", 48, Color.white);
            title.fontStyle = FontStyle.Bold;
            _resumeButton = _screenUi.Button(card, "Resume", new Rect(0.10f, 0.50f, 0.80f, 0.18f),
                font, "Продолжить", 29, ScreenUi.ActionColor);
            ScreenUi.Place((RectTransform)_restartButton.transform, card, new Rect(0.10f, 0.28f, 0.80f, 0.17f));
            _menuButton = _screenUi.Button(card, "Return to Menu", new Rect(0.10f, 0.06f, 0.80f, 0.17f),
                font, "В главное меню", 27, new Color32(53, 73, 93, 255));
            _pauseOverlay.gameObject.SetActive(false);
        }

        private void Pause()
        {
            SetPaused(true);
        }

        private void Resume()
        {
            SetPaused(false);
        }

        private void SetPaused(bool paused)
        {
            if (paused == _paused)
            {
                return;
            }

            _paused = paused;
            if (paused)
            {
                _previousTimeScale = Time.timeScale;
                _previousAudioPause = AudioListener.pause;
                _previousOrbitEnabled = _orbit.enabled;
                _orbit.enabled = false;
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }
            else
            {
                Time.timeScale = _previousTimeScale;
                AudioListener.pause = _previousAudioPause;
                if (_orbit != null)
                {
                    _orbit.enabled = _previousOrbitEnabled;
                }
            }

            if (_pauseOverlay == null)
            {
                return;
            }

            _pauseOverlay.gameObject.SetActive(paused);
            _pauseButton.interactable = !paused;
            _retryButton.interactable = !paused;
            _nextButton.interactable = !paused;
            RefreshExtraSlotButton();
            if (paused)
            {
                EventSystem.current.SetSelectedGameObject(_resumeButton.gameObject);
            }
        }

        private void ApplyExtraSlot()
        {
            _campaign.TryUseExtraSlot(Attempt);
            RefreshExtraSlotButton();
        }

        private void RefreshExtraSlotButton()
        {
            var canUse = _campaign.CanUseExtraSlot(Attempt);
            _extraSlotButton.interactable = canUse && !_paused;
            _extraSlotTile.alpha = canUse ? 1f : 0.66f;
            _extraSlotCount.text = _campaign.ExtraSlots.ToString();
        }

        private void Subscribe()
        {
            _selection.Selected += OnSelected;
            _pauseButton.onClick.AddListener(Pause);
            _resumeButton.onClick.AddListener(Resume);
            _restartButton.onClick.AddListener(Restart);
            _retryButton.onClick.AddListener(Restart);
            _nextButton.onClick.AddListener(Next);
            _menuButton.onClick.AddListener(ReturnToMenu);
            _extraSlotButton.onClick.AddListener(ApplyExtraSlot);
        }

        private void Restart()
        {
            if (_campaign.TryRetry(Attempt.LevelNumber))
            {
                LeaveGameplay(_navigator.RestartGameplay);
            }
        }

        private void Next()
        {
            if (_campaign.TryAdvance(Attempt.LevelNumber, Attempt.Status))
            {
                LeaveGameplay(_navigator.RestartGameplay);
            }
        }

        private void ReturnToMenu()
        {
            LeaveGameplay(_navigator.ReturnToMenu);
        }

        private void LeaveGameplay(Func<bool> loadScene)
        {
            var started = false;
            try
            {
                started = loadScene();
            }
            finally
            {
                if (!started)
                {
                    _campaign.CancelTransition();
                }
            }

            if (!started)
            {
                return;
            }

            enabled = false;
            _orbit.enabled = false;
            _pauseButton.interactable = false;
            _restartButton.interactable = false;
            _retryButton.interactable = false;
            _nextButton.interactable = false;
            _menuButton.interactable = false;
            _extraSlotButton.interactable = false;
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
