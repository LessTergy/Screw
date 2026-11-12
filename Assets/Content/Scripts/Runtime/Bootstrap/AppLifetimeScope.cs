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

        public LevelContentCatalog ContentCatalog => _contentCatalog;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(_contentCatalog);
            builder.Register(_ =>
            {
                _contentCatalog.ValidateCampaign();
                return new CampaignProgress(_contentCatalog.Levels.Count);
            }, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(_sceneNavigatorPrefab, Lifetime.Singleton).DontDestroyOnLoad();
            builder.RegisterEntryPoint<BootstrapEntryPoint>();
        }
    }
}
