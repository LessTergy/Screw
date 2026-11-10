using System;
using System.IO;
using Screw.Gameplay;
using Screw.Levels;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using Object = UnityEngine.Object;

namespace Screw.Editor.Levels
{
    public sealed class LevelRouteWindow : EditorWindow
    {
        private LevelRouteSession _session;
        private GameplaySession _gameplaySession;

        [MenuItem("Tools/Screw/Levels/Level 01 Route")]
        public static void Open()
        {
            GetWindow<LevelRouteWindow>("Level 01 Route");
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            EndSession();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Play Gameplay. Begin, prepare a step, then tap the expected head in Game view. Only this spatial stand removes objects; no game turn or falling is simulated.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || _session != null))
            {
                if (GUILayout.Button("Begin route"))
                {
                    var scope = Object.FindFirstObjectByType<GameplayLifetimeScope>();
                    var view = scope.LevelView;
                    var route = LevelRouteData.Read(File.ReadAllText(LevelRouteData.LevelOnePath), view.ReadSource());
                    if (scope.GameplaySession.Attempt.ActiveTurns.Count != 0
                        || scope.GameplaySession.Attempt.DetachedBoltIds.Count != 0)
                    {
                        throw new InvalidOperationException("Restart Gameplay before using the spatial stand.");
                    }

                    _gameplaySession = scope.GameplaySession;
                    _gameplaySession.enabled = false;
                    try
                    {
                        _session = new LevelRouteSession(view, scope.SceneCamera, scope.Container.Resolve<BoltSelection>(), route);
                    }
                    catch
                    {
                        _gameplaySession.enabled = true;
                        _gameplaySession = null;
                        throw;
                    }
                }
            }

            if (_session == null)
            {
                return;
            }

            EditorGUILayout.LabelField(_session.Message, EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(_session.IsComplete))
            {
                if (GUILayout.Button("Prepare next: manual tap"))
                {
                    _session.PrepareNext();
                }

                if (GUILayout.Button("Replay next: recorded pointer"))
                {
                    _session.ReplayNext();
                }
            }

            if (GUILayout.Button("Restore level / end route"))
            {
                EndSession();
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EndSession();
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            EndSession();
        }

        private void EndSession()
        {
            _session?.Dispose();
            _session = null;
            if (_gameplaySession != null)
            {
                _gameplaySession.enabled = true;
                _gameplaySession = null;
            }
        }
    }
}
