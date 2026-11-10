using System;
using System.Collections.Generic;
using UnityEngine;

namespace Screw.Editor.Levels
{
    public sealed class MeshRepairReview : ScriptableObject
    {
        [Serializable]
        public sealed class Part
        {
            public string NodePath;
            public string Problem;
            public Mesh Before;
            public string BeforeHash;
            public Mesh After;
            public string AfterHash;
            public GameObject Preview;
            public string Error;
            public string Mode = "cap-only";
            public string FallbackReason;
            public int AddedTriangles;
            public int TriangleCountBefore;
            public int TriangleCountAfter;
            public int ComponentCountBefore;
            public int ComponentCountAfter;
            public bool Applied;
        }

        [SerializeField] private GameObject _source;
        [SerializeField] private List<Part> _parts = new List<Part>();

        public GameObject Source
        {
            get { return _source; }
        }

        public IReadOnlyList<Part> Parts
        {
            get { return _parts; }
        }

        public void Initialize(GameObject source, List<Part> parts)
        {
            _source = source;
            _parts = parts;
        }
    }
}
