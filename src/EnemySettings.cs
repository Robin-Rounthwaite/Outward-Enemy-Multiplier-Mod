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
        private const string FORMAT = "2";
        private const char SEP = '\u001f';

        /// <summary>
        /// Network IDs are reserved for this many copies of every candidate enemy, whatever the
        /// sliders say, so IDs never depend on settings or on how an enemy was classified.
        /// Matches the highest slider value (10x = 9 copies).
        /// </summary>
        public const int MAX_COPIES = 9;

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
            return Math.Max(0, Math.Min(MAX_COPIES, m - 1));
        }

        public static EnemySettings FromConfig()
        {
            return new EnemySettings
            {
                Enabled = Plugin.Enabled.Value,
                StandardMultiplier = Plugin.StandardMultiplier.Value,
                UniqueMultiplier = Plugin.UniqueMultiplier.Value,
                BossMultiplier = Plugin.BossMultiplier.Value,
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
                if (p.Length < 8 || p[0] != FORMAT)
                    return null;
                var inv = CultureInfo.InvariantCulture;
                return new EnemySettings
                {
                    Enabled = p[1] == "1",
                    StandardMultiplier = int.Parse(p[2], inv),
                    UniqueMultiplier = int.Parse(p[3], inv),
                    BossMultiplier = int.Parse(p[4], inv),
                    Spacing = float.Parse(p[5], inv),
                    ExcludedNames = p[6],
                    IncludedNames = p[7],
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
