using System;
using System.Globalization;

namespace IncreasedEnemyCount
{
    /// <summary>
    /// The settings that decide which enemies get copied and how many times.
    /// In online co-op every player must use the host's values, so this can be sent as a string.
    /// </summary>
    internal sealed class EnemySettings
    {
        private const string FORMAT = "3";
        private const char SEP = '\u001f';

        /// <summary>
        /// Highest multiplier allowed (Advanced.MaxMultiplier). Network IDs are reserved for
        /// MaxMultiplier - 1 copies of every candidate enemy, whatever the sliders say, so IDs never
        /// depend on the sliders or on how an enemy was classified. In co-op this is the host's value.
        /// </summary>
        public int MaxMultiplier;

        public int MaxCopies => Math.Max(1, MaxMultiplier) - 1;

        public bool Enabled;
        public int StandardMultiplier;
        public int UniqueMultiplier;
        public int BossMultiplier;
        public float Spacing;
        public string ExcludedNames;
        public string IncludedNames;

        public bool AnyCopies => Enabled && (StandardMultiplier > 1 || UniqueMultiplier > 1 || BossMultiplier > 1);

        public int CopiesFor(EnemyCategory category)
        {
            if (!Enabled)
                return 0;
            int m;
            switch (category)
            {
                case EnemyCategory.Boss: m = BossMultiplier; break;
                case EnemyCategory.Unique: m = UniqueMultiplier; break;
                default: m = StandardMultiplier; break;
            }
            return Math.Max(0, Math.Min(MaxCopies, m - 1));
        }

        public static EnemySettings FromConfig()
        {
            return new EnemySettings
            {
                Enabled = Plugin.Enabled.Value,
                StandardMultiplier = Plugin.StandardMultiplier.Value,
                UniqueMultiplier = Plugin.UniqueMultiplier.Value,
                BossMultiplier = Plugin.BossMultiplier.Value,
                MaxMultiplier = Plugin.MaxMultiplier.Value,
                Spacing = Plugin.SpawnSpacing.Value,
                ExcludedNames = Plugin.ExcludedNames.Value ?? "",
                IncludedNames = Plugin.IncludedNames.Value ?? "",
            };
        }

        public EnemySettings WithEnabled(bool enabled)
        {
            var s = (EnemySettings)MemberwiseClone();
            s.Enabled = enabled;
            return s;
        }

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join(SEP.ToString(), new[]
            {
                FORMAT,
                Enabled ? "1" : "0",
                StandardMultiplier.ToString(inv),
                UniqueMultiplier.ToString(inv),
                BossMultiplier.ToString(inv),
                MaxMultiplier.ToString(inv),
                Spacing.ToString("R", inv),
                Clean(ExcludedNames),
                Clean(IncludedNames),
            });
        }

        public static EnemySettings Deserialize(string data)
        {
            if (string.IsNullOrEmpty(data))
                return null;
            try
            {
                var p = data.Split(SEP);
                if (p.Length < 9 || p[0] != FORMAT)
                    return null;
                var inv = CultureInfo.InvariantCulture;
                return new EnemySettings
                {
                    Enabled = p[1] == "1",
                    StandardMultiplier = int.Parse(p[2], inv),
                    UniqueMultiplier = int.Parse(p[3], inv),
                    BossMultiplier = int.Parse(p[4], inv),
                    MaxMultiplier = Math.Min(Plugin.MAX_MULTIPLIER_CEILING, Math.Max(2, int.Parse(p[5], inv))),
                    Spacing = float.Parse(p[6], inv),
                    ExcludedNames = p[7],
                    IncludedNames = p[8],
                };
            }
            catch
            {
                return null;
            }
        }

        public override string ToString()
        {
            return Enabled
                ? $"standard x{StandardMultiplier}, unique x{UniqueMultiplier}, bosses x{BossMultiplier}"
                : "off";
        }

        private static string Clean(string s) => (s ?? "").Replace(SEP, ',');
    }
}
