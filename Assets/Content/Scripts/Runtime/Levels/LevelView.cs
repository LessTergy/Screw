using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VContainer;

namespace Screw.Levels
{
    [DisallowMultipleComponent]
    public sealed class LevelView : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private LevelContentCatalog _contentCatalog;
        [SerializeField] private int _levelNumber = 1;
        [SerializeField] private Transform _levelRoot;

        [Header("Instance Bindings")]
        [SerializeField] private Transform _assemblyRoot;
        [SerializeField] private Transform _modelRoot;
        [SerializeField] private Transform _boltsRoot;
        [SerializeField] private LevelPartBinding[] _parts = Array.Empty<LevelPartBinding>();
        [SerializeField] private LevelBoltBinding[] _bolts = Array.Empty<LevelBoltBinding>();

        public LevelContentCatalog ContentCatalog
        {
            get { return _contentCatalog; }
        }

        public int LevelNumber
        {
            get { return _levelNumber; }
        }

        public Transform LevelRoot
        {
            get { return _levelRoot; }
        }

        public Transform ModelRoot
        {
            get { return _modelRoot; }
        }

        public Transform BoltsRoot
        {
            get { return _boltsRoot; }
        }

        public IReadOnlyList<LevelPartBinding> Parts
        {
            get { return Array.AsReadOnly(_parts); }
        }

        public IReadOnlyList<LevelBoltBinding> Bolts
        {
            get { return Array.AsReadOnly(_bolts); }
        }

        [Inject]
        public void Construct(LevelContentCatalog contentCatalog)
        {
            Require(_contentCatalog == contentCatalog, "Level view must reference the root DI content catalog.");
            ValidateBindings();
        }

        public LevelSourceData ReadSource()
        {
            Require(_contentCatalog != null && _contentCatalog.SourceCatalog != null, "Content catalog is missing.");
            var catalog = LevelDataReader.ReadCatalog(_contentCatalog.SourceCatalog.text);
            var assets = _contentCatalog.GetLevel(_levelNumber);
            Require(assets.Data != null && assets.Prefab != null, "Level source references are missing.");
            var source = LevelDataReader.ReadLevel(assets.Data.text, catalog.SchemaVersion);
            Require(source.Number == _levelNumber, "Level JSON number does not match the view.");
            return source;
        }

        public LevelSourceData ValidateBindings()
        {
            var source = ReadSource();
            Require(_levelRoot != null && _levelRoot.parent == transform, "Level root must be a child of the separate view object.");
            Require(_assemblyRoot != null && _assemblyRoot.parent == _levelRoot && _assemblyRoot.name == "Assembly",
                "Assembly binding is missing or belongs to another instance.");
            Require(_modelRoot != null && _modelRoot.parent == _assemblyRoot && _modelRoot.name == "Model",
                "Model binding is missing or belongs to another instance.");
            Require(_boltsRoot != null && _boltsRoot.parent == _assemblyRoot && _boltsRoot.name == "Bolts",
                "Bolts binding is missing or belongs to another instance.");

            CheckPose(_levelRoot, Vector3.zero, Vector3.zero, Vector3.one);
            CheckPose(_assemblyRoot, Vector3.zero, Vector(source.RootEulerDegrees), Vector3.one * (float)source.RootScale);
            CheckPose(_modelRoot, Vector(source.ModelPosition), Vector(source.ModelEulerDegrees), Vector3.one * (float)source.ModelScale);
            CheckPose(_boltsRoot, Vector3.zero, Vector3.zero, Vector3.one);
            Require(_parts != null && _parts.Length == source.Parts.Length && _modelRoot.childCount == source.Parts.Length,
                "Part binding count does not match the source.");
            Require(_bolts != null && _bolts.Length == source.Bolts.Length && _boltsRoot.childCount == source.Bolts.Length,
                "Bolt binding count does not match the source.");

            var parts = source.Parts.ToDictionary(part => part.Id);
            var partIds = new HashSet<int>();
            foreach (var binding in _parts)
            {
                Require(binding != null, "Part binding is missing.");
                Require(partIds.Add(binding.Id) && parts.ContainsKey(binding.Id), "Unknown or duplicate part binding: " + binding.Id + ".");
                var part = parts[binding.Id];
                Require(binding.Node != null && binding.Node.parent == _modelRoot
                    && binding.Node.GetSiblingIndex() == part.ChildIndex, "Part node does not match its ID: " + binding.Id + ".");
                CheckPath(binding.Node, part.NodePath);
            }

            var bolts = source.Bolts.ToDictionary(bolt => bolt.Id);
            var boltIds = new HashSet<int>();
            foreach (var binding in _bolts)
            {
                Require(binding != null, "Bolt binding is missing.");
                Require(boltIds.Add(binding.Id) && bolts.ContainsKey(binding.Id), "Unknown or duplicate bolt binding: " + binding.Id + ".");
                var bolt = bolts[binding.Id];
                Require(binding.OwnerId == bolt.OwnerId, "Bolt owner does not match source: " + binding.Id + ".");
                Require(binding.Node != null && binding.Node.parent == _boltsRoot, "Bolt node belongs to another instance: " + binding.Id + ".");
                CheckPath(binding.Node, bolt.NodePath);
                CheckPose(binding.Node, Vector(bolt.Position), Vector(bolt.EulerDegrees), Vector3.one);
            }

            return source;
        }

        public LevelPartBinding GetPart(int id)
        {
            foreach (var part in _parts)
            {
                if (part.Id == id)
                {
                    return part;
                }
            }

            throw new KeyNotFoundException("Unknown part binding: " + id + ".");
        }

        public LevelBoltBinding GetBolt(int id)
        {
            foreach (var bolt in _bolts)
            {
                if (bolt.Id == id)
                {
                    return bolt;
                }
            }

            throw new KeyNotFoundException("Unknown bolt binding: " + id + ".");
        }

        private void CheckPath(Transform node, string expected)
        {
            var names = new Stack<string>();
            var current = node;
            while (current != null && current != _levelRoot)
            {
                names.Push(current.name);
                current = current.parent;
            }

            Require(current == _levelRoot && string.Join("/", names) == expected, "Node path does not match source: " + expected + ".");
        }

        private static void CheckPose(Transform node, Vector3 position, Vector3 eulerDegrees, Vector3 scale)
        {
            Require(Vector3.Distance(node.localPosition, position) < 0.0001f
                && Quaternion.Angle(node.localRotation, Quaternion.Euler(eulerDegrees)) < 0.01f
                && Vector3.Distance(node.localScale, scale) < 0.0001f, "Source pose changed on " + node.name + ".");
        }

        private static Vector3 Vector(double[] value)
        {
            return new Vector3((float)value[0], (float)value[1], (float)value[2]);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }

    [Serializable]
    public sealed class LevelPartBinding
    {
        [SerializeField] private int _id;
        [SerializeField] private Transform _node;

        public int Id
        {
            get { return _id; }
        }

        public Transform Node
        {
            get { return _node; }
        }
    }

    [Serializable]
    public sealed class LevelBoltBinding
    {
        [SerializeField] private int _id;
        [SerializeField] private int _ownerId;
        [SerializeField] private Transform _node;

        public int Id
        {
            get { return _id; }
        }

        public int OwnerId
        {
            get { return _ownerId; }
        }

        public Transform Node
        {
            get { return _node; }
        }
    }
}
