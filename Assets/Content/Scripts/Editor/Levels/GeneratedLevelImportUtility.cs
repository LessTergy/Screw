using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Screw.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Screw.Editor.Levels
{
    public static class GeneratedLevelImportUtility
    {
        private const string MenuPath = "Tools/Screw/Levels/Import Generated Level";
        private const string OutputRoot = "Assets/Content/GeneratedLevels";
        private const string ViewFile = "/Level_View.prefab";
        private const string RouteFile = "/Level_Route.json";
        private const string GameplayFile = "/Level_Gameplay.asset";
        private const string ReportFile = "/ImportReport.json";
        private const string BoltPalettePath = "Assets/Content/3D/Bolts/BoltPalette.asset";
        private const int DraftLevelNumber = 1;

        [MenuItem(MenuPath)]
        private static void ImportSelectedFolder()
        {
            var folder = EditorUtility.OpenFolderPanel("Пакет сгенерированного уровня", Path.GetFullPath("ArtSource"), "");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            try
            {
                var view = Import(folder);
                Selection.activeObject = view;
                EditorGUIUtility.PingObject(view);
                Debug.Log("Сгенерированный уровень подготовлен: " + AssetDatabase.GetAssetPath(view), view);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Импорт уровня", exception.Message, "OK");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanImport()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
                && PrefabStageUtility.GetCurrentPrefabStage() == null;
        }

        public static GameObject Import(string folder)
        {
            if (!CanImport())
            {
                throw new InvalidOperationException("Откройте обычную сцену вне Play Mode и Prefab Mode.");
            }

            var package = GeneratedLevelPackage.Read(folder);
            var palette = Load<BoltPalette>(BoltPalettePath);
            var finalFolder = OutputRoot + "/" + package.Metadata.ModelName + "-" + package.ManifestHash.Substring(0, 12);
            if (Directory.Exists(finalFolder))
            {
                return ReadPreparedDraft(finalFolder, package, palette);
            }

            var source = package.CreateSource(DraftLevelNumber, new[] { 1, 2 }, finalFolder);
            var boltPrefab = palette.Prefab;
            Require(boltPrefab != null && boltPrefab.TryGetComponent<BoxCollider>(out var head)
                && head.enabled && !head.isTrigger, "У общего prefab болта нет активного BoxCollider головки.");
            if (!AssetDatabase.IsValidFolder(OutputRoot))
            {
                Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/Content", "GeneratedLevels")),
                    "Не удалось создать папку черновиков.");
            }

            var stageGuid = AssetDatabase.CreateFolder(OutputRoot, "_Import_" + Guid.NewGuid().ToString("N"));
            Require(!string.IsNullOrEmpty(stageGuid), "Не удалось создать временную папку импорта.");
            var stage = AssetDatabase.GUIDToAssetPath(stageGuid);
            var published = false;
            try
            {
                EditorUtility.DisplayProgressBar("Импорт уровня", "Геометрия и крепления", 0.15f);
                var glbPath = stage + "/Model.glb";
                File.WriteAllBytes(glbPath, package.Glb);
                AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceSynchronousImport);
                var imported = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
                Require(imported != null, "Unity не импортировала GLB. Проверьте glTFast и сообщения Console.");

                var colliderCount = BuildPrefabs(stage, imported, boltPrefab, package, source);
                EditorUtility.DisplayProgressBar("Импорт уровня", "Маршрут и игровые данные", 0.75f);
                var draftCatalog = CreateDraftCatalog(stage, palette, source, package.ManifestHash);
                var route = PreparedLevelUtility.Prepare(draftCatalog, source.Number, stage + ViewFile, stage + RouteFile,
                    requireCampaignPaths: false);
                var gameplay = LevelGameplayDefinitionUtility.Build(source, route.Steps.Select(step => step.BoltId).ToArray());
                AssetDatabase.CreateAsset(LevelGameplayDefinitionUtility.Create(draftCatalog, gameplay), stage + GameplayFile);
                WriteJson(stage + ReportFile, new JObject
                {
                    ["status"] = "prepared",
                    ["manifestSha256"] = package.ManifestHash,
                    ["glbSha256"] = package.Metadata.GlbSha256,
                    ["importedGlbSha256"] = package.ImportGlbHash,
                    ["parts"] = source.Parts.Length,
                    ["bolts"] = source.Bolts.Length,
                    ["colliders"] = colliderCount,
                    ["replayedSteps"] = route.Steps.Length,
                    ["physicsPlaytest"] = "not_checked"
                });

                var error = AssetDatabase.MoveAsset(stage, finalFolder);
                Require(string.IsNullOrEmpty(error), error);
                var view = ReadPreparedDraft(finalFolder, package, palette);
                published = true;
                return view;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                var createdFolder = AssetDatabase.GUIDToAssetPath(stageGuid);
                if (!published && (createdFolder == stage || createdFolder == finalFolder) && !AssetDatabase.DeleteAsset(createdFolder))
                {
                    Debug.LogWarning("Не удалось убрать папку незавершённого импорта: " + createdFolder);
                }
            }
        }

        private static int BuildPrefabs(string folder, GameObject imported, GameObject boltPrefab, GeneratedLevelPackage package,
            LevelSourceData source)
        {
            var assemblies = imported.GetComponentsInChildren<Transform>(true).Where(node => node.name == "Assembly").ToArray();
            Require(assemblies.Length == 1, "GLB должен содержать один узел Assembly.");
            var assembly = assemblies[0];
            for (var parent = assembly.parent; parent != null; parent = parent.parent)
            {
                ValidateIdentity(parent);
            }

            var model = assembly.Find("Model");
            var markers = assembly.Find("Bolts");
            Require(model != null && markers != null && assembly.childCount == 2
                && model.childCount == source.Parts.Length && markers.childCount == source.Bolts.Length,
                "Иерархия GLB или количество узлов не совпадает с манифестом.");
            ValidatePose(assembly);
            ValidatePose(model);
            ValidateIdentity(markers);
            Require(assembly.localPosition.sqrMagnitude < 1e-8f, "Assembly должна быть в начале координат.");

            var originalScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var modelRoot = new GameObject("Model").transform;
                var partMeshes = package.Metadata.Parts.ToDictionary(part => part.Id, part => part.TriangleCount);
                var owners = source.Parts.ToDictionary(part => part.Id, part => model.Find(part.Name));
                foreach (var part in source.Parts)
                {
                    var node = owners[part.Id];
                    Require(node != null && node.childCount == 0 && node.gameObject.activeSelf,
                        "Ожидался отдельный активный меш детали: " + part.NodePath);
                    ValidatePose(node);
                    var filter = node.GetComponent<MeshFilter>();
                    var renderer = node.GetComponent<MeshRenderer>();
                    Require(filter != null && filter.sharedMesh != null && renderer != null
                        && renderer.sharedMaterials.Length != 0 && renderer.sharedMaterials.All(material => material != null),
                        "У детали отсутствует меш или материал: " + part.Name);
                    var mesh = filter.sharedMesh;
                    ValidateMesh(mesh, partMeshes[part.Id], part.Name);

                    var copy = new GameObject(part.Name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                    copy.SetParent(modelRoot, false);
                    CopyPose(node, copy);
                    copy.GetComponent<MeshFilter>().sharedMesh = mesh;
                    copy.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                }

                var modelPath = folder + "/Model.prefab";
                SavePrefab(modelRoot.gameObject, modelPath);
                Object.DestroyImmediate(modelRoot.gameObject);
                EditorUtility.DisplayProgressBar("Импорт уровня", "Примитивные коллайдеры", 0.45f);
                var count = PrimitiveColliderBaker.BakePrefab(modelPath);
                var preparedModel = Load<GameObject>(modelPath);
                Require(count > 0 && preparedModel.GetComponentsInChildren<MeshFilter>(true)
                    .All(filter => filter.GetComponentsInChildren<BoxCollider>(true).Any(box => box.enabled && !box.isTrigger)),
                    "Коллайдеры созданы не для всех деталей.");

                var root = new GameObject("Level_" + source.Number.ToString("D2")).transform;
                var assemblyRoot = new GameObject("Assembly").transform;
                assemblyRoot.SetParent(root, false);
                assemblyRoot.localRotation = assembly.localRotation;
                assemblyRoot.localScale = assembly.localScale;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(preparedModel, assemblyRoot);
                CopyPose(model, instance.transform);
                var boltsRoot = new GameObject("Bolts").transform;
                boltsRoot.SetParent(assemblyRoot, false);
                foreach (var bolt in source.Bolts)
                {
                    var marker = markers.Find(Path.GetFileName(bolt.NodePath));
                    Require(marker != null && marker.childCount == 0 && marker.GetComponent<Renderer>() == null,
                        "Отсутствует маркер: " + bolt.NodePath);
                    ValidatePose(marker);
                    Require((marker.localScale - Vector3.one).sqrMagnitude < 1e-8f, "Масштаб маркера должен быть единичным.");
                    // Blender exports the marker's local +Z as glTF +Y. The Unity bolt points along local +Z.
                    var boltRotation = marker.localRotation * Quaternion.Euler(-90f, 0f, 0f);
                    var outward = markers.TransformDirection(boltRotation * Vector3.forward);
                    ValidateMount(marker.position, outward, owners[bolt.OwnerId], 0.05f * assembly.localScale.x, bolt.Id);
                    var boltObject = (GameObject)PrefabUtility.InstantiatePrefab(boltPrefab, boltsRoot);
                    boltObject.name = marker.name;
                    boltObject.transform.localPosition = marker.localPosition;
                    boltObject.transform.localRotation = boltRotation;
                    boltObject.transform.localScale = Vector3.one;
                    bolt.Position = Vector(boltObject.transform.localPosition);
                    bolt.EulerDegrees = Vector(boltObject.transform.localEulerAngles);
                }

                source.ModelPosition = Vector(instance.transform.localPosition);
                source.ModelEulerDegrees = Vector(instance.transform.localEulerAngles);
                source.ModelScale = instance.transform.localScale.x;
                source.RootEulerDegrees = Vector(assemblyRoot.localEulerAngles);
                source.RootScale = assemblyRoot.localScale.x;
                var prefab = SavePrefab(root.gameObject, folder + "/Level.prefab");
                LevelContentCatalogUtility.ValidateLevelPrefab(source, prefab);
                return count;
            }
            finally
            {
                SceneManager.SetActiveScene(originalScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static LevelContentCatalog CreateDraftCatalog(string folder, BoltPalette palette, LevelSourceData source, string manifestHash)
        {
            var colors = palette.Colors.OrderBy(entry => entry.Index).ToArray();
            Require(colors.All(entry => entry.Primary != null && entry.Secondary != null), "В палитре болта отсутствует материал.");
            var boltPrefabPath = AssetDatabase.GetAssetPath(palette.Prefab);
            var boltMeshPath = AssetDatabase.GetAssetPath(palette.Prefab.GetComponentInChildren<MeshFilter>().sharedMesh);
            var data = new LevelSourceCatalogData
            {
                SchemaVersion = LevelDataReader.SupportedSchemaVersion,
                PartCount = source.Parts.Length,
                BoltCount = source.Bolts.Length,
                BoltDefaultPaletteIndex = palette.DefaultIndex,
                SourceCommit = "manifest-sha256:" + manifestHash,
                SourceUnityVersion = Application.unityVersion,
                UnityRenderPipeline = "Universal Render Pipeline",
                CoordinateSystem = "Unity left-handed, Y up",
                BoltColors = "Screw bolt palette",
                RootScreenRatio = 1,
                Levels = new[] { source },
                BoltPalette = colors.Select(entry => new BoltPaletteData
                {
                    Index = entry.Index,
                    Primary = AssetDatabase.GetAssetPath(entry.Primary),
                    Secondary = AssetDatabase.GetAssetPath(entry.Secondary),
                    Prefab = boltPrefabPath,
                    Glb = boltMeshPath,
                    PrimarySrgb = MaterialColor(entry.Primary),
                    SecondarySrgb = MaterialColor(entry.Secondary)
                }).ToArray()
            };
            LevelDataValidator.ValidateCatalog(data);
            var catalogText = WriteJson(folder + "/catalog.json", JToken.FromObject(data));
            var levelText = WriteJson(folder + "/Level.json", JToken.FromObject(source));
            var draftCatalog = ScriptableObject.CreateInstance<LevelContentCatalog>();
            using (var serialized = new SerializedObject(draftCatalog))
            {
                serialized.FindProperty("_sourceCatalog").objectReferenceValue = catalogText;
                var levels = serialized.FindProperty("_levels");
                levels.arraySize = 1;
                var level = levels.GetArrayElementAtIndex(0);
                level.FindPropertyRelative("_number").intValue = source.Number;
                level.FindPropertyRelative("_data").objectReferenceValue = levelText;
                level.FindPropertyRelative("_prefab").objectReferenceValue = Load<GameObject>(folder + "/Level.prefab");
                var entries = serialized.FindProperty("_palette");
                entries.arraySize = colors.Length;
                for (var index = 0; index < colors.Length; index++)
                {
                    var entry = entries.GetArrayElementAtIndex(index);
                    entry.FindPropertyRelative("_index").intValue = colors[index].Index;
                    entry.FindPropertyRelative("_prefab").objectReferenceValue = palette.Prefab;
                    entry.FindPropertyRelative("_primary").objectReferenceValue = colors[index].Primary;
                    entry.FindPropertyRelative("_secondary").objectReferenceValue = colors[index].Secondary;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.CreateAsset(draftCatalog, folder + "/DraftCatalog.asset");
            return draftCatalog;
        }

        private static double[] MaterialColor(Material material)
        {
            var color = material.GetColor("_BaseColor");
            return new double[] { color.r, color.g, color.b, color.a };
        }

        private static GameObject ReadPreparedDraft(string folder, GeneratedLevelPackage package, BoltPalette palette)
        {
            var report = JObject.Parse(Load<TextAsset>(folder + ReportFile).text);
            Require((string)report["status"] == "prepared" && (string)report["manifestSha256"] == package.ManifestHash
                && string.Equals(GeneratedLevelPackage.Hash(File.ReadAllBytes(folder + "/Model.glb")),
                    package.ImportGlbHash, StringComparison.OrdinalIgnoreCase), "Папка занята другим или неполным импортом: " + folder);
            var prefab = Load<GameObject>(folder + ViewFile);
            var view = prefab.GetComponent<LevelView>();
            Require(view != null, "В подготовленном prefab отсутствует LevelView.");
            var source = view.ValidateBindings();
            var catalog = view.ContentCatalog;
            Require(catalog.Levels.Count == 1 && catalog.Palette.All(entry => entry.Prefab == palette.Prefab)
                && view.Bolts.All(bolt => PrefabUtility.GetCorrespondingObjectFromOriginalSource(bolt.Node.gameObject) == palette.Prefab),
                "Черновик использует прежний prefab болта и требует повторной подготовки: " + folder);
            LevelContentCatalogUtility.ValidateReferences(catalog);
            Require(LevelDataReader.ReadCatalog(catalog.SourceCatalog.text).BoltDefaultPaletteIndex == palette.DefaultIndex
                && palette.Colors.All(entry => catalog.GetColor(entry.Index).Primary == entry.Primary
                    && catalog.GetColor(entry.Index).Secondary == entry.Secondary),
                "Черновик использует прежнюю палитру и требует повторной подготовки: " + folder);
            Require(view.Parts.All(part => part.Node.GetComponentsInChildren<BoxCollider>(true)
                .Any(box => box.enabled && !box.isTrigger)), "В подготовленном prefab отсутствуют коллайдеры деталей.");
            LevelRouteData.Read(Load<TextAsset>(folder + RouteFile).text, source);
            var gameplay = Load<LevelGameplayDefinition>(folder + GameplayFile);
            Require(gameplay.ContentCatalog == view.ContentCatalog, "Игровое описание ссылается на другой каталог.");
            gameplay.ReadSource();
            return prefab;
        }

        private static void ValidateMesh(Mesh mesh, int triangleCount, string partName)
        {
            var triangles = mesh.triangles;
            Require(triangles.Length == triangleCount * 3L, "Число треугольников отличается: " + partName);
            PrimitiveColliderBaker.ValidateTopology(mesh);
            var vertices = mesh.vertices;
            var scale = mesh.bounds.size.magnitude;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = (vertices[triangles[i + 1]] - a) / scale;
                var c = (vertices[triangles[i + 2]] - a) / scale;
                Require(Vector3.Cross(b, c).sqrMagnitude > 1e-16f, "Вырожденный треугольник детали " + partName + ".");
            }
        }

        private static void ValidateMount(Vector3 position, Vector3 direction, Transform owner, float reach, int boltId)
        {
            var mesh = owner.GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;
            Require(normals.Length == vertices.Length, "Отсутствуют нормали для проверки крепления " + boltId + ".");

            var origin = owner.InverseTransformPoint(position + direction * reach);
            var ray = owner.InverseTransformVector(-direction);
            var nearest = 2f * reach;
            var facing = false;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = triangles[i];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                var edge1 = vertices[b] - vertices[a];
                var edge2 = vertices[c] - vertices[a];
                var p = Vector3.Cross(ray, edge2);
                var determinant = Vector3.Dot(edge1, p);
                if (Mathf.Abs(determinant) <= 1e-8f)
                {
                    continue;
                }

                var t = origin - vertices[a];
                var u = Vector3.Dot(t, p) / determinant;
                var q = Vector3.Cross(t, edge1);
                var v = Vector3.Dot(ray, q) / determinant;
                var distance = Vector3.Dot(edge2, q) / determinant;
                if (u < -1e-5f || v < -1e-5f || u + v > 1f + 1e-5f || distance < 0 || distance >= nearest)
                {
                    continue;
                }

                nearest = distance;
                var normal = normals[a] * (1f - u - v) + normals[b] * u + normals[c] * v;
                facing = Vector3.Dot(owner.TransformDirection(normal.normalized), direction) > 0.5f;
            }

            Require(facing, "Крепление " + boltId + " не направлено наружу у поверхности детали " + owner.name + ".");
        }

        private static void ValidatePose(Transform node)
        {
            var scale = node.localScale;
            Require(Finite(node.localPosition) && Finite(node.localEulerAngles) && Finite(scale)
                && scale.x > 0 && Mathf.Abs(scale.x - scale.y) < 1e-5f && Mathf.Abs(scale.x - scale.z) < 1e-5f,
                "Некорректная поза или масштаб узла " + node.name + ".");
        }

        private static void ValidateIdentity(Transform node)
        {
            ValidatePose(node);
            Require(node.localPosition.sqrMagnitude < 1e-8f && Quaternion.Angle(node.localRotation, Quaternion.identity) < 0.01f
                && (node.localScale - Vector3.one).sqrMagnitude < 1e-8f, "Ожидался единичный transform узла " + node.name + ".");
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
        }

        private static void CopyPose(Transform source, Transform target)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        private static double[] Vector(Vector3 value)
        {
            return new double[] { value.x, value.y, value.z };
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
            Require(saved && asset != null, "Не удалось сохранить prefab: " + path);
            return asset;
        }

        private static TextAsset WriteJson(string path, JToken json)
        {
            File.WriteAllText(path, json.ToString());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return Load<TextAsset>(path);
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Require(asset != null, "Отсутствует asset: " + path);
            return asset;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
