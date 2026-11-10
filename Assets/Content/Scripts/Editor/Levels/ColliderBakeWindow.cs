using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Screw.Editor.Levels
{
    public sealed class ColliderBakeWindow : EditorWindow
    {
        private const string MenuPath = "Assets/Screw/Generate Colliders...";

        [SerializeField] private GameObject _prefab;
        [SerializeField] private string _modelPath;
        [SerializeField] private int _resolution = PrimitiveColliderBaker.DefaultResolution;
        [SerializeField] private int _maxBoxes = PrimitiveColliderBaker.DefaultMaxBoxes;
        [SerializeField] private float _coveragePercent = PrimitiveColliderBaker.DefaultMinimumCoverage * 100f;
        [SerializeField] private float _insetPercent = PrimitiveColliderBaker.DefaultSurfaceInset * 100f;
        [SerializeField] private float _stepPercent = PrimitiveColliderBaker.DefaultCoverageStep * 100f;
        [SerializeField] private string _message;
        [SerializeField] private MessageType _messageType;
        [SerializeField] private string _qualityReport;

        [MenuItem(MenuPath, false, 2000)]
        public static void Open()
        {
            var window = GetWindow<ColliderBakeWindow>("Коллайдеры уровня");
            window.minSize = new Vector2(480f, 450f);
            window._prefab = (GameObject)Selection.activeObject;
            window._modelPath = string.Empty;
            window._message = string.Empty;
            window._qualityReport = string.Empty;
            window.ResetDefaults();
            try
            {
                window._modelPath = ColliderBatchUtility.ResolveModelPath(window._prefab);
            }
            catch (Exception exception)
            {
                window._message = exception.Message;
                window._messageType = MessageType.Error;
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanOpen()
        {
            return Selection.objects.Length == 1 && Selection.activeObject is GameObject
                && AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Выбранный prefab", _prefab, typeof(GameObject), false);
                EditorGUILayout.ObjectField("Общая модель", AssetDatabase.LoadAssetAtPath<GameObject>(_modelPath ?? string.Empty),
                    typeof(GameObject), false);
            }

            EditorGUILayout.HelpBox("Коллайдеры сохраняются в общем prefab модели. Все уровни с этой моделью получат изменения. "
                + "Проверка и ремонт мешей запускаются отдельно: ПКМ → Screw → Analyze Level Meshes...", MessageType.Info);
            _maxBoxes = EditorGUILayout.IntField(new GUIContent("Боксов на деталь", "От 1 до 256."), _maxBoxes);
            _coveragePercent = EditorGUILayout.FloatField(new GUIContent("Начальное покрытие, %", "Больше 0 и до 100. Покрытие внутренней области после отступа."), _coveragePercent);
            _insetPercent = EditorGUILayout.FloatField(new GUIContent("Отступ от поверхности, %", "От 0 до 50, не включая 50. Доля наименьшего габарита детали."), _insetPercent);
            _stepPercent = EditorGUILayout.FloatField(new GUIContent("Шаг снижения, п.п.", "От 0,001 до 100 процентных пунктов. Например: 90 → 85 → 80 при шаге 5."), _stepPercent);
            _resolution = EditorGUILayout.IntField(new GUIContent("Разрешение анализа", "От 4 до 64. Большее значение увеличивает время и память."), _resolution);
            if (GUILayout.Button("Вернуть значения по умолчанию"))
            {
                ResetDefaults();
            }

            var valid = true;
            try
            {
                PrimitiveColliderBaker.ValidateParameters(_resolution, _maxBoxes, _coveragePercent / 100f,
                    _insetPercent / 100f, _stepPercent / 100f);
            }
            catch (ArgumentOutOfRangeException)
            {
                valid = false;
                EditorGUILayout.HelpBox("Параметры вне допустимого диапазона. Диапазоны указаны в подсказках полей.", MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(!valid || string.IsNullOrEmpty(_modelPath)
                || AssetDatabase.LoadAssetAtPath<GameObject>(_modelPath) == null
                || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Перегенерировать коллайдеры"))
                {
                    Generate();
                }
            }

            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.HelpBox(_message, _messageType);
            }
            if (!string.IsNullOrEmpty(_qualityReport) && File.Exists(_qualityReport) && GUILayout.Button("Показать отчёт"))
            {
                EditorUtility.RevealInFinder(Path.GetFullPath(_qualityReport));
            }
        }

        private void ResetDefaults()
        {
            _resolution = PrimitiveColliderBaker.DefaultResolution;
            _maxBoxes = PrimitiveColliderBaker.DefaultMaxBoxes;
            _coveragePercent = PrimitiveColliderBaker.DefaultMinimumCoverage * 100f;
            _insetPercent = PrimitiveColliderBaker.DefaultSurfaceInset * 100f;
            _stepPercent = PrimitiveColliderBaker.DefaultCoverageStep * 100f;
        }

        private void Generate()
        {
            _qualityReport = string.Empty;
            try
            {
                ColliderBatchUtility.Run(new[] { _modelPath }, resolution: _resolution, maxBoxes: _maxBoxes,
                    minimumCoverage: _coveragePercent / 100f, surfaceInset: _insetPercent / 100f,
                    coverageStep: _stepPercent / 100f);
                var result = JArray.Parse(File.ReadAllText(ColliderBatchUtility.BatchReportPath))[0];
                if ((string)result["status"] == "cancelled")
                {
                    _message = "Генерация отменена.";
                    _messageType = MessageType.Info;
                    return;
                }

                _qualityReport = (string)result["qualityReport"];
                if ((string)result["status"] == "failed")
                {
                    throw new InvalidOperationException((string)result["error"]);
                }

                _message = "Готово. Коллайдеров: " + result["boxes"] + ".";
                _messageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                _message = exception.Message;
                _messageType = MessageType.Error;
            }
        }
    }
}
