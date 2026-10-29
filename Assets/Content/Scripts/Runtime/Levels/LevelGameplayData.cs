using System;

namespace Screw.Levels
{
    [Serializable]
    public sealed class LevelGameplayData
    {
        public int LevelNumber;
        public ColorGroupData[] ColorGroups;
        public int[] ContainerGroups;
    }

    [Serializable]
    public sealed class ColorGroupData
    {
        public int Id;
        public int[] BoltIds;
    }
}
