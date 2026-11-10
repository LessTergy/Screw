using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Screw.Editor.Levels
{
    [InitializeOnLoad]
    public sealed class MeshRepairReviewWindow : EditorWindow
    {
        [SerializeField] private MeshRepairReview _review;
        [SerializeField] private bool _showColliders = true;
        private Vector2 _scroll;
        private string _message;
        private static MeshRepairReview _activeReview;
        private static MeshRepairReviewWindow _openWindow;

        static MeshRepairReviewWindow()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            SceneView.duringSceneGui += DrawScene;
            EditorApplication.delayCall += () => OnSceneOpened(SceneManager.GetActiveScene(), OpenSceneMode.Single);
        }

        public static void Open(MeshRepairReview review)
        {
            var window = GetWindow<MeshRepairReviewWindow>("Проверка ремонта");
            window._review = review;
            window.minSize = new Vector2(420f, 320f);
            _activeReview = review;
            SceneView.RepaintAll();
        }

        private void OnEnable()
        {
            _openWindow = this;
        }

        private void OnDisable()
        {
            if (_openWindow == this)
            {
                _openWindow = null;
            }
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            _activeReview = null;
            if (string.IsNullOrEmpty(scene.path) || Path.GetFileName(scene.path) != "Comparison.unity")
            {
                return;
            }
            var review = AssetDatabase.LoadAssetAtPath<MeshRepairReview>(
                Path.GetDirectoryName(scene.path).Replace('\\', '/') + "/Review.asset");
            if (review != null)
            {
                Open(review);
            }
        }

        private void OnGUI()
        {
            if (_review == null)
            {
                EditorGUILayout.HelpBox("Откройте сцену сравнения после анализа мешей уровня.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Проблемных деталей: " + _review.Parts.Count, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Слева — исходная деталь, справа — исправленная. «Применить» подтверждает только эту пару "
                + "и заменяет её меш в общем prefab модели. После применения перегенерируйте коллайдеры уровня отдельно.", MessageType.Info);
            if (GUILayout.Button("Открыть сцену сравнения") && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(MeshRepairUtility.ComparisonPath(_review));
            }
            _showColliders = EditorGUILayout.Toggle("Показывать коллайдеры", _showColliders);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < _review.Parts.Count; i++)
            {
                var part = _review.Parts[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(string.IsNullOrEmpty(part.NodePath) ? _review.Source.name : part.NodePath, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(part.Problem, EditorStyles.wordWrappedLabel);
                if (part.Mode == "full")
                {
                    EditorGUILayout.HelpBox("Полный ремонт MeshFix. Треугольники: " + part.TriangleCountBefore + " → "
                        + part.TriangleCountAfter + "; связные части: " + part.ComponentCountBefore + " → " + part.ComponentCountAfter
                        + ". Проверьте сохранность формы, мелких частей, текстур и освещения.", MessageType.Warning);
                }
                if (!string.IsNullOrEmpty(part.Error))
                {
                    EditorGUILayout.HelpBox(part.Error, MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField(part.Applied ? "Применено. Коллайдеры уровня требуют перегенерации."
                        : part.Mode == "full" ? "Ожидает визуального подтверждения."
                        : "Добавлено треугольников: " + part.AddedTriangles, EditorStyles.wordWrappedLabel);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Показать пару"))
                {
                    FramePair(i);
                }
                using (new EditorGUI.DisabledScope(part.Applied || part.After == null || part.Preview == null
                    || !string.IsNullOrEmpty(part.Error) || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Применить эту деталь"))
                    {
                        try
                        {
                            MeshRepairUtility.Apply(_review, i);
                            _message = "Исправление применено: " + part.NodePath;
                        }
                        catch (Exception exception)
                        {
                            _message = exception.Message;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
            if (GUILayout.Button("Показать отчёт проблемных деталей"))
            {
                EditorUtility.RevealInFinder(Path.GetFullPath(Path.GetDirectoryName(AssetDatabase.GetAssetPath(_review)) + "/Report.json"));
            }
            if (GUI.changed)
            {
                SceneView.RepaintAll();
            }
        }

        private void FramePair(int index)
        {
            var scene = SceneManager.GetSceneByPath(MeshRepairUtility.ComparisonPath(_review));
            if (!scene.isLoaded)
            {
                _message = "Сначала откройте сцену сравнения: " + MeshRepairUtility.ComparisonPath(_review);
                return;
            }
            var root = scene.GetRootGameObjects().Single(item => item.name == "Pair_" + index.ToString("D3"));
            Selection.activeGameObject = root;
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Frame(bounds, false);
            }
        }

        private static void DrawScene(SceneView sceneView)
        {
            var scene = SceneManager.GetActiveScene();
            if (_activeReview == null || scene.path != MeshRepairUtility.ComparisonPath(_activeReview))
            {
                return;
            }
            Handles.BeginGUI();
            if (GUI.Button(new Rect(12f, 12f, 260f, 26f), "Проверить и применить исправления"))
            {
                Open(_activeReview);
            }
            Handles.EndGUI();

            if (_openWindow != null && !_openWindow._showColliders)
            {
                return;
            }
            var matrix = Handles.matrix;
            var color = Handles.color;
            try
            {
                Handles.color = Color.green;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var collider in root.GetComponentsInChildren<BoxCollider>())
                    {
                        Handles.matrix = collider.transform.localToWorldMatrix;
                        Handles.DrawWireCube(collider.center, collider.size);
                    }
                }
            }
            finally
            {
                Handles.matrix = matrix;
                Handles.color = color;
            }
        }
    }
}
