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
        private const string FORMAT = "1";
        private const char SEP = '\u001f';

        public bool Enabled;
        public int Multiplier;
        public float Spacing;
        public string ExcludedNames;
        public bool IncludeBosses;
        public float BossHealthThreshold;
        public string BossNameKeywords;
        public bool QuestTargetsAreBosses;

        public int Copies => Enabled ? Math.Max(0, Multiplier - 1) : 0;

        public static EnemySettings FromConfig()
        {
            return new EnemySettings
            {
                Enabled = Plugin.Enabled.Value,
                Multiplier = Plugin.EnemyMultiplier.Value,
                Spacing = Plugin.SpawnSpacing.Value,
                ExcludedNames = Plugin.ExcludedNames.Value ?? "",
                IncludeBosses = Plugin.IncludeBosses.Value,
                BossHealthThreshold = Plugin.BossHealthThreshold.Value,
                BossNameKeywords = Plugin.BossNameKeywords.Value ?? "",
                QuestTargetsAreBosses = Plugin.QuestTargetsAreBosses.Value,
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
                Multiplier.ToString(inv),
                Spacing.ToString("R", inv),
                Clean(ExcludedNames),
                IncludeBosses ? "1" : "0",
                BossHealthThreshold.ToString("R", inv),
                Clean(BossNameKeywords),
                QuestTargetsAreBosses ? "1" : "0",
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
                    Multiplier = int.Parse(p[2], inv),
                    Spacing = float.Parse(p[3], inv),
                    ExcludedNames = p[4],
                    IncludeBosses = p[5] == "1",
                    BossHealthThreshold = float.Parse(p[6], inv),
                    BossNameKeywords = p[7],
                    QuestTargetsAreBosses = p[8] == "1",
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
                ? $"x{Multiplier}, bosses {(IncludeBosses ? "included" : "excluded")}"
                : "off";
        }

        private static string Clean(string s) => (s ?? "").Replace(SEP, ',');
    }
}
