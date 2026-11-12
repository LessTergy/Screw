using System;
using NUnit.Framework;
using Screw.Gameplay;
using Screw.Levels;

namespace Screw.Tests.Gameplay
{
    public sealed class CampaignProgressTests
    {
        [TestCase(AttemptStatus.Active)]
        [TestCase(AttemptStatus.Lost)]
        public void AttemptWithoutVictoryCanRetryButCannotAdvance(AttemptStatus status)
        {
            var campaign = new CampaignProgress(5);
            Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(1));
            Assert.That(campaign.PendingLevelNumber, Is.Null);
            Assert.That(campaign.CanAdvance(1, status), Is.False);
            Assert.That(campaign.TryAdvance(1, status), Is.False);
            Assert.That(campaign.IsComplete(1, status), Is.False);
            Assert.That(campaign.PendingLevelNumber, Is.Null);

            Assert.That(campaign.TryRetry(1), Is.True);
            Assert.That(campaign.PendingLevelNumber, Is.EqualTo(1));
            Assert.That(campaign.TryRetry(1), Is.False);
            Assert.That(campaign.TryAdvance(1, AttemptStatus.Won), Is.False);
            campaign.CompleteTransition();
            Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(1));
            Assert.That(campaign.PendingLevelNumber, Is.Null);
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(LevelDataValidator.CampaignLevelCount)]
        public void VictoryAdvancesOneLevelAndTheLastLevelOnlyOffersRetry(int levelCount)
        {
            var campaign = new CampaignProgress(levelCount);
            for (var level = 1; level < levelCount; level++)
            {
                Assert.That(campaign.CanAdvance(level, AttemptStatus.Won), Is.True);
                Assert.That(campaign.IsComplete(level, AttemptStatus.Won), Is.False);
                Assert.That(campaign.TryAdvance(level, AttemptStatus.Won), Is.True);
                Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(level));
                Assert.That(campaign.PendingLevelNumber, Is.EqualTo(level + 1));
                Assert.That(campaign.TryAdvance(level, AttemptStatus.Won), Is.False);
                Assert.That(campaign.TryRetry(level), Is.False);
                Assert.That(campaign.PendingLevelNumber, Is.EqualTo(level + 1));

                campaign.CompleteTransition();
                Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(level + 1));
                Assert.That(campaign.PendingLevelNumber, Is.Null);
                Assert.That(campaign.TryAdvance(level, AttemptStatus.Won), Is.False);
                Assert.That(campaign.TryRetry(level), Is.False);
            }

            Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(levelCount));
            Assert.That(campaign.IsComplete(levelCount, AttemptStatus.Active), Is.False);
            Assert.That(campaign.IsComplete(levelCount, AttemptStatus.Lost), Is.False);
            Assert.That(campaign.IsComplete(levelCount, AttemptStatus.Won), Is.True);
            Assert.That(campaign.CanAdvance(levelCount, AttemptStatus.Won), Is.False);
            Assert.That(campaign.TryAdvance(levelCount, AttemptStatus.Won), Is.False);
            Assert.That(campaign.PendingLevelNumber, Is.Null);
            Assert.That(campaign.TryRetry(levelCount), Is.True);
            Assert.That(campaign.IsComplete(levelCount, AttemptStatus.Won), Is.False);
            campaign.CompleteTransition();
            Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(levelCount));
            Assert.That(new CampaignProgress(levelCount).CurrentLevelNumber, Is.EqualTo(1));
        }

        [Test]
        public void CancelledTransitionKeepsTheCurrentLevelAndAllowsAnotherRequest()
        {
            var campaign = new CampaignProgress(5);
            Assert.That(campaign.TryAdvance(1, AttemptStatus.Won), Is.True);
            campaign.CompleteTransition();
            Assert.That(campaign.TryAdvance(2, AttemptStatus.Won), Is.True);
            campaign.CancelTransition();

            Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(2));
            Assert.That(campaign.PendingLevelNumber, Is.Null);
            Assert.Throws<InvalidOperationException>(() => campaign.CompleteTransition());
            Assert.That(campaign.TryRetry(2), Is.True);
            Assert.That(campaign.PendingLevelNumber, Is.EqualTo(2));
            campaign.CompleteTransition();
            Assert.That(campaign.CurrentLevelNumber, Is.EqualTo(2));
            Assert.That(campaign.TryAdvance(2, AttemptStatus.Won), Is.True);
            Assert.That(campaign.PendingLevelNumber, Is.EqualTo(3));
        }

        [TestCase(0)]
        [TestCase(LevelDataValidator.CampaignLevelCount + 1)]
        public void InvalidCatalogSizeIsRejected(int levelCount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CampaignProgress(levelCount));
        }
    }
}
