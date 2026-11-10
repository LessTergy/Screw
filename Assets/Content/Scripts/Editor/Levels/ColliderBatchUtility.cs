using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Screw.Editor.Levels
{
    public static class ColliderBatchUtility
    {
        public const string BatchReportPath = ".tools/colliders/batch-report.json";

        [MenuItem("Tools/Screw/Levels/Prepare Selected Model Colliders and Report")]
        public static void PrepareSelected()
        {
            Run(Selection.objects.Cast<GameObject>().Select(ResolveModelPath));
        }

        [MenuItem("Tools/Screw/Levels/Prepare All Catalog Colliders and Report")]
        public static void PrepareCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelContentCatalog>(LevelContentCatalogUtility.CatalogAssetPath);
            Run(catalog.Levels.Select(level => ResolveModelPath(level.Prefab)));
        }

        public static string ResolveModelPath(GameObject prefab)
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            if (prefab == null || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Select a level or model prefab asset in Project.");
            }

            var view = prefab.GetComponent<LevelView>();
            var root = view == null ? prefab.transform : view.LevelRoot;
            if (root == null)
            {
                throw new InvalidOperationException("LevelView has no Level Root assigned.");
            }

            var model = root.Find("Assembly/Model");
            if (model == null)
            {
                if (view != null || prefab.GetComponentsInChildren<MeshFilter>(true).Length == 0)
                {
                    throw new InvalidOperationException("Prefab has no level model or mesh parts.");
                }

                return path;
            }

            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(model.gameObject);
            if (source == null)
            {
                throw new InvalidOperationException("Assembly/Model must reference a shared model prefab.");
            }

            return AssetDatabase.GetAssetPath(source);
        }

        public static void Run(IEnumerable<string> paths, bool reviewCopiesOnly = false,
            int resolution = PrimitiveColliderBaker.DefaultResolution, int maxBoxes = PrimitiveColliderBaker.DefaultMaxBoxes,
            float minimumCoverage = PrimitiveColliderBaker.DefaultMinimumCoverage,
            float surfaceInset = PrimitiveColliderBaker.DefaultSurfaceInset,
            float coverageStep = PrimitiveColliderBaker.DefaultCoverageStep)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Prepare colliders outside Play Mode.");
            }

            PrimitiveColliderBaker.ValidateParameters(resolution, maxBoxes, minimumCoverage, surfaceInset, coverageStep);
            var models = paths.Distinct().ToArray();
            if (models.Length == 0)
            {
                throw new ArgumentException("Select at least one shared model prefab.");
            }

            var results = new JArray();
            Directory.CreateDirectory(Path.GetDirectoryName(BatchReportPath));
            try
            {
                for (var i = 0; i < models.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Collider preparation", models[i], (float)i / models.Length))
                    {
                        results.Add(new JObject { ["status"] = "cancelled", ["nextModel"] = models[i] });
                        break;
                    }

                    var result = new JObject { ["source"] = models[i] };
                    results.Add(result);
                    try
                    {
                        PrepareModel(models[i], result, reviewCopiesOnly, resolution, maxBoxes, minimumCoverage, surfaceInset, coverageStep);
                    }
                    catch (Exception exception)
                    {
                        result["status"] = "failed";
                        result["error"] = exception.Message;
                    }
                    finally
                    {
                        File.WriteAllText(BatchReportPath, results.ToString());
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                File.WriteAllText(BatchReportPath, results.ToString());
            }

            Debug.Log("Collider batch finished: " + Path.GetFullPath(BatchReportPath));
        }

        private static void PrepareModel(string path, JObject report, bool reviewCopiesOnly,
            int resolution, int maxBoxes, float minimumCoverage, float surfaceInset, float coverageStep)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Select shared model prefab assets.");
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                throw new ArgumentException("Prefab does not exist: " + path);
            }

            var sourceBytes = File.ReadAllBytes(path);
            var problems = new JArray();
            foreach (var filter in asset.GetComponentsInChildren<MeshFilter>(true))
            {
                try
                {
                    PrimitiveColliderBaker.ValidateTopology(filter.sharedMesh);
                }
                catch (InvalidOperationException exception)
                {
                    var part = AnimationUtility.CalculateTransformPath(filter.transform, asset.transform);
                    problems.Add(new JObject { ["part"] = part, ["error"] = exception.Message });
                }
            }

            report["geometryProblems"] = problems;
            var target = path;
            if (problems.Count != 0)
            {
                throw new InvalidOperationException("Mesh problems found. Use Assets > Screw > Analyze Level Meshes... before generating colliders.");
            }

            if (reviewCopiesOnly)
            {
                target = CreateReviewDirectory(path) + "/Colliders.prefab";
                if (!AssetDatabase.CopyAsset(path, target))
                {
                    throw new InvalidOperationException("Could not copy the model prefab.");
                }
            }

            report["target"] = target;
            report["qualityReport"] = PrimitiveColliderBaker.ReportPath(target);
            report["boxes"] = PrimitiveColliderBaker.BakePrefab(target, resolution, maxBoxes,
                minimumCoverage, surfaceInset: surfaceInset, coverageStep: coverageStep);
            if (target != path)
            {
                report["sourcePrefabUnchanged"] = sourceBytes.SequenceEqual(File.ReadAllBytes(path));
                if (!(bool)report["sourcePrefabUnchanged"])
                {
                    throw new InvalidOperationException("Source prefab changed during review preparation.");
                }
            }
            report["status"] = "prepared";
        }

        internal static void RunRepair(string input, string output)
        {
            var python = Path.GetFullPath(".tools/coacd/Scripts/python.exe");
            if (!File.Exists(python))
            {
                throw new InvalidOperationException("Run Tools/Colliders/Install.ps1 first.");
            }

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(python,
                    "\"" + Path.GetFullPath("Tools/Colliders/repair_meshes.py") + "\" \"" + Path.GetFullPath(input)
                    + "\" \"" + Path.GetFullPath(output) + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                process.Start();
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000))
                {
                    process.Kill();
                    throw new InvalidOperationException("Mesh repair timed out; original prefab was not changed.");
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Mesh repair failed: " + stderr.GetAwaiter().GetResult()
                        + stdout.GetAwaiter().GetResult());
                }
            }
        }

        internal static string CreateReviewDirectory(string source)
        {
            const string parent = "Assets/Content/ModelRepairs";
            if (!AssetDatabase.IsValidFolder(parent))
            {
                AssetDatabase.CreateFolder("Assets/Content", "ModelRepairs");
            }

            var directory = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + Path.GetFileNameWithoutExtension(source));
            AssetDatabase.CreateFolder(parent, Path.GetFileName(directory));
            return directory;
        }
    }
}
