using Screw.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Screw.Bootstrap
{
    internal sealed class MainMenuView : MonoBehaviour
    {
        private CampaignProgress _campaign;
        private SceneNavigator _navigator;
        private RectTransform _safeArea;
        private RectTransform _home;
        private RectTransform _shop;
        private Text _balance;
        private Text _stock;
        private Text _purchaseStatus;
        private Button _play;
        private Button _buy;
        private bool _leaving;

        internal static MainMenuView Create(Scene scene, CampaignProgress campaign, SceneNavigator navigator, ScreenUi screenUi)
        {
            var root = new GameObject("Screw Menu", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MainMenuView));
            SceneManager.MoveGameObjectToScene(root, scene);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(540f, 960f);
            scaler.matchWidthOrHeight = 0.5f;

            if (EventSystem.current == null)
            {
                var input = new GameObject("Menu Input", typeof(EventSystem), typeof(StandaloneInputModule));
                input.transform.SetParent(root.transform, false);
            }

            var view = root.GetComponent<MainMenuView>();
            view.Initialize(campaign, navigator, screenUi);
            return view;
        }

        private void Initialize(CampaignProgress campaign, SceneNavigator navigator, ScreenUi screenUi)
        {
            _campaign = campaign;
            _navigator = navigator;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var screen = new Rect(0f, 0f, 1f, 1f);
            var background = ScreenUi.Area(transform, "Background", screen).gameObject.AddComponent<Image>();
            background.color = new Color32(19, 30, 44, 255);
            background.raycastTarget = false;
            _safeArea = ScreenUi.Area(transform, "Safe Area", screen);
            _balance = screenUi.CoinCounter(_safeArea, "Balance", new Rect(0.68f, 0.87f, 0.28f, 0.075f), font, 34, out _);

            var card = screenUi.Surface(_safeArea, "Card", new Rect(0.10f, 0.23f, 0.80f, 0.52f), ScreenUi.PanelColor).rectTransform;
            _home = ScreenUi.Area(card, "Home", screen);
            var title = ScreenUi.Label(_home, "Title", new Rect(0.08f, 0.68f, 0.84f, 0.25f),
                font, "Screw", 68, Color.white);
            title.fontStyle = FontStyle.Bold;
            var progress = campaign.CampaignCleared ? "Кампания пройдена" : $"Уровень {campaign.ResumeLevelNumber}";
            ScreenUi.Label(_home, "Progress", new Rect(0.08f, 0.52f, 0.84f, 0.16f), font, progress, 27, ScreenUi.AccentColor);
            var playCaption = campaign.CampaignCleared ? "Повторить последний уровень"
                : campaign.LevelsCleared == 0 ? "Играть" : "Продолжить";
            _play = screenUi.Button(_home, "Play", new Rect(0.10f, 0.28f, 0.80f, 0.17f),
                font, playCaption, 29, ScreenUi.ActionColor, Play);
            screenUi.Button(_home, "Shop", new Rect(0.10f, 0.08f, 0.80f, 0.16f),
                font, "Магазин", 26, new Color32(53, 73, 93, 255), () => ShowShop(true));

            _shop = ScreenUi.Area(card, "Shop", screen);
            ScreenUi.Label(_shop, "Title", new Rect(0.08f, 0.78f, 0.84f, 0.16f), font, "Магазин", 42, Color.white);
            ScreenUi.Icon(_shop, "ExtraSlot", new Rect(0.06f, 0.56f, 0.20f, 0.23f), screenUi.ExtraSlotIcon);
            ScreenUi.Label(_shop, "Product", new Rect(0.27f, 0.63f, 0.67f, 0.13f),
                font, "Дополнительная ячейка", 29, ScreenUi.AccentColor);
            ScreenUi.Label(_shop, "Effect", new Rect(0.27f, 0.52f, 0.67f, 0.11f),
                font, "+1 место в пуле на одну попытку", 20, Color.white);
            _stock = ScreenUi.Label(_shop, "Stock", new Rect(0.08f, 0.43f, 0.84f, 0.09f), font, "", 22, Color.white);
            _buy = screenUi.Button(_shop, "Buy", new Rect(0.10f, 0.24f, 0.80f, 0.16f), font,
                "", 26, ScreenUi.ActionColor, Buy);
            var buyLabel = ScreenUi.Label(_buy.transform, "Label", new Rect(0.04f, 0f, 0.52f, 1f), font, "Купить", 27, Color.white);
            buyLabel.fontStyle = FontStyle.Bold;
            var price = screenUi.CoinCounter(_buy.transform, "Price", new Rect(0.55f, 0.13f, 0.39f, 0.74f), font, 24, out _);
            price.text = CampaignProgress.ExtraSlotPrice.ToString();
            _purchaseStatus = ScreenUi.Label(_shop, "Purchase Status", new Rect(0.08f, 0.16f, 0.84f, 0.08f),
                font, "", 19, ScreenUi.AccentColor);
            screenUi.Button(_shop, "Back", new Rect(0.10f, 0.03f, 0.80f, 0.13f),
                font, "Назад", 24, new Color32(53, 73, 93, 255), () => ShowShop(false));
            ShowShop(false);
            LateUpdate();
        }

        private void LateUpdate()
        {
            var safe = Screen.safeArea;
            var size = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            var min = safe.min / size;
            var max = safe.max / size;
            if (_safeArea.anchorMin != min || _safeArea.anchorMax != max)
            {
                _safeArea.anchorMin = min;
                _safeArea.anchorMax = max;
            }
        }

        private void ShowShop(bool visible)
        {
            if (_leaving)
            {
                return;
            }

            _home.gameObject.SetActive(!visible);
            _shop.gameObject.SetActive(visible);
            RefreshPurchase(false);
        }

        private void Buy()
        {
            RefreshPurchase(_campaign.TryBuyExtraSlot());
        }

        private void RefreshPurchase(bool purchased)
        {
            _balance.text = _campaign.Coins.ToString();
            _stock.text = $"В запасе   {_campaign.ExtraSlots}";
            _buy.interactable = _campaign.Coins >= CampaignProgress.ExtraSlotPrice;
            _purchaseStatus.text = purchased ? "Куплено" : _buy.interactable ? "" : "Недостаточно монет";
        }

        private void Play()
        {
            if (!_campaign.TryContinue())
            {
                return;
            }

            try
            {
                _leaving = _navigator.ToGameplay();
            }
            finally
            {
                if (!_leaving)
                {
                    _campaign.CancelTransition();
                }
            }

            _play.interactable = !_leaving;
        }
    }
}
