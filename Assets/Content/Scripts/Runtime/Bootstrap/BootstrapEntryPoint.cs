using System;
using Screw.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Screw.Bootstrap
{
    public sealed class BootstrapEntryPoint : IStartable, IDisposable
    {
        private readonly SceneNavigator _sceneNavigator;
        private readonly CampaignProgress _campaign;
        private readonly ScreenUi _screenUi;
        private MainMenuView _menu;
        private bool _started;

        public BootstrapEntryPoint(SceneNavigator sceneNavigator, CampaignProgress campaign, ScreenUi screenUi)
        {
            _sceneNavigator = sceneNavigator;
            _campaign = campaign;
            _screenUi = screenUi;
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            if (Application.platform == RuntimePlatform.Android)
            {
                Application.targetFrameRate = 120;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            ShowMenu(SceneManager.GetActiveScene());
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ShowMenu(scene);
        }

        private void ShowMenu(Scene scene)
        {
            if (!_sceneNavigator.IsBootstrap(scene) || _menu != null)
            {
                return;
            }

            _menu = MainMenuView.Create(scene, _campaign, _sceneNavigator, _screenUi);
        }
    }
}
