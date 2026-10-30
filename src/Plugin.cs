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
        public const string VERSION = "1.2.3";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // General
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> SpawnSpacing;
        internal static ConfigEntry<string> ExcludedNames;
        internal static ConfigEntry<string> IncludedNames;

        // Multipliers
        internal static ConfigEntry<int> StandardMultiplier;
        internal static ConfigEntry<int> UniqueMultiplier;
        internal static ConfigEntry<int> BossMultiplier;

        // Advanced (only in the .cfg file, hidden from Config Manager)
        internal static ConfigEntry<int> MaxMultiplier;

        /// <summary>Hard ceiling for MaxMultiplier, so a typo can't create tens of thousands of enemies.</summary>
        internal const int MAX_MULTIPLIER_CEILING = 100;

        // Multiplayer
        internal static ConfigEntry<bool> RequireModOnAllPlayers;

        // Audio
        internal static ConfigEntry<bool> PrioritizeMusic;
        internal static ConfigEntry<int> MaxAudibleSounds;

        // Debug
        internal static ConfigEntry<bool> LogEachEnemy;
        internal static ConfigEntry<string> InspectEnemies;

        private EnemyDoubler m_doubler;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            const string NEXT_LOAD = " Takes effect the next time an area loads. In online co-op only the host's setting is used.";

            Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Master switch." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Enabled" }));
            SpawnSpacing = Config.Bind("General", "SpawnSpacing", 2.0f, new ConfigDescription(
                "How far (metres) each copy is placed from the original enemy." + NEXT_LOAD,
                new AcceptableValueRange<float>(0.5f, 10f),
                new ConfigurationManagerAttributes { Order = 90, DispName = "Spawn spacing (m)", ShowRangeAsPercent = false }));
            ExcludedNames = Config.Bind("General", "ExcludedNames", "", new ConfigDescription(
                "Comma-separated keywords. Any enemy whose name contains one of these (case-insensitive) is never copied, " +
                "whatever its type. Wins over 'Always standard'." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 80, DispName = "Never copy (keywords)" }));
            IncludedNames = Config.Bind("General", "IncludedNames", "", new ConfigDescription(
                "Comma-separated keywords. Any enemy whose name contains one of these (case-insensitive) is treated as a standard enemy " +
                "and uses the standard multiplier, even if it's a boss or unique enemy." + NEXT_LOAD, null,
                new ConfigurationManagerAttributes { Order = 70, DispName = "Always standard (keywords)" }));

            MaxMultiplier = Config.Bind("Advanced", "MaxMultiplier", 10, new ConfigDescription(
                "Highest value the three multiplier sliders can be set to (they also go up to this in the F5 menu). " +
                $"Up to {MAX_MULTIPLIER_CEILING}. Values above 10 are untested and can hurt performance badly: every enemy in an area " +
                "is multiplied, so 100x turns an area with 76 enemies into 7,600. Requires restarting the game. " +
                "In online co-op the host's value is used.",
                new AcceptableValueRange<int>(2, MAX_MULTIPLIER_CEILING),
                new ConfigurationManagerAttributes { Browsable = false }));
            int maxMult = MaxMultiplier.Value;

            const string LISTS = " Bosses and unique enemies are the ones listed on the official Outward wiki (outward.wiki.gg); " +
                                 "the names are matched in English.";
            StandardMultiplier = Config.Bind("Multipliers", "StandardEnemies", 2, new ConfigDescription(
                "Total number of each standard enemy (everything that isn't a boss or unique enemy). 1 = unchanged, 2 = double." + NEXT_LOAD,
                new AcceptableValueRange<int>(1, maxMult),
                new ConfigurationManagerAttributes { Order = 100, DispName = "Standard enemies", ShowRangeAsPercent = false }));
            UniqueMultiplier = Config.Bind("Multipliers", "UniqueEnemies", 2, new ConfigDescription(
                "Total number of each unique enemy (named one-off enemies like the Ghost of Vanasse). 1 = unchanged." + LISTS + NEXT_LOAD,
                new AcceptableValueRange<int>(1, maxMult),
                new ConfigurationManagerAttributes { Order = 90, DispName = "Unique enemies", ShowRangeAsPercent = false }));
            BossMultiplier = Config.Bind("Multipliers", "Bosses", 2, new ConfigDescription(
                "Total number of each boss (Elite enemies and other arena bosses). 1 = unchanged." + LISTS + NEXT_LOAD,
                new AcceptableValueRange<int>(1, maxMult),
                new ConfigurationManagerAttributes { Order = 80, DispName = "Bosses", ShowRangeAsPercent = false }));

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
                "Write one line per enemy to the BepInEx log (name, health, type and how many copies). Useful for checking the boss and unique lists.", null,
                new ConfigurationManagerAttributes { Order = 100, DispName = "Log each enemy" }));
            InspectEnemies = Config.Bind("Debug", "InspectEnemies", "", new ConfigDescription(
                "For troubleshooting. Comma-separated keywords: matching enemies get a detailed report in the BepInEx log when " +
                "their area loads, and every change in the state of the original and its copies is logged while you play. " +
                "Leave empty normally.", null,
                new ConfigurationManagerAttributes { Order = 90, DispName = "Inspect enemies (keywords)" }));

            AudioVoiceLimit.Apply(MaxAudibleSounds.Value);

            m_doubler = gameObject.AddComponent<EnemyDoubler>();

            SceneManager.sceneLoaded += m_doubler.OnSceneLoaded;
            SceneManager.sceneUnloaded += m_doubler.OnSceneUnloaded;

            new Harmony(GUID).PatchAll(typeof(Patches));

            Log.LogMessage($"{NAME} {VERSION} loaded. Standard x{StandardMultiplier.Value}, unique x{UniqueMultiplier.Value}, bosses x{BossMultiplier.Value}.");
        }
    }
}
