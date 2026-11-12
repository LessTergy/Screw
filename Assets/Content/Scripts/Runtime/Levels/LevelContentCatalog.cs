using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Screw.Levels
{
    [CreateAssetMenu(fileName = "LevelContentCatalog", menuName = "Screw/Level Content Catalog")]
    public sealed class LevelContentCatalog : ScriptableObject
    {
        [SerializeField] private TextAsset _sourceCatalog;
        [SerializeField] private LevelAssetReference[] _levels = Array.Empty<LevelAssetReference>();
        [SerializeField] private BoltPaletteReference[] _palette = Array.Empty<BoltPaletteReference>();

        public TextAsset SourceCatalog
        {
            get { return _sourceCatalog; }
        }

        public IReadOnlyList<LevelAssetReference> Levels
        {
            get { return _levels; }
        }

        public IReadOnlyList<BoltPaletteReference> Palette
        {
            get { return _palette; }
        }

        public void ValidateCampaign()
        {
            if (_sourceCatalog == null)
            {
                throw new InvalidDataException("Campaign source catalog is missing.");
            }

            var source = LevelDataReader.ReadCatalog(_sourceCatalog.text);
            if (_levels.Length != source.Levels.Length)
            {
                throw new InvalidDataException("Campaign entries do not match the source catalog.");
            }

            for (var number = 1; number <= _levels.Length; number++)
            {
                var level = GetLevel(number);
                if (level.View == null || level.Gameplay == null)
                {
                    throw new InvalidDataException("Campaign view or gameplay definition is missing for level " + number + ".");
                }

                if (level.View.ContentCatalog != this || level.View.LevelNumber != number
                    || level.Gameplay.ContentCatalog != this || level.Gameplay.Gameplay?.LevelNumber != number)
                {
                    throw new InvalidDataException("Campaign view or gameplay definition does not match level " + number + ".");
                }

                level.View.ValidateBindings();
                level.Gameplay.ReadSource();
            }
        }

        public LevelAssetReference GetLevel(int number)
        {
            foreach (var level in _levels)
            {
                if (level != null && level.Number == number)
                {
                    return level;
                }
            }

            throw new InvalidDataException("Missing level asset reference: " + number + ".");
        }

        public BoltPaletteReference GetColor(int index)
        {
            foreach (var color in _palette)
            {
                if (color.Index == index)
                {
                    return color;
                }
            }

            throw new InvalidDataException("Missing palette asset reference: " + index + ".");
        }
    }

    [Serializable]
    public sealed class LevelAssetReference
    {
        [SerializeField] private int _number;
        [SerializeField] private TextAsset _data;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private LevelView _view;
        [SerializeField] private LevelGameplayDefinition _gameplay;

        public int Number
        {
            get { return _number; }
        }

        public TextAsset Data
        {
            get { return _data; }
        }

        public GameObject Prefab
        {
            get { return _prefab; }
        }

        public LevelView View
        {
            get { return _view; }
        }

        public LevelGameplayDefinition Gameplay
        {
            get { return _gameplay; }
        }
    }

    [Serializable]
    public sealed class BoltPaletteReference
    {
        [SerializeField] private int _index;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private Material _primary;
        [SerializeField] private Material _secondary;

        public int Index
        {
            get { return _index; }
        }

        public GameObject Prefab
        {
            get { return _prefab; }
        }

        public Material Primary
        {
            get { return _primary; }
        }

        public Material Secondary
        {
            get { return _secondary; }
        }
    }
}
