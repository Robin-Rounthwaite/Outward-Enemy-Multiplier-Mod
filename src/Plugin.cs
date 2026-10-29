using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace IncreasedEnemyCount
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "catalysten.increasedenemycount";
        public const string NAME = "Increased Enemy Count";
        public const string VERSION = "0.2.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // General
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> EnemyMultiplier;
        internal static ConfigEntry<float> SpawnSpacing;
        internal static ConfigEntry<string> ExcludedNames;

        // Bosses
        internal static ConfigEntry<bool> IncludeBosses;
        internal static ConfigEntry<float> BossHealthThreshold;
        internal static ConfigEntry<string> BossNameKeywords;
        internal static ConfigEntry<bool> QuestTargetsAreBosses;

        // Debug
        internal static ConfigEntry<bool> LogEachEnemy;

        private EnemyDoubler m_doubler;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            const string NEXT_LOAD = " Takes effect the next time an area loads.";

            Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Master switch." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Enabled" }));
            EnemyMultiplier = Config.Bind("General", "EnemyMultiplier", 2, new ConfigDescription(
                "Total number of each enemy after the mod runs. 2 = double, 3 = triple, 1 = off." + NEXT_LOAD,
                new AcceptableValueRange<int>(1, 6),
                new ConfigurationManagerAttributes { Order = 90, DispName = "Enemy multiplier", ShowRangeAsPercent = false }));
            SpawnSpacing = Config.Bind("General", "SpawnSpacing", 2.0f, new ConfigDescription(
                "How far (metres) each copy is placed from the original enemy." + NEXT_LOAD,
                new AcceptableValueRange<float>(0.5f, 10f),
                new ConfigurationManagerAttributes { Order = 80, DispName = "Spawn spacing (m)", ShowRangeAsPercent = false }));
            ExcludedNames = Config.Bind("General", "ExcludedNames", "", new ConfigDescription(
                "Comma-separated list. Any enemy whose name contains one of these (case-insensitive) is never copied." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 70, DispName = "Never copy (names)" }));

            IncludeBosses = Config.Bind("Bosses", "IncludeBosses", false, new ConfigDescription(
                "If on, bosses are multiplied as well. If off, anything detected as a boss is left alone." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Multiply bosses too" }));
            BossHealthThreshold = Config.Bind("Bosses", "BossHealthThreshold", 1000f, new ConfigDescription(
                "Enemies with at least this much base max health count as bosses. 0 disables this check." + NEXT_LOAD,
                new AcceptableValueRange<float>(0f, 10000f),
                new ConfigurationManagerAttributes { Order = 90, DispName = "Boss health threshold", ShowRangeAsPercent = false }));
            BossNameKeywords = Config.Bind("Bosses", "BossNameKeywords", "Elite,Lich,Grandmother,Calixa,Immaculate", new ConfigDescription(
                "Comma-separated list. Any enemy whose name contains one of these (case-insensitive) counts as a boss." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 80, DispName = "Boss name keywords" }));
            QuestTargetsAreBosses = Config.Bind("Bosses", "QuestTargetsAreBosses", true, new ConfigDescription(
                "If on, enemies whose death advances a quest count as bosses." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 70, DispName = "Quest targets count as bosses" }));

            LogEachEnemy = Config.Bind("Debug", "LogEachEnemy", true, new ConfigDescription(
                "Write one line per enemy to the BepInEx log (name, health, boss decision). Useful for tuning the boss settings.", null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Log each enemy" }));

            m_doubler = gameObject.AddComponent<EnemyDoubler>();

            SceneManager.sceneLoaded += m_doubler.OnSceneLoaded;
            SceneManager.sceneUnloaded += m_doubler.OnSceneUnloaded;

            new Harmony(GUID).PatchAll(typeof(Patches));

            Log.LogMessage($"{NAME} {VERSION} loaded. Multiplier x{EnemyMultiplier.Value}, bosses {(IncludeBosses.Value ? "included" : "excluded")}.");
        }
    }
}
