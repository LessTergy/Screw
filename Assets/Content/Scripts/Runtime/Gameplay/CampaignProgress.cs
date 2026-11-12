using System;
using Screw.Levels;

namespace Screw.Gameplay
{
    public sealed class CampaignProgress
    {
        private readonly int _levelCount;

        public int CurrentLevelNumber { get; private set; } = 1;
        public int? PendingLevelNumber { get; private set; }

        public CampaignProgress(int levelCount)
        {
            if (levelCount < 1 || levelCount > LevelDataValidator.CampaignLevelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(levelCount), levelCount,
                    "Campaign level count must be between 1 and " + LevelDataValidator.CampaignLevelCount + ".");
            }

            _levelCount = levelCount;
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
    }
}
