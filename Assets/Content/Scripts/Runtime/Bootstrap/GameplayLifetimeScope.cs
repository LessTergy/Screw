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
        [SerializeField] private LevelView _levelView;
        [SerializeField] private LevelGameplayDefinition _gameplayDefinition;
        [SerializeField] private BoltTurnOperations _turnOperations;
        [SerializeField] private GameplaySession _gameplaySession;

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

        public LevelView LevelView
        {
            get { return _levelView; }
        }

        public GameplaySession GameplaySession
        {
            get { return _gameplaySession; }
        }

        public LevelGameplayDefinition GameplayDefinition
        {
            get { return _gameplayDefinition; }
        }

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(_levelView);
            builder.RegisterComponent(_sceneCamera);
            builder.RegisterEntryPoint<BoltSelection>(Lifetime.Scoped).AsSelf();
            builder.RegisterInstance(_gameplayDefinition);
            builder.Register(_ =>
            {
                if (_gameplayDefinition.ContentCatalog != _levelView.ContentCatalog
                    || _gameplayDefinition.Gameplay.LevelNumber != _levelView.LevelNumber)
                {
                    throw new InvalidDataException("Gameplay definition does not match the scene level and catalog.");
                }

                return new LevelAttempt(_gameplayDefinition.ReadSource(), _gameplayDefinition.Gameplay);
            }, Lifetime.Scoped);
            builder.RegisterComponent(_turnOperations).As<ITurnOperations>().AsSelf();
            builder.Register<TurnCoordinator>(Lifetime.Scoped);
            builder.RegisterComponent(_gameplaySession);
        }
    }
}
