using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using ComicReader.Models;

namespace ComicReader.Views
{
    public partial class ComicStatsWindow : Window, INotifyPropertyChanged
    {
    private ReadingStats _stats;
    private ObservableCollection<ComicProgress> _recentProgress;
    private ObservableCollection<ReadingSession> _todaySessions;
    private ComicReader.Core.Abstractions.IReadingStatsService _statsService => ComicReader.Core.Services.ServiceLocator.TryGet<ComicReader.Core.Abstractions.IReadingStatsService>();

        public ReadingStats Stats
        {
            get => _stats;
            set
            {
                _stats = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<ComicProgress> RecentProgress
        {
            get => _recentProgress;
            set
            {
                _recentProgress = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<ReadingSession> TodaySessions
        {
            get => _todaySessions;
            set
            {
                _todaySessions = value;
                OnPropertyChanged();
            }
        }

        public ComicStatsWindow()
        {
            #pragma warning disable
            InitializeComponent();
            #pragma warning restore
            DataContext = this;
            LoadStats();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LoadStats()
        {
            var dash = _statsService?.GetDashboard();
            if (dash == null)
            {
                Stats = new ReadingStats();
                RecentProgress = new ObservableCollection<ComicProgress>();
                TodaySessions = new ObservableCollection<ReadingSession>();
                return;
            }

            Stats = new ReadingStats
            {
                TotalComicsRead = dash.TotalComicsRead,
                TotalPagesRead = dash.TotalPagesRead,
                TotalReadingTime = dash.TotalReadingTime,
                AverageReadingTime = dash.AverageReadingTime,
                ComicsThisWeek = dash.ComicsThisWeek,
                ComicsThisMonth = dash.ComicsThisMonth,
                FavoriteGenre = dash.FavoriteGenre,
                FavoriteDay = dash.FavoriteDay,
                PreferredFormat = dash.PreferredFormat,
                LongestReadingSession = dash.LongestReadingSession,
                CurrentStreak = dash.CurrentStreak
            };

            RecentProgress = new ObservableCollection<ComicProgress>(
                (_statsService?.GetRecentProgress(20) ?? Array.Empty<ComicReader.Core.Abstractions.ComicProgressInfo>())
                .Select(p => new ComicProgress { Title = p.Title, Progress = p.Progress, TotalPages = p.TotalPages, LastRead = p.LastRead })
            );

            TodaySessions = new ObservableCollection<ReadingSession>(
                (_statsService?.GetTodaySessions() ?? Array.Empty<ComicReader.Core.Abstractions.ReadingSessionInfo>())
                .Select(s => new ReadingSession { ComicTitle = s.ComicTitle, StartTime = s.StartTime, Duration = s.Duration, PagesRead = s.PagesRead })
            );
        }

        private void ResetStats_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("¿Estás seguro de que deseas resetear todas las estadísticas?", 
                "Confirmar Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                _statsService?.ResetAll();
                LoadStats();
                MessageBox.Show("Estadísticas reseteadas.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExportStats_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar Estadísticas",
                Filter = "Archivo CSV|*.csv|Archivo de Texto|*.txt",
                DefaultExt = ".csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                _statsService?.ExportSessionsToCsv(saveDialog.FileName);
                MessageBox.Show($"Estadísticas exportadas a: {saveDialog.FileName}", 
                    "Exportación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

namespace ComicReader.Models
{
    public class ReadingStats : INotifyPropertyChanged
    {
        private int _totalComicsRead;
        private int _totalPagesRead;
        private TimeSpan _totalReadingTime;
        private TimeSpan _averageReadingTime;
        private int _comicsThisWeek;
        private int _comicsThisMonth;
        private string _favoriteGenre;
        private string _favoriteDay;
        private string _preferredFormat;
        private TimeSpan _longestReadingSession;
        private int _currentStreak;

        public int TotalComicsRead
        {
            get => _totalComicsRead;
            set { _totalComicsRead = value; OnPropertyChanged(); }
        }

        public int TotalPagesRead
        {
            get => _totalPagesRead;
            set { _totalPagesRead = value; OnPropertyChanged(); }
        }

        public TimeSpan TotalReadingTime
        {
            get => _totalReadingTime;
            set { _totalReadingTime = value; OnPropertyChanged(); }
        }

        public TimeSpan AverageReadingTime
        {
            get => _averageReadingTime;
            set { _averageReadingTime = value; OnPropertyChanged(); }
        }

        public int ComicsThisWeek
        {
            get => _comicsThisWeek;
            set { _comicsThisWeek = value; OnPropertyChanged(); }
        }

        public int ComicsThisMonth
        {
            get => _comicsThisMonth;
            set { _comicsThisMonth = value; OnPropertyChanged(); }
        }

        public string FavoriteGenre
        {
            get => _favoriteGenre;
            set { _favoriteGenre = value; OnPropertyChanged(); }
        }

        public string FavoriteDay
        {
            get => _favoriteDay;
            set { _favoriteDay = value; OnPropertyChanged(); }
        }

        public string PreferredFormat
        {
            get => _preferredFormat;
            set { _preferredFormat = value; OnPropertyChanged(); }
        }

        public TimeSpan LongestReadingSession
        {
            get => _longestReadingSession;
            set { _longestReadingSession = value; OnPropertyChanged(); }
        }

        public int CurrentStreak
        {
            get => _currentStreak;
            set { _currentStreak = value; OnPropertyChanged(); }
        }

        public string TotalReadingTimeFormatted => $"{(int)TotalReadingTime.TotalHours}h {TotalReadingTime.Minutes}m";
        public string AverageReadingTimeFormatted => $"{(int)AverageReadingTime.TotalMinutes}m";
        public string LongestSessionFormatted => $"{(int)LongestReadingSession.TotalHours}h {LongestReadingSession.Minutes}m";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ComicProgress : INotifyPropertyChanged
    {
        private string _title;
        private int _progress;
        private int _totalPages;
        private DateTime _lastRead;

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercentage)); }
        }

        public int TotalPages
        {
            get => _totalPages;
            set { _totalPages = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercentage)); }
        }

        public DateTime LastRead
        {
            get => _lastRead;
            set { _lastRead = value; OnPropertyChanged(); }
        }

        // Nota: Algunos controles (p. ej., RangeBase.Value) enlazan en modo TwoWay por defecto.
        // Para evitar excepciones si un estilo/plantilla fuerza TwoWay, exponemos un setter inofensivo.
        public double ProgressPercentage
        {
            get => TotalPages > 0 ? (double)Progress / TotalPages * 100 : 0;
            set { /* Setter intencionalmente vacío para soportar bindings TwoWay; no modifica el estado. */ }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ReadingSession : INotifyPropertyChanged
    {
        private string _comicTitle;
        private DateTime _startTime;
        private TimeSpan _duration;
        private int _pagesRead;

        public string ComicTitle
        {
            get => _comicTitle;
            set { _comicTitle = value; OnPropertyChanged(); }
        }

        public DateTime StartTime
        {
            get => _startTime;
            set { _startTime = value; OnPropertyChanged(); }
        }

        public TimeSpan Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); }
        }

        public int PagesRead
        {
            get => _pagesRead;
            set { _pagesRead = value; OnPropertyChanged(); }
        }

        public string DurationFormatted => $"{(int)Duration.TotalMinutes}m";
        public string StartTimeFormatted => StartTime.ToString("HH:mm");

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}