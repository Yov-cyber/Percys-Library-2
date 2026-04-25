using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ComicReader.Core.Abstractions;
using ComicReader.Core.Services;
using ComicReader.Models;

namespace ComicReader.Services
{
    /// <summary>
    /// Sistema de logros discreto. No recolecta datos propios: lee
    /// IReadingStatsService.GetDashboard() y compara contra un catalogo fijo
    /// de umbrales. La unica persistencia propia es la fecha de desbloqueo
    /// de cada logro, en %AppData%\PercysLibrary\achievements.json.
    ///
    /// Llamar Refresh() despues de cualquier evento que pueda haber cambiado
    /// las stats (cierre de sesion, lectura de pagina). Si un logro pasa de
    /// locked -> unlocked, se dispara AchievementUnlocked y se muestra un
    /// toast sutil via NotificationService.
    /// </summary>
    public sealed class AchievementService
    {
        private static readonly Lazy<AchievementService> _instance = new(() => new AchievementService());
        public static AchievementService Instance => _instance.Value;

        private readonly string _dataFile;
        private readonly Dictionary<string, DateTime> _unlocks = new();
        private readonly List<Achievement> _catalog;

        public event Action<Achievement> AchievementUnlocked;

        private AchievementService()
        {
            var rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PercysLibrary");
            try { Directory.CreateDirectory(rootDir); } catch { }
            _dataFile = Path.Combine(rootDir, "achievements.json");
            _catalog = BuildCatalog();
            LoadUnlocks();
        }

        private static List<Achievement> BuildCatalog() => new()
        {
            new Achievement { Id = "first_page",      Name = "Primera página",     Description = "Leiste tu primera página.",            Metric = AchievementMetric.PagesRead,          Threshold = 1,    IconGlyph = "📖" },
            new Achievement { Id = "ten_pages",       Name = "Diez páginas",       Description = "Leiste 10 páginas en total.",          Metric = AchievementMetric.PagesRead,          Threshold = 10,   IconGlyph = "📄" },
            new Achievement { Id = "hundred_pages",   Name = "Centenar",           Description = "Leiste 100 páginas en total.",         Metric = AchievementMetric.PagesRead,          Threshold = 100,  IconGlyph = "📚" },
            new Achievement { Id = "thousand_pages",  Name = "Mil páginas",        Description = "Leiste 1000 páginas en total.",        Metric = AchievementMetric.PagesRead,          Threshold = 1000, IconGlyph = "🏛" },
            new Achievement { Id = "first_comic",     Name = "Primer cómic",       Description = "Completaste tu primer cómic.",         Metric = AchievementMetric.ComicsCompleted,    Threshold = 1,    IconGlyph = "🥇" },
            new Achievement { Id = "five_comics",     Name = "Cinco cómics",       Description = "Completaste 5 cómics.",                Metric = AchievementMetric.ComicsCompleted,    Threshold = 5,    IconGlyph = "🥈" },
            new Achievement { Id = "twenty_comics",   Name = "Coleccionista",      Description = "Completaste 20 cómics.",               Metric = AchievementMetric.ComicsCompleted,    Threshold = 20,   IconGlyph = "🏆" },
            new Achievement { Id = "streak_3",        Name = "Racha de 3 días",    Description = "Leiste 3 días seguidos.",              Metric = AchievementMetric.Streak,             Threshold = 3,    IconGlyph = "🔥" },
            new Achievement { Id = "streak_7",        Name = "Racha de 7 días",    Description = "Leiste 7 días seguidos.",              Metric = AchievementMetric.Streak,             Threshold = 7,    IconGlyph = "⚡" },
            new Achievement { Id = "marathon",        Name = "Maratón",            Description = "Sesión de lectura de 60+ minutos.",    Metric = AchievementMetric.LongestSessionMinutes, Threshold = 60,    IconGlyph = "⏱" },
        };

        public IReadOnlyList<Achievement> GetAll() => _catalog.AsReadOnly();

        public int UnlockedCount => _catalog.Count(a => a.Unlocked);
        public int TotalCount => _catalog.Count;

        /// <summary>
        /// Lee el dashboard actual y actualiza progreso + estado de cada logro.
        /// Devuelve la lista de logros que pasaron a desbloqueado en esta llamada.
        /// </summary>
        public List<Achievement> Refresh()
        {
            var newlyUnlocked = new List<Achievement>();
            StatsDashboard dash = null;
            try
            {
                var stats = ServiceLocator.TryGet<IReadingStatsService>();
                dash = stats?.GetDashboard();
            }
            catch { }

            if (dash == null) return newlyUnlocked;

            foreach (var a in _catalog)
            {
                a.CurrentValue = a.Metric switch
                {
                    AchievementMetric.PagesRead             => dash.TotalPagesRead,
                    AchievementMetric.ComicsCompleted       => dash.TotalComicsRead,
                    AchievementMetric.Streak                => dash.CurrentStreak,
                    AchievementMetric.ReadingTimeMinutes    => (int)dash.TotalReadingTime.TotalMinutes,
                    AchievementMetric.LongestSessionMinutes => (int)dash.LongestReadingSession.TotalMinutes,
                    _ => 0,
                };

                bool wasUnlocked = a.Unlocked;
                if (_unlocks.TryGetValue(a.Id, out var unlockedAt))
                {
                    a.Unlocked = true;
                    a.UnlockedAt = unlockedAt;
                }

                if (!a.Unlocked && a.CurrentValue >= a.Threshold && a.Threshold > 0)
                {
                    a.Unlocked = true;
                    a.UnlockedAt = DateTime.Now;
                    _unlocks[a.Id] = a.UnlockedAt.Value;
                    if (!wasUnlocked) newlyUnlocked.Add(a);
                }
            }

            if (newlyUnlocked.Count > 0)
            {
                SaveUnlocks();
                foreach (var a in newlyUnlocked)
                {
                    try { AchievementUnlocked?.Invoke(a); } catch { }
                    try
                    {
                        Notifications.NotificationService.Instance?.Info(
                            a.Description,
                            $"Logro desbloqueado: {a.Name}");
                    }
                    catch { }
                }
            }

            return newlyUnlocked;
        }

        private void LoadUnlocks()
        {
            try
            {
                if (!File.Exists(_dataFile)) return;
                var json = File.ReadAllText(_dataFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
                if (data == null) return;
                foreach (var kv in data) _unlocks[kv.Key] = kv.Value;
            }
            catch { }
        }

        private void SaveUnlocks()
        {
            try
            {
                var json = JsonSerializer.Serialize(_unlocks, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataFile, json);
            }
            catch { }
        }
    }
}
