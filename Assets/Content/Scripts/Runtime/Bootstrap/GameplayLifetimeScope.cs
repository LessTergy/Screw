using System.IO;
using Screw.Gameplay;
using Screw.Levels;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Screw
{
    [DisallowMultipleComponent]
    public class GameplayLifetimeScope : LifetimeScope
    {
        [SerializeField] private Transform _levelAnchor;
        [SerializeField] private Camera _sceneCamera;
        [SerializeField] private Light _sceneLight;
        [SerializeField] private Transform _ground;
        [SerializeField] private BoltTurnOperations _turnOperations;
        [SerializeField] private GameplaySession _gameplaySession;
        [SerializeField, Min(0.01f)] private float _partRemovalMargin = 1f;

        public Transform LevelAnchor
        {
            get { return _levelAnchor; }
        }

        public Camera SceneCamera
        {
            get { return _sceneCamera; }
        }

        public Light SceneLight
        {
            get { return _sceneLight; }
        }

        public Transform Ground
        {
            get { return _ground; }
        }

        public LevelView LevelView { get; private set; }

        public GameplaySession GameplaySession
        {
            get { return _gameplaySession; }
        }

        public LevelGameplayDefinition GameplayDefinition { get; private set; }

        protected override void Configure(IContainerBuilder builder)
        {
            if (_levelAnchor.GetComponentInChildren<LevelView>(true) != null)
            {
                throw new InvalidDataException("Remove the scene level view before loading a campaign level.");
            }

            var orbit = _sceneCamera.GetComponent<OrbitCamera>();
            builder.RegisterComponent(_sceneCamera);
            builder.RegisterComponent(orbit);
            builder.Register(resolver =>
            {
                var campaign = resolver.Resolve<CampaignProgress>();
                return resolver.Resolve<LevelContentCatalog>().GetLevel(campaign.PendingLevelNumber ?? campaign.CurrentLevelNumber);
            }, Lifetime.Scoped);
            builder.Register(resolver =>
            {
                LevelView = resolver.Instantiate(resolver.Resolve<LevelAssetReference>().View, _levelAnchor);
                orbit.SetLevelRoot(LevelView.transform);
                return LevelView;
            }, Lifetime.Scoped);
            builder.Register(resolver =>
            {
                GameplayDefinition = resolver.Resolve<LevelAssetReference>().Gameplay;
                return GameplayDefinition;
            }, Lifetime.Scoped);
            builder.RegisterEntryPoint<BoltSelection>(Lifetime.Scoped).AsSelf();
            builder.Register(resolver =>
            {
                var definition = resolver.Resolve<LevelGameplayDefinition>();
                return new LevelAttempt(definition.ReadSource(), definition.Gameplay);
            }, Lifetime.Scoped);
            builder.Register<LevelPartPhysics>(Lifetime.Scoped)
                .WithParameter("ground", _ground)
                .WithParameter("removalMargin", _partRemovalMargin);
            builder.RegisterComponent(_turnOperations).As<ITurnOperations>().AsSelf();
            builder.Register<TurnCoordinator>(Lifetime.Scoped);
            builder.RegisterComponent(_gameplaySession);
        }
    }
}
