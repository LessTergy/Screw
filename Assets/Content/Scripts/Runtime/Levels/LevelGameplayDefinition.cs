using System.IO;
using UnityEngine;

namespace Screw.Levels
{
    public sealed class LevelGameplayDefinition : ScriptableObject
    {
        [SerializeField] private LevelContentCatalog _contentCatalog;
        [SerializeField] private LevelGameplayData _gameplay;

        public LevelContentCatalog ContentCatalog
        {
            get { return _contentCatalog; }
        }

        public LevelGameplayData Gameplay
        {
            get { return _gameplay; }
        }

        public LevelSourceData ReadSource()
        {
            if (_gameplay == null)
            {
                throw new InvalidDataException("Gameplay data is missing.");
            }

            var catalog = LevelDataReader.ReadCatalog(_contentCatalog.SourceCatalog.text);
            var assets = _contentCatalog.GetLevel(_gameplay.LevelNumber);
            var source = LevelDataReader.ReadLevel(assets.Data.text, catalog.SchemaVersion);
            LevelDataValidator.ValidateGameplay(source, _gameplay);
            return source;
        }
    }
}
