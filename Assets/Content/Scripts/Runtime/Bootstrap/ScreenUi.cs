using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Screw.Bootstrap
{
    [Serializable]
    public sealed class ScreenUi
    {
        internal static readonly Color32 PanelColor = new Color32(33, 48, 64, 255);
        internal static readonly Color32 ActionColor = new Color32(31, 117, 98, 255);
        internal static readonly Color32 AccentColor = new Color32(255, 215, 141, 255);
        internal static readonly Color32 IvoryColor = new Color32(255, 242, 218, 255);

        [SerializeField] private Sprite _roundedSurface;
        [SerializeField] private Sprite _coinIcon;
        [SerializeField] private Sprite _extraSlotIcon;
        [SerializeField] private Texture2D _coinSpinTexture;

        internal Sprite ExtraSlotIcon => _extraSlotIcon;
        internal Texture2D CoinSpinTexture => _coinSpinTexture;

        internal static RectTransform Area(Transform parent, string name, Rect anchors)
        {
            var node = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)node.transform;
            Place(rect, parent, anchors);
            return rect;
        }

        internal static void Place(RectTransform rect, Transform parent, Rect anchors)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = anchors.min;
            rect.anchorMax = anchors.max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal Image Surface(Transform parent, string name, Rect anchors, Color color, bool raycast = false)
        {
            var image = Area(parent, name, anchors).gameObject.AddComponent<Image>();
            image.sprite = _roundedSurface;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 0.34f;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        internal static Image Icon(Transform parent, string iconName, Rect anchors, Sprite sprite)
        {
            var image = Area(parent, iconName, anchors).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        internal Text CoinCounter(Transform parent, string name, Rect anchors, Font font, int fontSize, out RectTransform coin)
        {
            var rect = Area(parent, name, anchors);
            Surface(rect, "Rim", new Rect(0.16f, 0.04f, 0.84f, 0.79f), new Color32(187, 137, 86, 255));
            Surface(rect, "Face", new Rect(0.16f, 0.12f, 0.84f, 0.79f), IvoryColor);
            coin = Icon(rect, "Coin", new Rect(0f, 0f, 0.48f, 1f), _coinIcon).rectTransform;
            var amount = Label(rect, name + " Amount", new Rect(0.44f, 0.12f, 0.50f, 0.79f), font, "", fontSize, PanelColor);
            amount.fontStyle = FontStyle.Bold;
            return amount;
        }

        internal static Text Label(Transform parent, string name, Rect anchors, Font font, string caption,
            int fontSize, Color color)
        {
            var text = Area(parent, name, anchors).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.text = caption;
            text.raycastTarget = false;
            return text;
        }

        internal Button Button(Transform parent, string name, Rect anchors, Font font, string caption,
            int fontSize, Color color, UnityAction onClick = null)
        {
            var image = Surface(parent, name, anchors, color, raycast: true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }
            if (caption.Length > 0)
            {
                var label = Label(image.rectTransform, "Label", new Rect(0.04f, 0f, 0.92f, 1f), font, caption, fontSize, Color.white);
                label.fontStyle = FontStyle.Bold;
            }

            return button;
        }
    }
}
