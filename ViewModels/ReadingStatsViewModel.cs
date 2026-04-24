using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ComicReader.Core.Abstractions;
using ComicReader.Services;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ComicReader.ViewModels
{
    public class StatItem : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _value = "0";
        private string _caption = string.Empty;
        private string _glyph = "\uE8D7";
        private Brush _accentBrush = Brushes.DeepSkyBlue;
        private double _trendDelta;
        private string _trendLabel = string.Empty;

        public string Label { get => _label; set => Set(ref _label, value); }
        public string Value { get => _value; set => Set(ref _value, value); }
        public string Caption { get => _caption; set => Set(ref _caption, value); }
        public string Glyph { get => _glyph; set => Set(ref _glyph, value); }
        public Brush AccentBrush { get => _accentBrush; set => Set(ref _accentBrush, value); }
        public double TrendDelta { get => _trendDelta; set { if (Set(ref _trendDelta, value)) OnPropertyChanged(nameof(TrendDeltaText)); } }
        public string TrendLabel { get => _trendLabel; set => Set(ref _trendLabel, value); }
        public string TrendDeltaText => TrendDelta >= 0 ? $"+{TrendDelta:0.#}%" : $"{TrendDelta:0.#}%";
        public bool HasTrend => !string.IsNullOrWhiteSpace(TrendLabel);

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ProgressItem : INotifyPropertyChanged
    {
        private string _title;
        private double _percentage;
        private string _thumbnailPath;

        public string Title { get => _title; set { if (_title != value) { _title = value; OnPropertyChanged(); } } }
        public double Percentage { get => _percentage; set { if (Math.Abs(_percentage - value) > 0.01) { _percentage = value; OnPropertyChanged(); } } }
        public string ThumbnailPath { get => _thumbnailPath; set { if (_thumbnailPath != value) { _thumbnailPath = value; OnPropertyChanged(); } } }
        public string PagesText { get; set; }
        public string LastReadText { get; set; }
        public string AccessibilityHelp { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class HeroHighlight
    {
        public string Glyph { get; set; } = "\uE80F";
        public string Title { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public Brush AccentBrush { get; set; } = Brushes.DeepSkyBlue;
        public Brush AccentShadowBrush { get; set; } = Brushes.SteelBlue;
    }

    public class CompletionLeaderItem
    {
        public string Title { get; set; } = string.Empty;
        public double Completion { get; set; }
        public string CompletionText => $"{Completion:0.#}%";
        public string PagesText { get; set; } = string.Empty;
        public string LastReadText { get; set; } = string.Empty;
    }

    public class ReadingStatsViewModel : INotifyPropertyChanged
    {
        private readonly string _favoriteGenreDefault = "—";
        private string _favoriteGenre = "—";
        private string _favoriteDay = "—";
        private string _preferredFormat = "—";
        private readonly IReadingStatsService _statsService = ComicReader.Core.Services.ServiceLocator.TryGet<IReadingStatsService>();
        private readonly DispatcherTimer _refreshTimer;
        private const string ThumbCacheVersion = "v2";
        private readonly SemaphoreSlim _thumbSemaphore;
        private readonly ConcurrentDictionary<string, Task> _thumbTasks = new ConcurrentDictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        private readonly CultureInfo _culture = CultureInfo.CurrentUICulture;

        private ISeries[] _dailyActivitySeries = Array.Empty<ISeries>();
        private Axis[] _dailyActivityXAxes = Array.Empty<Axis>();
        private Axis[] _dailyActivityYAxes = Array.Empty<Axis>();
        private ISeries[] _dayOfWeekSeries = Array.Empty<ISeries>();
        private Axis[] _dayOfWeekValueAxes = Array.Empty<Axis>();
        private Axis[] _dayOfWeekCategoryAxes = Array.Empty<Axis>();
        private ISeries[] _genreShareSeries = Array.Empty<ISeries>();

        private static readonly SolidColorBrush AccentCyan = CreateBrush("#FF1EA7FF");
        private static readonly SolidColorBrush AccentPurple = CreateBrush("#FFB388FF");
        private static readonly SolidColorBrush AccentLime = CreateBrush("#FF63E6BE");
        private static readonly SolidColorBrush AccentAmber = CreateBrush("#FFFFC75F");
        private static readonly SolidColorBrush AccentPink = CreateBrush("#FFFF87D7");
        private static readonly SolidColorBrush AccentBlue = CreateBrush("#FF60A5FA");
        private static readonly SolidColorBrush AccentOrange = CreateBrush("#FFFF944D");
        private static readonly SolidColorPaint AxisPaint = new SolidColorPaint(new SKColor(206, 224, 247, 220));
        private static readonly SKColor[] GenrePalette = new[]
        {
            new SKColor(92, 153, 255),
            new SKColor(111, 207, 255),
            new SKColor(166, 110, 255),
            new SKColor(255, 159, 243),
            new SKColor(255, 203, 112),
            new SKColor(255, 111, 145),
            new SKColor(120, 220, 150),
            new SKColor(103, 232, 249)
        };

        public ObservableCollection<StatItem> Stats { get; } = new ObservableCollection<StatItem>();
        public ObservableCollection<ProgressItem> ProgressList { get; } = new ObservableCollection<ProgressItem>();
        public ObservableCollection<HeroHighlight> HeroHighlights { get; } = new ObservableCollection<HeroHighlight>();
        public ObservableCollection<CompletionLeaderItem> CompletionLeaders { get; } = new ObservableCollection<CompletionLeaderItem>();

        public class SessionItem
        {
            public string StartText { get; set; }
            public string Title { get; set; }
            public string DurationText { get; set; }
            public string PagesText { get; set; }
            public string HelpText { get; set; }
            public string ComicPath { get; set; }
        }

        public ObservableCollection<SessionItem> TodaySessions { get; } = new ObservableCollection<SessionItem>();

        public string FavoriteGenre { get => _favoriteGenre; set { if (_favoriteGenre != value) { _favoriteGenre = value; OnPropertyChanged(); } } }
        public string FavoriteDay { get => _favoriteDay; set { if (_favoriteDay != value) { _favoriteDay = value; OnPropertyChanged(); } } }
        public string PreferredFormat { get => _preferredFormat; set { if (_preferredFormat != value) { _preferredFormat = value; OnPropertyChanged(); } } }

        public ISeries[] DailyActivitySeries { get => _dailyActivitySeries; private set { _dailyActivitySeries = value ?? Array.Empty<ISeries>(); OnPropertyChanged(); OnPropertyChanged(nameof(Series)); } }
        public Axis[] DailyActivityXAxes { get => _dailyActivityXAxes; private set { _dailyActivityXAxes = value ?? Array.Empty<Axis>(); OnPropertyChanged(); OnPropertyChanged(nameof(Labels)); } }
        public Axis[] DailyActivityYAxes { get => _dailyActivityYAxes; private set { _dailyActivityYAxes = value ?? Array.Empty<Axis>(); OnPropertyChanged(); } }
        public ISeries[] DayOfWeekSeries { get => _dayOfWeekSeries; private set { _dayOfWeekSeries = value ?? Array.Empty<ISeries>(); OnPropertyChanged(); } }
        public Axis[] DayOfWeekValueAxes { get => _dayOfWeekValueAxes; private set { _dayOfWeekValueAxes = value ?? Array.Empty<Axis>(); OnPropertyChanged(); } }
        public Axis[] DayOfWeekCategoryAxes { get => _dayOfWeekCategoryAxes; private set { _dayOfWeekCategoryAxes = value ?? Array.Empty<Axis>(); OnPropertyChanged(); } }
        public ISeries[] GenreShareSeries { get => _genreShareSeries; private set { _genreShareSeries = value ?? Array.Empty<ISeries>(); OnPropertyChanged(); } }

        // Legacy bindings maintained for compatibility
        public ISeries[] Series => DailyActivitySeries;
        public string[] Labels
        {
            get
            {
                var firstAxis = DailyActivityXAxes.FirstOrDefault();
                if (firstAxis?.Labels == null)
                    return Array.Empty<string>();
                return firstAxis.Labels.ToArray();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ReadingStatsViewModel()
        {
            EnsureDefaultStats();
            TryApplySavedOrder();
            SeedHeroHighlights();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += (s, e) => Refresh();
            _refreshTimer.Start();

            var concurrency = 2;
            try { concurrency = Math.Max(1, SettingsManager.Settings.ReadingStatsThumbConcurrency); } catch { }
            _thumbSemaphore = new SemaphoreSlim(concurrency);

            Refresh();
            Stats.CollectionChanged += (s, e) => SaveOrder();
        }

        private void SeedHeroHighlights()
        {
            ReplaceCollection(HeroHighlights, new[]
            {
                new HeroHighlight { Title = "Tiempo total", Value = "0m", Subtitle = "Sueños en papel", Glyph = "\uE823", AccentBrush = AccentBlue, AccentShadowBrush = AccentCyan },
                new HeroHighlight { Title = "Racha activa", Value = "0", Subtitle = "Días consecutivos", Glyph = "\uEADF", AccentBrush = AccentCyan, AccentShadowBrush = AccentPurple },
                new HeroHighlight { Title = "Cómics completados", Value = "0", Subtitle = "Victorias épicas", Glyph = "\uE915", AccentBrush = AccentLime, AccentShadowBrush = AccentAmber }
            });
        }

        public void Refresh()
        {
            try
            {
                if (_statsService == null)
                {
                    // Servicio no disponible: mostrar contenido de marcador para evitar pantalla vacía
                    DailyActivitySeries = new ISeries[]
                    {
                        new ColumnSeries<double>
                        {
                            Values = new double[] { 0 },
                            Name = "Sin datos",
                            Fill = new SolidColorPaint(new SKColor(96, 165, 250)), // azul visible sobre fondo oscuro
                            Stroke = new SolidColorPaint(new SKColor(59, 130, 246))
                        }
                    };
                    DayOfWeekSeries = new ISeries[]
                    {
                        new ColumnSeries<double>
                        {
                            Values = new double[] { 0 },
                            Name = "Sin datos",
                            Fill = new SolidColorPaint(new SKColor(181, 126, 255)),
                            Stroke = new SolidColorPaint(new SKColor(157, 78, 221))
                        }
                    };
                    GenreShareSeries = Array.Empty<ISeries>();
                    return;
                }

                var dash = _statsService.GetDashboard();
                var insights = _statsService.GetInsights(30);

                if (dash != null)
                {
                    UpdateStat("Cómics Leídos", dash.TotalComicsRead.ToString(CultureInfo.InvariantCulture));
                    UpdateStat("Páginas Leídas", dash.TotalPagesRead.ToString(CultureInfo.InvariantCulture));
                    UpdateStat("Tiempo Total", FormatTimeSpan(dash.TotalReadingTime));
                    UpdateStat("Días Consecutivos", dash.CurrentStreak.ToString(CultureInfo.InvariantCulture));
                    UpdateStat("Esta Semana", dash.ComicsThisWeek.ToString(CultureInfo.InvariantCulture));
                    UpdateStat("Este Mes", dash.ComicsThisMonth.ToString(CultureInfo.InvariantCulture));
                    UpdateStat("Promedio/Sesión", FormatTimeSpan(dash.AverageReadingTime));
                    UpdateStat("Sesión Más Larga", FormatTimeSpan(dash.LongestReadingSession));

                    FavoriteGenre = string.IsNullOrWhiteSpace(dash.FavoriteGenre) ? _favoriteGenreDefault : dash.FavoriteGenre;
                    FavoriteDay = string.IsNullOrWhiteSpace(dash.FavoriteDay) ? "—" : dash.FavoriteDay;
                    PreferredFormat = string.IsNullOrWhiteSpace(dash.PreferredFormat) ? "—" : dash.PreferredFormat;
                }

                UpdateStatTrends(insights);
                UpdateHeroHighlights(dash, insights);
                UpdateCharts(insights);
                UpdateCompletionLeaderboard(insights);

                ProgressList.Clear();
                foreach (var p in _statsService.GetRecentProgress(10))
                {
                    var percent = p.TotalPages > 0 ? (double)p.Progress * 100.0 / p.TotalPages : 0.0;
                    var thumb = TryGetCachedThumbPath(p.ComicPath);
                    var item = new ProgressItem
                    {
                        Title = p.Title,
                        Percentage = percent,
                        ThumbnailPath = thumb,
                        PagesText = p.TotalPages > 0 ? $"{p.Progress} / {p.TotalPages} páginas" : "Progreso sincronizado",
                        LastReadText = p.LastRead.ToString("g", _culture),
                        AccessibilityHelp = $"{p.Title}. {percent:0}% completado."
                    };
                    ProgressList.Add(item);

                    if (string.IsNullOrWhiteSpace(thumb) && !string.IsNullOrWhiteSpace(p.ComicPath) && (File.Exists(p.ComicPath) || Directory.Exists(p.ComicPath)))
                    {
                        if (!_thumbTasks.ContainsKey(p.ComicPath))
                        {
                            var t = GenerateAndSaveThumbAsync(p.ComicPath, item);
                            _thumbTasks.TryAdd(p.ComicPath, t);
                            _ = t.ContinueWith(_ => { _thumbTasks.TryRemove(p.ComicPath, out _); });
                        }
                    }
                }

                TodaySessions.Clear();
                foreach (var s in _statsService.GetTodaySessions())
                {
                    var help = $"Hora: {s.StartTime:HH:mm}; Duración: {(s.Duration.TotalMinutes >= 60 ? string.Format(_culture, "{0}h {1}m", (int)s.Duration.TotalHours, s.Duration.Minutes) : string.Format(_culture, "{0}m", (int)s.Duration.TotalMinutes))};" + (s.PagesRead > 0 ? $" Páginas: {s.PagesRead}" : string.Empty);
                    TodaySessions.Add(new SessionItem
                    {
                        StartText = s.StartTime.ToString("HH:mm", _culture),
                        Title = s.ComicTitle,
                        ComicPath = (s is ReadingSessionInfo rsi) ? rsi.ComicPath : null,
                        DurationText = s.Duration.TotalMinutes >= 60 ? string.Format(_culture, "{0}h {1}m", (int)s.Duration.TotalHours, s.Duration.Minutes) : string.Format(_culture, "{0}m", (int)s.Duration.TotalMinutes),
                        PagesText = s.PagesRead > 0 ? s.PagesRead + "p" : string.Empty,
                        HelpText = help
                    });
                }
            }
            catch
            {
            }
        }

        private void UpdateStatTrends(ReadingInsights insights)
        {
            if (insights == null || insights.DailyActivity == null || insights.DailyActivity.Count == 0)
                return;

            var pagesTrend = CalculateTrend(insights.DailyActivity.Select(d => (double)d.Pages).ToList(), 7);
            var minutesTrend = CalculateTrend(insights.DailyActivity.Select(d => d.Minutes).ToList(), 7);
            var weekSeries = insights.DailyActivity.Select(d => d.Pages).ToList();

            UpdateStat("Páginas Leídas", null, trendDelta: pagesTrend, trendLabel: "vs semana previa");
            UpdateStat("Tiempo Total", null, trendDelta: minutesTrend, trendLabel: "vs semana previa");

            var weekNow = weekSeries.Skip(Math.Max(0, weekSeries.Count - 7)).Sum();
            var weekPrev = weekSeries.Skip(Math.Max(0, weekSeries.Count - 14)).Take(Math.Min(7, Math.Max(0, weekSeries.Count - 7))).Sum();
            var weekDelta = CalculateDelta(weekPrev, weekNow);
            UpdateStat("Esta Semana", null, trendDelta: weekDelta, trendLabel: "vs semana previa");
        }

        private void UpdateHeroHighlights(StatsDashboard dash, ReadingInsights insights)
        {
            if (dash == null)
                return;

            var heroItems = new List<HeroHighlight>
            {
                new HeroHighlight
                {
                    Glyph = "\uE823",
                    Title = "Tiempo total",
                    Value = FormatLongTime(dash.TotalReadingTime),
                    Subtitle = "Minutos soñando en viñetas",
                    AccentBrush = AccentBlue,
                    AccentShadowBrush = AccentCyan
                },
                new HeroHighlight
                {
                    Glyph = "\uEADF",
                    Title = "Racha activa",
                    Value = dash.CurrentStreak.ToString(CultureInfo.InvariantCulture),
                    Subtitle = dash.CurrentStreak == 1 ? "Un día encendido" : "Días consecutivos leyendo",
                    AccentBrush = AccentCyan,
                    AccentShadowBrush = AccentPurple
                },
                new HeroHighlight
                {
                    Glyph = "\uE915",
                    Title = "Cómics completados",
                    Value = dash.TotalComicsRead.ToString(CultureInfo.InvariantCulture),
                    Subtitle = "Historias terminadas",
                    AccentBrush = AccentLime,
                    AccentShadowBrush = AccentAmber
                }
            };

            var bestHour = insights?.HourlyActivity?.OrderByDescending(h => h.Minutes).FirstOrDefault();
            if (bestHour != null)
            {
                heroItems.Add(new HeroHighlight
                {
                    Glyph = "\uE823",
                    Title = string.Format(_culture, "Hora pico {0:00}:00", bestHour.Hour),
                    Value = string.Format(_culture, "{0:0.#}m", bestHour.Minutes),
                    Subtitle = "Cuando más lees",
                    AccentBrush = AccentPink,
                    AccentShadowBrush = AccentBlue
                });
            }

            ReplaceCollection(HeroHighlights, heroItems.Take(3));
        }

        private void UpdateCompletionLeaderboard(ReadingInsights insights)
        {
            if (insights == null)
            {
                CompletionLeaders.Clear();
                return;
            }

            var items = insights.CompletionLeaderboard
                .Select(entry => new CompletionLeaderItem
                {
                    Title = entry.Title,
                    Completion = entry.Completion,
                    PagesText = entry.TotalPages > 0 ? $"{entry.PagesRead}/{entry.TotalPages} páginas" : "Progreso parcial",
                    LastReadText = entry.LastRead.ToString("g", _culture)
                })
                .ToList();

            ReplaceCollection(CompletionLeaders, items);
        }

        private void UpdateCharts(ReadingInsights insights)
        {
            try
            {
                var daily = insights?.DailyActivity?.ToList() ?? new List<DailyActivityPoint>();
                if (daily.Count == 0)
                {
                    DailyActivitySeries = new ISeries[]
                    {
                        new ColumnSeries<double>
                        {
                            Values = new double[] { 0 },
                            Name = "Sin datos",
                            Fill = new SolidColorPaint(new SKColor(96, 165, 250)),
                            Stroke = new SolidColorPaint(new SKColor(59, 130, 246))
                        }
                    };
                }
                else
                {
                    DailyActivitySeries = new ISeries[]
                    {
                        new ColumnSeries<double>
                        {
                            Values = daily.Select(d => (double)d.Pages).ToArray(),
                            Name = "Páginas",
                            Fill = new SolidColorPaint(new SKColor(96, 165, 250)),
                            Stroke = new SolidColorPaint(new SKColor(59, 130, 246))
                        }
                    };
                }

                var byDay = insights?.DayOfWeekActivity?.ToList() ?? new List<DayOfWeekActivityPoint>();
                if (byDay.Count == 0)
                {
                    DayOfWeekSeries = new ISeries[]
                    {
                        new ColumnSeries<double>
                        {
                            Values = new double[] { 0 },
                            Name = "Sin datos",
                            Fill = new SolidColorPaint(new SKColor(181, 126, 255)),
                            Stroke = new SolidColorPaint(new SKColor(157, 78, 221))
                        }
                    };
                }
                else
                {
                    DayOfWeekSeries = new ISeries[]
                    {
                        new ColumnSeries<double>
                        {
                            Values = byDay.Select(d => Math.Round(d.Minutes, 1)).ToArray(),
                            Name = "Minutos",
                            Fill = new SolidColorPaint(new SKColor(181, 126, 255)),
                            Stroke = new SolidColorPaint(new SKColor(157, 78, 221))
                        }
                    };
                }

                var genres = insights?.GenreDistribution?.ToList() ?? new List<GenreDistributionPoint>();
                if (genres.Count == 0)
                {
                    GenreShareSeries = Array.Empty<ISeries>();
                }
                else
                {
                    GenreShareSeries = genres.Select(g => new PieSeries<double>
                    {
                        Values = new double[] { g.Count },
                        Name = g.Genre,
                        Fill = new SolidColorPaint(GenrePalette[Math.Abs(g.Genre?.GetHashCode() ?? 0) % GenrePalette.Length])
                    } as ISeries).ToArray();
                }

                // Update axes properties for compatibility
                DailyActivityXAxes = Array.Empty<Axis>();
                DailyActivityYAxes = Array.Empty<Axis>();
                DayOfWeekValueAxes = Array.Empty<Axis>();
                DayOfWeekCategoryAxes = Array.Empty<Axis>();
            }
            catch
            {
                DailyActivitySeries = Array.Empty<ISeries>();
                DayOfWeekSeries = Array.Empty<ISeries>();
                GenreShareSeries = Array.Empty<ISeries>();
            }
        }

        private void UpdateStat(string label, string value, SolidColorBrush accent = null, string caption = null, string glyph = null, double? trendDelta = null, string trendLabel = null)
        {
            var item = Stats.FirstOrDefault(s => string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            if (value != null) item.Value = value;
            if (!string.IsNullOrWhiteSpace(caption)) item.Caption = caption;
            if (!string.IsNullOrWhiteSpace(glyph)) item.Glyph = glyph;
            if (accent != null) item.AccentBrush = accent;

            if (trendDelta.HasValue)
            {
                item.TrendDelta = trendDelta.Value;
                item.TrendLabel = trendLabel ?? string.Empty;
            }
            else
            {
                item.TrendLabel = string.Empty;
                item.TrendDelta = 0;
            }
        }

        private double CalculateTrend(IReadOnlyList<double> values, int window)
        {
            if (values == null || values.Count == 0) return 0;

            var current = values.Skip(Math.Max(0, values.Count - window)).Sum();
            var previous = values.Skip(Math.Max(0, values.Count - 2 * window)).Take(Math.Min(window, Math.Max(0, values.Count - window))).Sum();
            return CalculateDelta(previous, current);
        }

        private static double CalculateDelta(double previous, double current)
        {
            if (previous <= double.Epsilon)
                return current <= double.Epsilon ? 0 : 100;
            return ((current - previous) / previous) * 100.0;
        }

        private void EnsureDefaultStats()
        {
            if (Stats.Count > 0) return;

            Stats.Add(new StatItem { Label = "Cómics Leídos", Value = "0", Caption = "Finalizados", Glyph = "\uE8FB", AccentBrush = AccentLime });
            Stats.Add(new StatItem { Label = "Páginas Leídas", Value = "0", Caption = "Total histórico", Glyph = "\uE714", AccentBrush = AccentCyan });
            Stats.Add(new StatItem { Label = "Tiempo Total", Value = "0m", Caption = "Invertidos leyendo", Glyph = "\uE823", AccentBrush = AccentPurple });
            Stats.Add(new StatItem { Label = "Días Consecutivos", Value = "0", Caption = "Tu mejor racha", Glyph = "\uEADF", AccentBrush = AccentAmber });
            Stats.Add(new StatItem { Label = "Esta Semana", Value = "0", Caption = "Historias nuevas", Glyph = "\uE81E", AccentBrush = AccentBlue });
            Stats.Add(new StatItem { Label = "Este Mes", Value = "0", Caption = "Cómics del mes", Glyph = "\uE163", AccentBrush = AccentOrange });
            Stats.Add(new StatItem { Label = "Promedio/Sesión", Value = "0m", Caption = "Tiempo medio", Glyph = "\uF167", AccentBrush = AccentPink });
            Stats.Add(new StatItem { Label = "Sesión Más Larga", Value = "0m", Caption = "Maratón épica", Glyph = "\uEAD8", AccentBrush = AccentPurple });
        }

        private void TryApplySavedOrder()
        {
            try
            {
                var order = SettingsManager.Settings.ReadingStatsModuleOrder;
                if (order != null && order.Length > 0 && Stats.Count > 0)
                {
                    var map = new Dictionary<string, StatItem>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in Stats) map[s.Label] = s;
                    var reordered = new ObservableCollection<StatItem>();
                    foreach (var key in order)
                    {
                        if (map.TryGetValue(key, out var item))
                        {
                            reordered.Add(item);
                            map.Remove(key);
                        }
                    }
                    foreach (var kv in map) reordered.Add(kv.Value);
                    Stats.Clear();
                    foreach (var it in reordered) Stats.Add(it);
                }
            }
            catch { }
        }

        private void SaveOrder()
        {
            try
            {
                var labels = new List<string>();
                foreach (var it in Stats) labels.Add(it.Label);
                SettingsManager.Settings.ReadingStatsModuleOrder = labels.ToArray();
                SettingsManager.SaveSettings();
            }
            catch { }
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", (int)ts.TotalHours, ts.Minutes);
            return string.Format(CultureInfo.InvariantCulture, "{0}m", Math.Max(1, (int)Math.Round(ts.TotalMinutes))); 
        }

        private static string FormatLongTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 24)
            {
                var days = (int)ts.TotalDays;
                var hours = ts.Hours;
                return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h", days, hours);
            }

            if (ts.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", (int)ts.TotalHours, ts.Minutes);

            return string.Format(CultureInfo.InvariantCulture, "{0}m", Math.Max(1, (int)Math.Round(ts.TotalMinutes)));
        }

        private string TryGetCachedThumbPath(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return null;
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "PercysLibrary", "Thumbs");
                Directory.CreateDirectory(dir);
                using (var sha1 = System.Security.Cryptography.SHA1.Create())
                {
                    var key = ThumbCacheVersion + "|" + filePath;
                    var hash = BitConverter.ToString(sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", string.Empty);
                    var p = Path.Combine(dir, hash + ".png");
                    return File.Exists(p) ? p : null;
                }
            }
            catch { return null; }
        }

        private string ComputeThumbCachePath(string filePath)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "PercysLibrary", "Thumbs");
            Directory.CreateDirectory(dir);
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                var key = ThumbCacheVersion + "|" + filePath;
                var hash = BitConverter.ToString(sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", string.Empty);
                return Path.Combine(dir, hash + ".png");
            }
        }

        private async Task GenerateAndSaveThumbAsync(string filePath, ProgressItem item)
        {
            await _thumbSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                BitmapSource cover = null;
                try
                {
                    using (var loader = new ComicPageLoader(filePath))
                    {
                        await loader.LoadComicAsync().ConfigureAwait(false);
                        cover = await loader.GetCoverThumbnailAsync(300, 400).ConfigureAwait(false);
                    }
                }
                catch { }

                if (cover != null)
                {
                    try
                    {
                        var path = ComputeThumbCachePath(filePath);
                        using (var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            var enc = new PngBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(cover));
                            enc.Save(fs);
                        }

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            try { item.ThumbnailPath = path; } catch { }
                        });
                    }
                    catch { }
                }
            }
            finally
            {
                _thumbSemaphore.Release();
            }
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return Brushes.DeepSkyBlue;
            }
        }

        private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
