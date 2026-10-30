using UnityEngine;
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

    }
}
