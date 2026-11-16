using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Eflatun.SceneReference;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Screw.Bootstrap
{
    public sealed class SceneNavigator : MonoBehaviour
    {
        [SerializeField] private SceneReference _bootstrapScene;
        [SerializeField] private SceneReference _gameplayScene;

        private bool _loadRequested;
        private bool _prewarmGameplay;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        internal bool IsBootstrap(Scene scene)
        {
            return scene.path == _bootstrapScene.Path;
        }

        public bool ToGameplay()
        {
            return IsBootstrap(SceneManager.GetActiveScene()) && LoadScene(_gameplayScene, prewarm: true);
        }

        public bool RestartGameplay()
        {
            return SceneManager.GetActiveScene().path == _gameplayScene.Path && LoadScene(_gameplayScene);
        }

        public bool ReturnToMenu()
        {
            return SceneManager.GetActiveScene().path == _gameplayScene.Path && LoadScene(_bootstrapScene);
        }

        internal void PrewarmGameplay(Camera camera, Transform modelRoot, Material partPreviewMaterial, CancellationToken cancellationToken)
        {
            if (!_prewarmGameplay)
            {
                return;
            }

            _prewarmGameplay = false;
            GameplayShaderWarmup.RunAsync(camera, modelRoot, partPreviewMaterial, cancellationToken).Forget(Debug.LogException);
        }

        private bool LoadScene(SceneReference target, bool prewarm = false)
        {
            if (_loadRequested)
            {
                return false;
            }

            _loadRequested = true;
            _prewarmGameplay = prewarm;
            try
            {
                if (SceneManager.LoadSceneAsync(target.Path, LoadSceneMode.Single) == null)
                {
                    throw new InvalidOperationException("Unity did not start loading " + target.Path + ".");
                }

                return true;
            }
            catch
            {
                _loadRequested = false;
                _prewarmGameplay = false;
                throw;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (IsBootstrap(scene) || scene.path == _gameplayScene.Path)
            {
                _loadRequested = false;
            }
        }
    }
}
