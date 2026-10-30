using System;
using Eflatun.SceneReference;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectBootstrap
{
    public sealed class SceneNavigator : MonoBehaviour
    {
        [SerializeField] private SceneReference _bootstrapScene;
        [SerializeField] private SceneReference _gameplayScene;

        private bool _loadRequested;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public void ToGameplay()
        {
            if (SceneManager.GetActiveScene().path == _bootstrapScene.Path)
            {
                LoadGameplay();
            }
        }

        public void RestartGameplay()
        {
            if (SceneManager.GetActiveScene().path == _gameplayScene.Path)
            {
                LoadGameplay();
            }
        }

        private void LoadGameplay()
        {
            if (_loadRequested)
            {
                return;
            }

            _loadRequested = true;
            try
            {
                if (SceneManager.LoadSceneAsync(_gameplayScene.Path, LoadSceneMode.Single) == null)
                {
                    throw new InvalidOperationException("Unity did not start loading Gameplay.");
                }
            }
            catch
            {
                _loadRequested = false;
                throw;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path == _gameplayScene.Path)
            {
                _loadRequested = false;
            }
        }
    }
}
