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

        public LevelAssetReference GetLevel(int number)
        {
            foreach (var level in _levels)
            {
                if (level.Number == number)
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
