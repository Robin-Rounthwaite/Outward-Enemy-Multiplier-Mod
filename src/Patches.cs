using HarmonyLib;

namespace IncreasedEnemyCount
{
    internal static class Patches
    {
        // After the game applies the area save to every registered character, give clones that
        // have no save entry of their own (e.g. first visit with the mod installed) the same
        // dead/alive state as their original.
        [HarmonyPatch(typeof(CharacterManager), nameof(CharacterManager.LoadAiCharactersFromSave))]
        [HarmonyPostfix]
        private static void LoadAiCharactersFromSave_Postfix(CharacterSaveData[] _saves)
        {
            EnemyDoubler.Instance?.ApplySavesToClones(_saves);
        }

        // Unity can only play a limited number of sounds at once and silently drops the
        // lowest-priority ones past that. With many enemies, their footsteps and idle sounds
        // compete with the music at the same default priority, so the music sometimes cut out.
        // All of the game's music (area, combat, event) starts through PlayMusic: mark it top priority
        // (0 = highest in Unity) so enemy noises are dropped first.
        [HarmonyPatch(typeof(GlobalAudioManager), "PlayMusic")]
        [HarmonyPostfix]
        private static void PlayMusic_Postfix(UnityEngine.AudioSource __result)
        {
            if (__result && Plugin.PrioritizeMusic.Value)
                __result.priority = 0;
        }
    }
}
