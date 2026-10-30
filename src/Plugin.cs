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
        public const string VERSION = "0.3.3";

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

        // Multiplayer
        internal static ConfigEntry<bool> RequireModOnAllPlayers;

        // Audio
        internal static ConfigEntry<bool> PrioritizeMusic;
        internal static ConfigEntry<int> MaxAudibleSounds;

        // Debug
        internal static ConfigEntry<bool> LogEachEnemy;

        private EnemyDoubler m_doubler;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            const string NEXT_LOAD = " Takes effect the next time an area loads. In online co-op only the host's setting is used.";

            Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Master switch." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Enabled" }));
            EnemyMultiplier = Config.Bind("General", "EnemyMultiplier", 2, new ConfigDescription(
                "Total number of each enemy after the mod runs. 2 = double, 3 = triple, 1 = off." + NEXT_LOAD,
                new AcceptableValueRange<int>(1, 10),
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
            BossNameKeywords = Config.Bind("Bosses", "BossNameKeywords", "Elite,Lich,Grandmother,Calixa", new ConfigDescription(
                "Comma-separated list. Any enemy whose name contains one of these (case-insensitive) counts as a boss." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 80, DispName = "Boss name keywords" }));
            QuestTargetsAreBosses = Config.Bind("Bosses", "QuestTargetsAreBosses", true, new ConfigDescription(
                "If on, enemies whose death advances a quest count as bosses." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 70, DispName = "Quest targets count as bosses" }));

            RequireModOnAllPlayers = Config.Bind("Multiplayer", "RequireModOnAllPlayers", true, new ConfigDescription(
                "Host only. If on, no extra enemies are added to an area while anyone in the session is missing this mod. " +
                "Players without the mod can't see the extra enemies, but can still be hit by them.", null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Require mod on all players" }));

            PrioritizeMusic = Config.Bind("Audio", "PrioritizeMusic", true, new ConfigDescription(
                "Keeps music from cutting out when lots of enemies are making noise, by giving music the highest sound priority. " +
                "Applies to the next music track that starts. Affects only this player.", null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Keep music playing" }));

            MaxAudibleSounds = Config.Bind("Audio", "MaxAudibleSounds", 128, new ConfigDescription(
                "How many sounds can be heard at once. Past this, Unity mutes the quietest ones, which makes voices in big groups choppy. " +
                "Unity's default is 32. Higher costs a little CPU. Requires restarting the game. Affects only this player.",
                new AcceptableValueRange<int>(32, 255),
                new ConfigurationManagerAttributes { Order = 90, DispName = "Max audible sounds (restart)", ShowRangeAsPercent = false }));

            LogEachEnemy = Config.Bind("Debug", "LogEachEnemy", true, new ConfigDescription(
                "Write one line per enemy to the BepInEx log (name, health, boss decision). Useful for tuning the boss settings.", null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Log each enemy" }));

            AudioVoiceLimit.Apply(MaxAudibleSounds.Value);

            m_doubler = gameObject.AddComponent<EnemyDoubler>();

            SceneManager.sceneLoaded += m_doubler.OnSceneLoaded;
            SceneManager.sceneUnloaded += m_doubler.OnSceneUnloaded;

            new Harmony(GUID).PatchAll(typeof(Patches));

            Log.LogMessage($"{NAME} {VERSION} loaded. Multiplier x{EnemyMultiplier.Value}, bosses {(IncludeBosses.Value ? "included" : "excluded")}.");
        }
    }
}
