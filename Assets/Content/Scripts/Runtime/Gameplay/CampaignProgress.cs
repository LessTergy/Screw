using System;
using Screw.Levels;
using UnityEngine;

namespace Screw.Gameplay
{
    public sealed class CampaignProgress
    {
        public const string SaveKey = "Screw.Campaign.v1";
        public const int FirstWinReward = 100;
        public const int ExtraSlotPrice = 100;

        private readonly int _levelCount;
        private readonly string _saveKey;
        private Profile _profile = new Profile();

        public int CurrentLevelNumber { get; private set; } = 1;
        public int? PendingLevelNumber { get; private set; }
        public int Coins => _profile.Coins;
        public int ExtraSlots => _profile.ExtraSlots;
        public int LevelsCleared => _profile.LevelsCleared;
        public bool CampaignCleared => LevelsCleared == _levelCount;
        public int ResumeLevelNumber => Math.Min(LevelsCleared + 1, _levelCount);

        public CampaignProgress(int levelCount)
            : this(levelCount, null)
        {
        }

        private CampaignProgress(int levelCount, string saveKey)
        {
            if (levelCount < 1 || levelCount > LevelDataValidator.CampaignLevelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(levelCount), levelCount,
                    "Campaign level count must be between 1 and " + LevelDataValidator.CampaignLevelCount + ".");
            }

            _levelCount = levelCount;
            _saveKey = saveKey;
        }

        public static CampaignProgress Load(int levelCount, string saveKey)
        {
            var progress = new CampaignProgress(levelCount, saveKey);
            var saved = PlayerPrefs.GetString(saveKey);
            if (saved.Length == 0)
            {
                return progress;
            }

            try
            {
                var profile = JsonUtility.FromJson<Profile>(saved);
                if (profile != null && profile.LevelsCleared >= 0 && profile.LevelsCleared <= levelCount
                    && profile.Coins >= 0 && profile.Coins <= profile.LevelsCleared * FirstWinReward
                    && profile.ExtraSlots >= 0 && profile.ExtraSlots <= profile.LevelsCleared)
                {
                    progress._profile = profile;
                    return progress;
                }
            }
            catch (ArgumentException)
            {
                // JsonUtility throws on malformed preferences.
            }

            Debug.LogWarning("Campaign save is invalid. Starting with an empty profile.");
            return progress;
        }

        public int RecordVictory(int levelNumber, AttemptStatus status)
        {
            if (!AcceptsCommandsFrom(levelNumber) || status != AttemptStatus.Won || CampaignCleared
                || levelNumber != LevelsCleared + 1)
            {
                return 0;
            }

            Save(levelNumber, Coins + FirstWinReward, ExtraSlots);
            return FirstWinReward;
        }

        public bool TryBuyExtraSlot()
        {
            if (Coins < ExtraSlotPrice)
            {
                return false;
            }

            Save(LevelsCleared, Coins - ExtraSlotPrice, ExtraSlots + 1);
            return true;
        }

        public bool CanUseExtraSlot(LevelAttempt attempt)
        {
            return ExtraSlots > 0 && AcceptsCommandsFrom(attempt.LevelNumber)
                && attempt.Status == AttemptStatus.Active && !attempt.HasExtraPoolSlot;
        }

        public bool TryUseExtraSlot(LevelAttempt attempt)
        {
            if (!CanUseExtraSlot(attempt))
            {
                return false;
            }

            Save(LevelsCleared, Coins, ExtraSlots - 1);
            attempt.AddExtraPoolSlot();
            return true;
        }

        public bool TryContinue()
        {
            if (PendingLevelNumber.HasValue)
            {
                return false;
            }

            PendingLevelNumber = ResumeLevelNumber;
            return true;
        }

        public bool CanAdvance(int levelNumber, AttemptStatus status)
        {
            return AcceptsCommandsFrom(levelNumber) && status == AttemptStatus.Won && levelNumber < _levelCount;
        }

        public bool IsComplete(int levelNumber, AttemptStatus status)
        {
            return AcceptsCommandsFrom(levelNumber) && status == AttemptStatus.Won && levelNumber == _levelCount;
        }

        public bool TryAdvance(int levelNumber, AttemptStatus status)
        {
            if (!CanAdvance(levelNumber, status))
            {
                return false;
            }

            PendingLevelNumber = levelNumber + 1;
            return true;
        }

        public bool TryRetry(int levelNumber)
        {
            if (!AcceptsCommandsFrom(levelNumber))
            {
                return false;
            }

            PendingLevelNumber = levelNumber;
            return true;
        }

        public void CompleteTransition()
        {
            CurrentLevelNumber = PendingLevelNumber
                ?? throw new InvalidOperationException("No campaign transition is pending.");
            PendingLevelNumber = null;
        }

        public void CancelTransition()
        {
            PendingLevelNumber = null;
        }

        private bool AcceptsCommandsFrom(int levelNumber)
        {
            return levelNumber == CurrentLevelNumber && !PendingLevelNumber.HasValue;
        }

        private void Save(int levelsCleared, int coins, int extraSlots)
        {
            var profile = new Profile { LevelsCleared = levelsCleared, Coins = coins, ExtraSlots = extraSlots };
            if (_saveKey != null)
            {
                PlayerPrefs.SetString(_saveKey, JsonUtility.ToJson(profile));
                PlayerPrefs.Save();
            }

            _profile = profile;
        }

        [Serializable]
        private sealed class Profile
        {
            public int LevelsCleared;
            public int Coins;
            public int ExtraSlots;
        }
    }
}
