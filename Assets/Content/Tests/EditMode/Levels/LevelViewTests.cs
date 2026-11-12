using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Screw.Editor.Levels;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Screw.Tests.Levels
{
    public sealed class LevelViewTests
    {
        private LevelContentCatalog _catalog;
        private GameObject _wrapper;
        private LevelView _view;

        [SetUp]
        public void SetUp()
        {
            _catalog = Object.Instantiate(LevelTestFixtures.LoadCampaign());
            _wrapper = new GameObject("LevelViewTest");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(_catalog.GetLevel(1).Prefab);
            instance.transform.SetParent(_wrapper.transform, false);
            _view = _wrapper.AddComponent<LevelView>();
            using (var serialized = new SerializedObject(_view))
            {
                serialized.FindProperty("_contentCatalog").objectReferenceValue = _catalog;
                serialized.FindProperty("_levelRoot").objectReferenceValue = instance.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_wrapper != null)
            {
                Object.DestroyImmediate(_wrapper);
            }

            if (_catalog != null)
            {
                Object.DestroyImmediate(_catalog);
            }
        }

        [Test]
        public void RealLevelOneBindsAllPartsAndBoltsWithoutChangingSourceOrPoses()
        {
            var prefabPath = AssetDatabase.GetAssetPath(_catalog.GetLevel(1).Prefab);
            var dataPath = AssetDatabase.GetAssetPath(_catalog.GetLevel(1).Data);
            var prefabHash = AssetDatabase.GetAssetDependencyHash(prefabPath);
            var dataHash = AssetDatabase.GetAssetDependencyHash(dataPath);
            var catalogBefore = EditorJsonUtility.ToJson(_catalog);
            var nodes = _view.LevelRoot.GetComponentsInChildren<Transform>(true);
            var poses = nodes.Select(node => new { node.localPosition, node.localRotation, node.localScale }).ToArray();

            LevelViewBindingUtility.Bind(_view);
            var source = _view.ValidateBindings();
            Assert.That(source.Number, Is.EqualTo(1));
            Assert.That(_view.Parts.Count, Is.EqualTo(4));
            Assert.That(_view.Bolts.Count, Is.EqualTo(9));
            CollectionAssert.AreEquivalent(source.Parts.Select(part => part.Id), _view.Parts.Select(part => part.Id));
            CollectionAssert.AreEquivalent(source.Bolts.Select(bolt => bolt.Id), _view.Bolts.Select(bolt => bolt.Id));
            foreach (var bolt in source.Bolts)
            {
                var binding = _view.GetBolt(bolt.Id);
                Assert.That(binding.OwnerId, Is.EqualTo(bolt.OwnerId));
                Assert.That(binding.Node.parent, Is.EqualTo(_view.BoltsRoot));
                Assert.That(_view.GetPart(binding.OwnerId).Node.parent, Is.EqualTo(_view.ModelRoot));
                Assert.That(binding.Node, Is.EqualTo(_view.LevelRoot.Find(bolt.NodePath)));
            }

            var firstBindings = EditorJsonUtility.ToJson(_view);
            LevelViewBindingUtility.Bind(_view);
            Assert.That(EditorJsonUtility.ToJson(_view), Is.EqualTo(firstBindings));
            for (var i = 0; i < nodes.Length; i++)
            {
                Assert.That(nodes[i].localPosition, Is.EqualTo(poses[i].localPosition));
                Assert.That(nodes[i].localRotation, Is.EqualTo(poses[i].localRotation));
                Assert.That(nodes[i].localScale, Is.EqualTo(poses[i].localScale));
            }

            Assert.That(EditorJsonUtility.ToJson(_catalog), Is.EqualTo(catalogBefore));
            Assert.That(AssetDatabase.GetAssetDependencyHash(prefabPath), Is.EqualTo(prefabHash));
            Assert.That(AssetDatabase.GetAssetDependencyHash(dataPath), Is.EqualTo(dataHash));
            Assert.That(AssetDatabase.GetAssetPath(_catalog), Is.Empty);
            Assert.Throws<NotSupportedException>(() => ((IList<LevelPartBinding>)_view.Parts).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<LevelBoltBinding>)_view.Bolts).Clear());
            Assert.Throws<KeyNotFoundException>(() => _view.GetPart(-1));
            Assert.Throws<KeyNotFoundException>(() => _view.GetBolt(-1));
        }

        [Test]
        public void InjectionRequiresTheSameCatalogWithoutReplacingTheSavedReference()
        {
            LevelViewBindingUtility.Bind(_view);
            Assert.DoesNotThrow(() => _view.Construct(_catalog));
            var other = ScriptableObject.CreateInstance<LevelContentCatalog>();
            try
            {
                Assert.Throws<InvalidDataException>(() => _view.Construct(other));
                Assert.That(_view.ContentCatalog, Is.SameAs(_catalog));
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void InjectionRejectsUnpreparedBindings()
        {
            Assert.Throws<InvalidDataException>(() => _view.Construct(_catalog));
        }

        [Test]
        public void BindingArrayOrderAndExternalPlacementDoNotChangeIdentity()
        {
            LevelViewBindingUtility.Bind(_view);
            using (var serialized = new SerializedObject(_view))
            {
                serialized.FindProperty("_parts").MoveArrayElement(0, 3);
                serialized.FindProperty("_bolts").MoveArrayElement(0, 8);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            _wrapper.transform.SetPositionAndRotation(new Vector3(10, 2, -3), Quaternion.Euler(0, 135, 0));
            _wrapper.transform.localScale = Vector3.one * 2;
            Assert.DoesNotThrow(() => LevelViewBindingUtility.Validate(_view));
            Assert.That(_view.GetBolt(0).Id, Is.Zero);
            Assert.That(_view.GetPart(_view.GetBolt(0).OwnerId).Id, Is.EqualTo(_view.GetBolt(0).OwnerId));
        }

        [TestCase("missingPart")]
        [TestCase("duplicatePart")]
        [TestCase("unknownPart")]
        [TestCase("wrongPartNode")]
        [TestCase("missingBolt")]
        [TestCase("duplicateBolt")]
        [TestCase("unknownBolt")]
        [TestCase("wrongBoltNode")]
        [TestCase("wrongOwner")]
        [TestCase("missingModel")]
        [TestCase("missingCatalog")]
        public void InvalidBindingsAreRejectedWithoutRepair(string defect)
        {
            LevelViewBindingUtility.Bind(_view);
            using (var serialized = new SerializedObject(_view))
            {
                var parts = serialized.FindProperty("_parts");
                var bolts = serialized.FindProperty("_bolts");
                switch (defect)
                {
                    case "missingPart": { parts.arraySize--; break; }
                    case "duplicatePart": { parts.GetArrayElementAtIndex(1).FindPropertyRelative("_id").intValue = _view.Parts[0].Id; break; }
                    case "unknownPart": { parts.GetArrayElementAtIndex(0).FindPropertyRelative("_id").intValue = 999; break; }
                    case "wrongPartNode": { parts.GetArrayElementAtIndex(0).FindPropertyRelative("_node").objectReferenceValue = _view.Parts[1].Node; break; }
                    case "missingBolt": { bolts.arraySize--; break; }
                    case "duplicateBolt": { bolts.GetArrayElementAtIndex(1).FindPropertyRelative("_id").intValue = _view.Bolts[0].Id; break; }
                    case "unknownBolt": { bolts.GetArrayElementAtIndex(0).FindPropertyRelative("_id").intValue = 999; break; }
                    case "wrongBoltNode": { bolts.GetArrayElementAtIndex(0).FindPropertyRelative("_node").objectReferenceValue = _view.Bolts[1].Node; break; }
                    case "wrongOwner": { bolts.GetArrayElementAtIndex(0).FindPropertyRelative("_ownerId").intValue = 999; break; }
                    case "missingModel": { serialized.FindProperty("_modelRoot").objectReferenceValue = null; break; }
                    case "missingCatalog": { serialized.FindProperty("_contentCatalog").objectReferenceValue = null; break; }
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var before = EditorJsonUtility.ToJson(_view);
            Assert.Throws<InvalidDataException>(() => _view.ValidateBindings());
            Assert.That(EditorJsonUtility.ToJson(_view), Is.EqualTo(before));
        }

        [TestCase("modelOffset")]
        [TestCase("boltsOffset")]
        [TestCase("assemblyScale")]
        [TestCase("rootScale")]
        [TestCase("boltPose")]
        public void MisalignedInstanceIsRejected(string defect)
        {
            LevelViewBindingUtility.Bind(_view);
            switch (defect)
            {
                case "modelOffset": { _view.ModelRoot.localPosition += Vector3.right; break; }
                case "boltsOffset": { _view.BoltsRoot.localPosition += Vector3.up; break; }
                case "assemblyScale": { _view.ModelRoot.parent.localScale *= 2; break; }
                case "rootScale": { _view.LevelRoot.localScale *= 2; break; }
                case "boltPose": { _view.Bolts[0].Node.localPosition += Vector3.forward; break; }
            }

            Assert.Throws<InvalidDataException>(() => _view.ValidateBindings());
        }

        [Test]
        public void ForeignInstanceNodeCannotBeUsedEvenWithMatchingNames()
        {
            LevelViewBindingUtility.Bind(_view);
            var other = (GameObject)PrefabUtility.InstantiatePrefab(_catalog.GetLevel(1).Prefab);
            try
            {
                using (var serialized = new SerializedObject(_view))
                {
                    var node = other.transform.Find(_view.ReadSource().Bolts[0].NodePath);
                    serialized.FindProperty("_bolts").GetArrayElementAtIndex(0).FindPropertyRelative("_node").objectReferenceValue = node;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                Assert.Throws<InvalidDataException>(() => _view.ValidateBindings());
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void BindingRejectsWrongSourceBeforeChangingExistingBindings()
        {
            LevelViewBindingUtility.Bind(_view);
            var other = (GameObject)PrefabUtility.InstantiatePrefab(LevelTestFixtures.LoadCatDraft().GetLevel(1).Prefab);
            other.transform.SetParent(_wrapper.transform, false);
            using (var serialized = new SerializedObject(_view))
            {
                serialized.FindProperty("_levelRoot").objectReferenceValue = other.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var before = EditorJsonUtility.ToJson(_view);
            Assert.Throws<InvalidOperationException>(() => LevelViewBindingUtility.Bind(_view));
            Assert.That(EditorJsonUtility.ToJson(_view), Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(() => LevelViewBindingUtility.Bind(null));
        }
    }
}
