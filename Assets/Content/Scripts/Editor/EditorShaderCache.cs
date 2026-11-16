using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Screw.Editor
{
    internal static class EditorShaderCache
    {
        private const string MenuPath = "Tools/Screw/Rendering/Clear Shader Cache";

        [MenuItem(MenuPath, true)]
        private static bool CanClear()
        {
            return !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(MenuPath)]
        private static void Clear()
        {
            if (ShaderUtil.anythingCompiling)
            {
                Debug.LogWarning("Wait for shader compilation to finish, then clear the cache again.");
                return;
            }

            var libraryPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
            var cache = new DirectoryInfo(Path.Combine(libraryPath, "ShaderCache"));
            try
            {
                if (!cache.Exists)
                {
                    Debug.Log($"Editor shader cache is already absent at '{cache.FullName}'. Restart Unity before testing.");
                    return;
                }

                var attributes = File.GetAttributes(libraryPath) | cache.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Debug.LogError($"Cannot clear the editor shader cache through a filesystem link at '{cache.FullName}'.");
                    return;
                }

                cache.Delete(recursive: true);
                if (Directory.Exists(cache.FullName))
                {
                    Debug.LogWarning($"Unity recreated '{cache.FullName}'. Close Unity and clear that directory before reopening.");
                    return;
                }

                Debug.Log($"Cleared editor shader cache at '{cache.FullName}'. Restart Unity before testing.");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogError($"Could not clear the editor shader cache at '{cache.FullName}'. {exception.Message}");
            }
        }
    }
}
