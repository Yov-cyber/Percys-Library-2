using System;

namespace ComicReader.Models
{
    public enum AchievementMetric
    {
        ComicsCompleted,
        PagesRead,
        Streak,
        ReadingTimeMinutes,
        LongestSessionMinutes,
    }

    /// <summary>
    /// Definicion estatica + estado serializable de un logro.
    /// El catalogo lo provee AchievementService; el estado (Unlocked/UnlockedAt)
    /// se persiste en %AppData%\PercysLibrary\achievements.json.
    /// </summary>
    public sealed class Achievement
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public AchievementMetric Metric { get; init; }
        public int Threshold { get; init; }
        public string IconGlyph { get; init; } = "★";

        public bool Unlocked { get; set; }
        public DateTime? UnlockedAt { get; set; }

        // Progreso actual del usuario respecto al umbral. Se rellena en runtime.
        public int CurrentValue { get; set; }
        public double Progress => Threshold <= 0 ? 1.0 : Math.Min(1.0, (double)CurrentValue / Threshold);
        public string ProgressLabel => Threshold <= 0 ? "—" : $"{Math.Min(CurrentValue, Threshold)} / {Threshold}";
    }
}
