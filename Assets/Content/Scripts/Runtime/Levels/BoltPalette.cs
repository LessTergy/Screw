using System;
using System.Collections.Generic;
using UnityEngine;

namespace Screw.Levels
{
    public sealed class BoltPalette : ScriptableObject
    {
        [SerializeField] private GameObject _prefab;
        [SerializeField] private int _defaultIndex;
        [SerializeField] private Entry[] _colors = Array.Empty<Entry>();

        public GameObject Prefab
        {
            get { return _prefab; }
        }

        public int DefaultIndex
        {
            get { return _defaultIndex; }
        }

        public IReadOnlyList<Entry> Colors
        {
            get { return _colors; }
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private int _index;
            [SerializeField] private Material _primary;
            [SerializeField] private Material _secondary;

            public int Index
            {
                get { return _index; }
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
}
