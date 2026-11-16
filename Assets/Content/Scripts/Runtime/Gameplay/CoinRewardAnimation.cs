using System;
using DG.Tweening;
using Screw.Bootstrap;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Screw.Gameplay
{
    internal sealed class CoinRewardAnimation
    {
        private const int CoinCount = 5;
        private const int AtlasColumns = 4;
        private const int AtlasRows = 3;
        private const float InitialDelay = 0.08f;
        private const float SpinFramesPerSecond = 24f;
        private const float AppearanceTime = 0.21f;
        private const float FlightTime = 0.53f;
        private const float SpawnInterval = 0.18f;
        private const float CoinDuration = AppearanceTime + FlightTime;

        private readonly RectTransform _layer;
        private readonly RectTransform _source;
        private readonly RectTransform _destination;
        private readonly Text _balance;
        private readonly Texture2D _atlas;
        private Tween _animation;

        internal CoinRewardAnimation(RectTransform hud, RectTransform source, RectTransform destination, Text balance, Texture2D atlas)
        {
            _layer = ScreenUi.Area(hud, "Flying Coins", new Rect(0f, 0f, 1f, 1f));
            _source = source;
            _destination = destination;
            _balance = balance;
            _atlas = atlas;
        }

        internal void Play(int previousBalance, int reward)
        {
            Stop();
            if (_atlas == null)
            {
                _balance.text = (previousBalance + reward).ToString();
                throw new InvalidOperationException("Assign the coin animation texture in AppLifetimeScope.");
            }

            _balance.text = previousBalance.ToString();
            var coins = new RawImage[CoinCount];
            for (var index = 0; index < coins.Length; index++)
            {
                var coin = ScreenUi.Area(_layer, "Reward Coin", new Rect(0.5f, 0.5f, 0f, 0f)).gameObject.AddComponent<RawImage>();
                coin.texture = _atlas;
                coin.raycastTarget = false;
                coin.gameObject.SetActive(false);
                coins[index] = coin;
            }

            var duration = InitialDelay + (CoinCount - 1) * SpawnInterval + CoinDuration;
            var arrivals = 0;
            _animation = DOVirtual.Float(0f, duration, duration, elapsed =>
            {
                var currentArrivals = UpdateCoins(coins, elapsed);
                if (currentArrivals != arrivals)
                {
                    arrivals = currentArrivals;
                    _balance.text = (previousBalance + reward * arrivals / CoinCount).ToString();
                }
            }).SetEase(Ease.Linear).SetUpdate(UpdateType.Normal, false)
                .SetAutoKill(true).SetLink(_layer.gameObject).OnKill(() =>
            {
                _animation = null;
                if (_balance != null)
                {
                    _balance.text = (previousBalance + reward).ToString();
                }

                foreach (var coin in coins)
                {
                    if (coin != null)
                    {
                        coin.gameObject.SetActive(false);
                        Object.Destroy(coin.gameObject);
                    }
                }
            });
            _animation.Play();
        }

        internal void Stop()
        {
            _animation?.Kill();
        }

        private int UpdateCoins(RawImage[] coins, float elapsed)
        {
            var arrivals = 0;
            for (var index = 0; index < coins.Length; index++)
            {
                var startsAt = InitialDelay + index * SpawnInterval;
                if (elapsed < startsAt)
                {
                    break;
                }

                var coin = coins[index];
                if (elapsed >= startsAt + CoinDuration)
                {
                    arrivals++;
                    if (coin.gameObject.activeSelf)
                    {
                        coin.gameObject.SetActive(false);
                    }
                }
                else
                {
                    UpdateCoin(coin, elapsed - startsAt, (index - (CoinCount - 1) * 0.5f) * 0.02f);
                }
            }

            return arrivals;
        }

        private void UpdateCoin(RawImage coin, float elapsed, float horizontalOffset)
        {
            var rect = coin.rectTransform;
            var origin = CenterInLayer(_source) + Vector2.right * (_layer.rect.width * horizontalOffset);
            var destination = CenterInLayer(_destination);
            var bend = (origin + destination) * 0.5f + new Vector2(-_layer.rect.width * 0.19f, _layer.rect.height * 0.10f);
            var flight = Mathf.Clamp01((elapsed - AppearanceTime) / FlightTime);
            var travel = DOVirtual.EasedValue(0f, 1f, flight, Ease.InOutQuad);
            rect.anchoredPosition = Vector2.Lerp(Vector2.Lerp(origin, bend, travel),
                Vector2.Lerp(bend, destination, travel), travel);

            var size = Mathf.Min(_destination.rect.width, _destination.rect.height) * 0.86f;
            rect.sizeDelta = Vector2.one * size;
            var appearance = Mathf.Clamp01(elapsed / AppearanceTime);
            var bounce = DOVirtual.EasedValue(0.18f, 1f, appearance, Ease.OutBack);
            rect.localScale = Vector3.one * (bounce * Mathf.Lerp(1f, 0.54f, flight * flight));
            var frame = Mathf.FloorToInt(elapsed * SpinFramesPerSecond) % (AtlasColumns * AtlasRows);
            const float frameWidth = 1f / AtlasColumns;
            const float frameHeight = 1f / AtlasRows;
            coin.uvRect = new Rect(frame % AtlasColumns * frameWidth, 1f - (frame / AtlasColumns + 1) * frameHeight,
                frameWidth, frameHeight);
            if (!coin.gameObject.activeSelf)
            {
                coin.gameObject.SetActive(true);
            }
        }

        private Vector2 CenterInLayer(RectTransform rect)
        {
            return _layer.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        }
    }
}
