using System;
using Screw.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Screw.Editor.Levels
{
    public static class LevelViewBindingUtility
    {
        [MenuItem("Tools/Screw/Levels/Bind Selected Level View")]
        public static void BindSelected()
        {
            var view = SelectedView();
            Bind(view);
            Debug.Log("Bound level " + view.LevelNumber + ": " + view.Parts.Count + " parts and " + view.Bolts.Count + " bolts.", view);
        }

        [MenuItem("Tools/Screw/Levels/Validate Selected Level View")]
        public static void ValidateSelected()
        {
            var view = SelectedView();
            Validate(view);
            Debug.Log("Validated level " + view.LevelNumber + " instance bindings.", view);
        }

        public static void Bind(LevelView view)
        {
            Bind(view, requireCampaignPaths: true);
        }

        internal static void Bind(LevelView view, bool requireCampaignPaths)
        {
            var source = ValidateInstanceSource(view, requireCampaignPaths);
            var root = view.LevelRoot;
            using (var serialized = new SerializedObject(view))
            {
                serialized.FindProperty("_assemblyRoot").objectReferenceValue = root.Find("Assembly");
                serialized.FindProperty("_modelRoot").objectReferenceValue = root.Find("Assembly/Model");
                serialized.FindProperty("_boltsRoot").objectReferenceValue = root.Find("Assembly/Bolts");
                var parts = serialized.FindProperty("_parts");
                parts.arraySize = source.Parts.Length;
                for (var i = 0; i < source.Parts.Length; i++)
                {
                    var data = source.Parts[i];
                    var item = parts.GetArrayElementAtIndex(i);
                    item.FindPropertyRelative("_id").intValue = data.Id;
                    item.FindPropertyRelative("_node").objectReferenceValue = root.Find(data.NodePath);
                }

                var bolts = serialized.FindProperty("_bolts");
                bolts.arraySize = source.Bolts.Length;
                for (var i = 0; i < source.Bolts.Length; i++)
                {
                    var data = source.Bolts[i];
                    var item = bolts.GetArrayElementAtIndex(i);
                    item.FindPropertyRelative("_id").intValue = data.Id;
                    item.FindPropertyRelative("_ownerId").intValue = data.OwnerId;
                    item.FindPropertyRelative("_node").objectReferenceValue = root.Find(data.NodePath);
                }

                serialized.ApplyModifiedProperties();
            }

            view.ValidateBindings();
        }

        public static void Validate(LevelView view)
        {
            ValidateInstanceSource(view);
            view.ValidateBindings();
        }

        private static LevelSourceData ValidateInstanceSource(LevelView view, bool requireCampaignPaths = true)
        {
            if (view == null || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorUtility.IsPersistent(view) || PrefabStageUtility.GetPrefabStage(view.gameObject) != null)
            {
                throw new InvalidOperationException("Select a LevelView in a regular scene outside Play Mode and Prefab Mode.");
            }

            var source = view.ReadSource();
            if (view.LevelRoot == null || view.LevelRoot.parent != view.transform)
            {
                throw new InvalidOperationException("Assign a level prefab instance directly under the separate LevelView object.");
            }

            var expectedPrefab = view.ContentCatalog.GetLevel(view.LevelNumber).Prefab;
            if (PrefabUtility.GetCorrespondingObjectFromSource(view.LevelRoot.gameObject) != expectedPrefab)
            {
                throw new InvalidOperationException("Level Root must be an instance of the prefab referenced by the content catalog.");
            }

            LevelContentCatalogUtility.ValidateReferences(view.ContentCatalog, requireCampaignPaths ? (int?)null : view.LevelNumber);
            LevelContentCatalogUtility.ValidateLevelPrefab(source, view.LevelRoot.gameObject);
            return source;
        }

        private static LevelView SelectedView()
        {
            var selected = Selection.activeGameObject;
            if (selected == null || !selected.TryGetComponent<LevelView>(out var view))
            {
                throw new InvalidOperationException("Select the separate scene object with a LevelView component.");
            }

            return view;
        }
    }
}
