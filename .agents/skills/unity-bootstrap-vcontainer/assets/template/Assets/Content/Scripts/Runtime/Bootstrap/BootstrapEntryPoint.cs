using VContainer.Unity;

namespace ProjectBootstrap
{
    internal sealed class BootstrapEntryPoint : IStartable
    {
        private readonly SceneNavigator _sceneNavigator;

        public BootstrapEntryPoint(SceneNavigator sceneNavigator)
        {
            _sceneNavigator = sceneNavigator;
        }

        public void Start()
        {
            _sceneNavigator.ToGameplay();
        }
    }
}
