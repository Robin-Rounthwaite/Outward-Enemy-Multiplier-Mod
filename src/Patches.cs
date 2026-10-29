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
    }
}
