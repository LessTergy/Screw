using Screw.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Screw.Editor
{
    internal static class CampaignSaveMenu
    {
        private const string MenuPath = "Tools/Screw/Reset Campaign Save...";

        [MenuItem(MenuPath, false, 2001)]
        private static void ResetSave()
        {
            if (!EditorUtility.DisplayDialog("Сброс сохранения",
                    "Сбросить пройденные уровни, баланс монет и запас бустеров?",
                    "Сбросить", "Отмена"))
            {
                return;
            }

            PlayerPrefs.DeleteKey(CampaignProgress.SaveKey);
            PlayerPrefs.Save();
            Debug.Log("Сохранение кампании сброшено.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanResetSave()
        {
            return !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
