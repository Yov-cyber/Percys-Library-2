using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ComicReader.Core.Abstractions;
using System.Globalization;
using ComicReader.Services;
using ComicReader.Models;

namespace ComicReader.Services
{
    public class ReadingStatsService : IReadingStatsService
    {
        private readonly string _rootDir;
        private readonly string _dataFile;
        private Data _data = new();
        private ActiveSession _active;

        public ReadingStatsService()
        {
            _rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PercysLibrary");
            _dataFile = Path.Combine(_rootDir, "readingStats.json");
            Load();
        }

        public void StartSession(string comicPath, string comicTitle, int totalPages)
        {
            EndSession(); // Cerrar si había una previa
            _active = new ActiveSession
            {
                ComicPath = comicPath,
                ComicTitle = comicTitle,
                TotalPages = totalPages,
                StartTime = DateTime.Now,
                LastPage = 0
            };
        }

        public void EndSession()
        {
            if (_active == null) return;
            var now = DateTime.Now;
            var duration = now - _active.StartTime;
            var pages = Math.Max(0, _active.LastPage - _active.StartPage);
            _data.Sessions.Add(new SessionRecord
            {
                ComicPath = _active.ComicPath,
                ComicTitle = _active.ComicTitle,
                StartTime = _active.StartTime,
                EndTime = now,
                PagesRead = pages
            });

            // Actualizar progreso del cómic
            var prog = _data.Progress.FirstOrDefault(p => string.Equals(p.ComicPath, _active.ComicPath, StringComparison.OrdinalIgnoreCase));
            if (prog == null)
            {
                prog = new ComicProgressRecord
                {
                    ComicPath = _active.ComicPath,
                    Title = _active.ComicTitle,
                    TotalPages = _active.TotalPages,
                    Progress = _active.LastPage,
                    LastRead = now
                };
                _data.Progress.Add(prog);
            }
            else
            {
                prog.Title = _active.ComicTitle;
                prog.TotalPages = _active.TotalPages;
                prog.Progress = Math.Max(prog.Progress, _active.LastPage);
                prog.LastRead = now;
            }

            Save();
            _active = null;
        }

        public void RecordPageViewed(int pageNumber)
        {
            if (_active == null)
                return;
            if (_active.StartPage == 0)
                _active.StartPage = pageNumber;
            _active.LastPage = Math.Max(_active.LastPage, pageNumber);
        }

        public StatsDashboard GetDashboard()
        {
            var now = DateTime.Now;
            var weekAgo = now.AddDays(-7);
            var monthAgo = now.AddDays(-30);

            var totalTime = _data.Sessions.Aggregate(TimeSpan.Zero, (acc, s) => acc + (s.EndTime - s.StartTime));
            var totalPages = _data.Sessions.Sum(s => s.PagesRead);
            var comicsFinished = _data.Progress.Count(p => p.TotalPages > 0 && p.Progress >= p.TotalPages);

            var thisWeekComics = _data.Sessions.Where(s => s.StartTime >= weekAgo).Select(s => s.ComicPath).Distinct().Count();
            var thisMonthComics = _data.Sessions.Where(s => s.StartTime >= monthAgo).Select(s => s.ComicPath).Distinct().Count();
            var longest = _data.Sessions.Any() ? _data.Sessions.Max(s => (s.EndTime - s.StartTime)) : TimeSpan.Zero;

            // streak: días consecutivos con al menos una sesión
            int streak = 0;
            var days = _data.Sessions
                .Select(s => s.StartTime.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();
            var dcur = now.Date;
            foreach (var d in days)
            {
                if (d == dcur) { streak++; dcur = dcur.AddDays(-1); }
                else if (d == dcur.AddDays(-1)) { streak++; dcur = dcur.AddDays(-1); }
                else break;
            }

            var avg = _data.Sessions.Any() ? TimeSpan.FromMinutes(_data.Sessions.Average(s => (s.EndTime - s.StartTime).TotalMinutes)) : TimeSpan.Zero;

            // Día favorito (por número de sesiones)
            string favoriteDay = "—";
            if (_data.Sessions.Any())
            {
                var day = _data.Sessions
                    .GroupBy(s => s.StartTime.DayOfWeek)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();
                // Localizar nombre del día según cultura actual
                var culture = CultureInfo.CurrentUICulture;
                favoriteDay = culture.DateTimeFormat.GetDayName(day);
                if (!string.IsNullOrEmpty(favoriteDay))
                {
                    // Capitalizar primera letra si la cultura lo devuelve en minúsculas
                    favoriteDay = char.ToUpper(favoriteDay[0], culture) + favoriteDay.Substring(1);
                }
            }

            // Formato preferido (por sesiones únicas por archivo)
            string preferredFormat = "—";
            if (_data.Sessions.Any())
            {
                preferredFormat = _data.Sessions
                    .Select(s => Path.GetExtension(s.ComicPath) ?? string.Empty)
                    .Select(ext => ext.TrimStart('.').ToUpperInvariant())
                    .Where(ext => !string.IsNullOrWhiteSpace(ext))
                    .GroupBy(ext => ext)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";
            }

            // Género favorito (por tags de colecciones de favoritos para los cómics con actividad)
            string favoriteGenre = "—";
            try
            {
                var readPaths = _data.Progress.Select(p => p.ComicPath)
                    .Concat(_data.Sessions.Select(s => s.ComicPath))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (readPaths.Count > 0)
                {
                    var collections = FavoritesStorage.Load();
                    var tags = collections
                        .SelectMany(c => c.Items)
                        .Where(item => !string.IsNullOrWhiteSpace(item.FilePath) && readPaths.Contains(item.FilePath))
                        .SelectMany(item => item.Tags ?? System.Linq.Enumerable.Empty<string>())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t.Trim())
                        .ToList();

                    if (tags.Count > 0)
                    {
                        favoriteGenre = tags
                            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                            .OrderByDescending(g => g.Count())
                            .Select(g => g.Key)
                            .FirstOrDefault() ?? "—";
                    }
                }
            }
            catch { }

            return new StatsDashboard
            {
                TotalComicsRead = comicsFinished,
                TotalPagesRead = totalPages,
                TotalReadingTime = totalTime,
                AverageReadingTime = avg,
                ComicsThisWeek = thisWeekComics,
                ComicsThisMonth = thisMonthComics,
                FavoriteGenre = favoriteGenre,
                FavoriteDay = favoriteDay,
                PreferredFormat = preferredFormat,
                LongestReadingSession = longest,
                CurrentStreak = streak
            };
        }

        public ReadingInsights GetInsights(int days = 30)
        {
            var insights = new ReadingInsights();
            try
            {
                if (days < 7) days = 7;
                if (days > 180) days = 180;

                var now = DateTime.Now;
                var startDate = now.Date.AddDays(-(days - 1));

                var scopedSessions = _data.Sessions
                    .Where(s => s.StartTime.Date >= startDate)
                    .OrderBy(s => s.StartTime)
                    .ToList();

                var daily = new List<DailyActivityPoint>();
                for (var cursor = startDate; cursor <= now.Date; cursor = cursor.AddDays(1))
                {
                    var daySessions = scopedSessions.Where(s => s.StartTime.Date == cursor).ToList();
                    var minutes = daySessions.Sum(s => (s.EndTime - s.StartTime).TotalMinutes);
                    var pages = daySessions.Sum(s => s.PagesRead);
                    daily.Add(new DailyActivityPoint
                    {
                        Date = cursor,
                        Minutes = minutes,
                        Pages = pages
                    });
                }

                var dow = scopedSessions
                    .GroupBy(s => s.StartTime.DayOfWeek)
                    .Select(g => new DayOfWeekActivityPoint
                    {
                        Day = g.Key,
                        Sessions = g.Count(),
                        Minutes = g.Sum(s => (s.EndTime - s.StartTime).TotalMinutes),
                        Pages = g.Sum(s => s.PagesRead)
                    })
                    .OrderBy(g => ((int)g.Day + 6) % 7) // Monday-first ordering
                    .ToList();

                var hourly = scopedSessions
                    .GroupBy(s => s.StartTime.Hour)
                    .Select(g => new HourlyActivityPoint
                    {
                        Hour = g.Key,
                        Sessions = g.Count(),
                        Minutes = g.Sum(s => (s.EndTime - s.StartTime).TotalMinutes),
                        Pages = g.Sum(s => s.PagesRead)
                    })
                    .OrderBy(g => g.Hour)
                    .ToList();

                // Genre distribution leveraging Favorites tags when present
                var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var activityPaths = scopedSessions.Select(s => s.ComicPath)
                        .Concat(_data.Progress.Select(p => p.ComicPath))
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (activityPaths.Count > 0)
                    {
                        var collections = FavoritesStorage.Load();
                        foreach (var item in collections.SelectMany(c => c.Items))
                        {
                            if (string.IsNullOrWhiteSpace(item.FilePath))
                                continue;
                            if (!activityPaths.Contains(item.FilePath))
                                continue;

                            if (item.Tags == null || item.Tags.Count == 0)
                                continue;

                            foreach (var rawTag in item.Tags)
                            {
                                var tag = rawTag?.Trim();
                                if (string.IsNullOrWhiteSpace(tag))
                                    continue;
                                if (genreCounts.TryGetValue(tag, out var count)) genreCounts[tag] = count + 1;
                                else genreCounts[tag] = 1;
                            }
                        }
                    }
                }
                catch
                {
                    // ignore tagging failures and rely on fallbacks
                }

                if (genreCounts.Count == 0)
                {
                    foreach (var path in scopedSessions.Select(s => s.ComicPath)
                                 .Where(p => !string.IsNullOrWhiteSpace(p)))
                    {
                        var genreKey = GuessFallbackGenre(path);
                        if (genreCounts.TryGetValue(genreKey, out var count)) genreCounts[genreKey] = count + 1;
                        else genreCounts[genreKey] = 1;
                    }
                }

                var genres = genreCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(8)
                    .Select(kv => new GenreDistributionPoint { Genre = kv.Key, Count = kv.Value })
                    .ToList();

                var leaderboard = _data.Progress
                    .Where(p => p.TotalPages > 0)
                    .OrderByDescending(p => p.Progress * 1.0 / Math.Max(1, p.TotalPages))
                    .ThenByDescending(p => p.LastRead)
                    .Take(6)
                    .Select(p => new CompletionLeaderboardEntry
                    {
                        Title = p.Title ?? Path.GetFileNameWithoutExtension(p.ComicPath) ?? "—",
                        Completion = Math.Min(100d, Math.Round(p.Progress * 100d / Math.Max(1, p.TotalPages), 1)),
                        PagesRead = p.Progress,
                        TotalPages = p.TotalPages,
                        LastRead = p.LastRead
                    })
                    .ToList();

                insights = new ReadingInsights
                {
                    DailyActivity = daily,
                    DayOfWeekActivity = dow,
                    HourlyActivity = hourly,
                    GenreDistribution = genres,
                    CompletionLeaderboard = leaderboard
                };
            }
            catch
            {
                // Return whatever we could compute so far
            }

            return insights;
        }

        private static string GuessFallbackGenre(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Sin etiqueta";

            try
            {
                var ext = Path.GetExtension(path) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(ext))
                    return ext.Trim('.').ToUpperInvariant();

                var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(folder))
                    return folder;
            }
            catch { }

            return "Sin etiqueta";
        }

        public IEnumerable<ComicProgressInfo> GetRecentProgress(int count = 10)
        {
            return _data.Progress
                .OrderByDescending(p => p.LastRead)
                .Take(count)
                .Select(p => new ComicProgressInfo
                {
                    Title = p.Title,
                    ComicPath = p.ComicPath,
                    Progress = p.Progress,
                    TotalPages = p.TotalPages,
                    LastRead = p.LastRead
                });
        }

        public IEnumerable<ReadingSessionInfo> GetTodaySessions()
        {
            var today = DateTime.Today;
            return _data.Sessions
                .Where(s => s.StartTime.Date == today)
                .OrderByDescending(s => s.StartTime)
                .Select(s => new ReadingSessionInfo
                {
                    ComicTitle = s.ComicTitle,
                    ComicPath = s.ComicPath,
                    StartTime = s.StartTime,
                    Duration = s.EndTime - s.StartTime,
                    PagesRead = s.PagesRead
                });
        }

        public void ResetAll()
        {
            _data = new Data();
            Save();
        }

        public void ExportSessionsToCsv(string filePath)
        {
            var sep = ",";
            var lines = new List<string> { "ComicTitle,StartTime,EndTime,DurationMinutes,PagesRead" };
            lines.AddRange(_data.Sessions.Select(s => string.Join(sep, new[]
            {
                Escape(s.ComicTitle),
                s.StartTime.ToString("s"),
                s.EndTime.ToString("s"),
                ((int)(s.EndTime - s.StartTime).TotalMinutes).ToString(),
                s.PagesRead.ToString()
            })));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllLines(filePath, lines);

            static string Escape(string v) => '"' + (v?.Replace("\"", "\"\"") ?? string.Empty) + '"';
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_dataFile))
                {
                    var json = File.ReadAllText(_dataFile);
                    _data = JsonSerializer.Deserialize<Data>(json) ?? new Data();
                }
                else
                {
                    // Migrar desde ruta antigua si existe
                    var oldRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComicReader");
                    var oldFile = Path.Combine(oldRoot, "readingStats.json");
                    if (File.Exists(oldFile))
                    {
                        var json = File.ReadAllText(oldFile);
                        _data = JsonSerializer.Deserialize<Data>(json) ?? new Data();
                        try
                        {
                            Directory.CreateDirectory(_rootDir);
                            File.Copy(oldFile, _dataFile, overwrite: true);
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                _data = new Data();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(_rootDir);
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataFile, json);
            }
            catch { }
        }

        private class Data
        {
            public List<SessionRecord> Sessions { get; set; } = new();
            public List<ComicProgressRecord> Progress { get; set; } = new();
        }

        private class SessionRecord
        {
            public string ComicPath { get; set; }
            public string ComicTitle { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public int PagesRead { get; set; }
        }

        private class ComicProgressRecord
        {
            public string ComicPath { get; set; }
            public string Title { get; set; }
            public int Progress { get; set; }
            public int TotalPages { get; set; }
            public DateTime LastRead { get; set; }
        }

        private class ActiveSession
        {
            public string ComicPath { get; set; }
            public string ComicTitle { get; set; }
            public int TotalPages { get; set; }
            public DateTime StartTime { get; set; }
            public int StartPage { get; set; }
            public int LastPage { get; set; }
        }
    }
}
