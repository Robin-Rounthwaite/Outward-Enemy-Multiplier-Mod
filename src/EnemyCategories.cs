using System;
using System.Collections.Generic;

namespace IncreasedEnemyCount
{
    internal enum EnemyCategory
    {
        Standard,
        Unique,
        Boss
    }

    /// <summary>
    /// Which enemies count as Bosses and which as Unique, taken from the official Outward wiki:
    ///   https://outward.wiki.gg/wiki/Enemies/Bosses
    ///   https://outward.wiki.gg/wiki/Enemies/Unique
    /// Names are matched exactly (ignoring case and apostrophe style), because several standard,
    /// unique and boss enemies share part of their name ("Ash Giant" / "Ash Giant Highmonk",
    /// "Grandmother" / "Grandmother Medyse"). Everything not listed is Standard.
    /// The names are English, so they only match when the game is in English.
    /// </summary>
    internal static class EnemyCategories
    {
        private static readonly string[] s_bosses =
        {
            "Ash Giant Highmonk",
            "Brand Squire",
            "Breath of Darkness",
            "Calixa",
            "Concealed Knight: ???",
            "Crimson Avatar",
            "Djinn",
            "Elite Alpha Tuanosaur",
            "Elite Ash Giant",
            "Elite Beast Golem",
            "Elite Boozu",
            "Elite Burning Man",
            "Elite Crescent Shark",
            "Elite Crimson Avatar",
            "Elite Gargoyle Alchemist",
            "Elite Gargoyle Mage",
            "Elite Gargoyle Warrior",
            "Elite Krypteia Warrior",
            "Elite Krypteia Witch",
            "Elite Mantis Shrimp",
            "Elite Obsidian Elemental",
            "Elite Sublime Shell",
            "Elite Torcrab",
            "Forge Master",
            "Grandmother",
            "Immaculate Dreamer",
            "Immaculate's Bird",
            // The wiki also calls these two Unique when met in their normal locations. A name can't
            // tell the two apart, so they're treated as bosses everywhere.
            "Light Mender",
            "Plague Doctor",
            "Troglodyte Queen",
        };

        private static readonly string[] s_uniques =
        {
            "Accursed Wendigo",
            "Altered Gargoyle",
            "Ancestral General",
            "Ancestral Soldier",
            "Bloody Alexis",
            "Calygrey Hero",
            "Chromatic Arcane Elemental",
            "Cracked Gargoyle",
            "Elemental Parasite",
            "Executioner Bug",
            "Ghost of Vanasse",
            "Giant Horror",
            "Glacial Tuanosaur",
            "Golden Matriarch",
            "Grandmother Medyse",
            "Greater Grotesque",
            "Guardian of the Compass",
            "Kazite Admiral",
            "Lightning Dancer",
            "Liquid-Cooled Golem",
            "Luke the Pearlescent",
            "Mad Captain's Bones",
            "Matriarch Myrmitaur",
            "Quartz Elemental",
            "Razorhorn Stekosaur",
            "Royal Manticore",
            "Rusted Enforcer",
            "Sandrose Horror",
            "She Who Speaks",
            "That Annoying Troglodyte",
            "The Crusher",
            "The First Cannibal",
            "The Last Acolyte",
            "Thunderbolt Golem",
            "Titanic Guardian Mk-7",
            "Troglodyte Archmage",
            "Tyrant of the Hive",
            "Vile Illuminator",
            "Virulent Hiveman",
            "Volcanic Gastrocin",
        };

        private static readonly Dictionary<string, EnemyCategory> s_byName = Build();

        private static Dictionary<string, EnemyCategory> Build()
        {
            var d = new Dictionary<string, EnemyCategory>(StringComparer.Ordinal);
            foreach (var n in s_uniques)
                d[Normalize(n)] = EnemyCategory.Unique;
            foreach (var n in s_bosses)
                d[Normalize(n)] = EnemyCategory.Boss;
            return d;
        }

        /// <summary>Category for an enemy name; Standard if it's on neither list.</summary>
        public static EnemyCategory Lookup(string name)
        {
            if (string.IsNullOrEmpty(name))
                return EnemyCategory.Standard;
            return s_byName.TryGetValue(Normalize(name), out var c) ? c : EnemyCategory.Standard;
        }

        private static string Normalize(string s)
        {
            return s.Trim()
                    .Replace('’', '\'')   // ’ -> '
                    .Replace('‘', '\'')
                    .ToLowerInvariant();
        }
    }
}
