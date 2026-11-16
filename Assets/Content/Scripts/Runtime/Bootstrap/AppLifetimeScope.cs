using Screw.Bootstrap;
using Screw.Gameplay;
using Screw.Levels;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Screw
{
    [DisallowMultipleComponent]
    public class AppLifetimeScope : LifetimeScope
    {
        [SerializeField] private LevelContentCatalog _contentCatalog;
        [SerializeField] private SceneNavigator _sceneNavigatorPrefab;
        [SerializeField] private ScreenUi _screenUi = new ScreenUi();

        public LevelContentCatalog ContentCatalog => _contentCatalog;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(_contentCatalog);
            builder.RegisterInstance(_screenUi);
            builder.Register(_ =>
            {
                _contentCatalog.ValidateCampaign();
                return CampaignProgress.Load(_contentCatalog.Levels.Count, CampaignProgress.SaveKey);
            }, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(_sceneNavigatorPrefab, Lifetime.Singleton).DontDestroyOnLoad();
            builder.RegisterEntryPoint<BootstrapEntryPoint>();
        }
    }
}
