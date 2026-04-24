using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace ComicReader.Views
{
    /// <summary>
    /// Interaction logic for PresentationModeWindow.xaml
    /// </summary>
    public partial class PresentationModeWindow : Window, INotifyPropertyChanged
    {
        private DispatcherTimer _presentationTimer;
        private int _currentSlideInterval = 5;
        private bool _isPlaying;
        private int _totalSlides;
        private int _currentSlide = 1;
        private string _currentComicTitle;

        public int CurrentSlideInterval
        {
            get => _currentSlideInterval;
            set
            {
                _currentSlideInterval = value;
                OnPropertyChanged();
                if (_presentationTimer != null)
                    _presentationTimer.Interval = TimeSpan.FromSeconds(value);
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayPauseIcon));
                OnPropertyChanged(nameof(PlayPauseText));
            }
        }

        public int TotalSlides
        {
            get => _totalSlides;
            set
            {
                _totalSlides = value;
                OnPropertyChanged();
            }
        }

        public int CurrentSlide
        {
            get => _currentSlide;
            set
            {
                _currentSlide = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }

        public string CurrentComicTitle
        {
            get => _currentComicTitle;
            set
            {
                _currentComicTitle = value;
                OnPropertyChanged();
            }
        }

        public string PlayPauseIcon => IsPlaying ? "⏸️" : "▶️";
        public string PlayPauseText => IsPlaying ? "Pausar" : "Reproducir";
        public double ProgressPercentage => TotalSlides > 0 ? (double)CurrentSlide / TotalSlides * 100 : 0;

        public PresentationModeWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeTimer();
            
            // Datos de ejemplo
            CurrentComicTitle = "Batman: Year One";
            TotalSlides = 120;
            CurrentSlide = 1;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void InitializeTimer()
        {
            _presentationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(CurrentSlideInterval)
            };
            _presentationTimer.Tick += PresentationTimer_Tick;
        }

        private void PresentationTimer_Tick(object sender, EventArgs e)
        {
            if (CurrentSlide < TotalSlides)
            {
                CurrentSlide++;
                // Aquí llamarías al método para avanzar la página en el MainWindow
                NotifyMainWindowNextPage();
            }
            else
            {
                // Fin de la presentación
                StopPresentation();
                ComicReader.Services.Notifications.NotificationService.Instance.Success("Presentación completada", "Finalizado");
            }
        }

        private void NotifyMainWindowNextPage()
        {
            // En una implementación real, esto comunicaría con MainWindow
            // Por ejemplo, usando eventos o una referencia directa
            var mainWindow = Owner as global::ComicReader.MainWindow;
            mainWindow?.NextPage_Click(null, null);
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (IsPlaying)
            {
                StopPresentation();
            }
            else
            {
                StartPresentation();
            }
        }

        private void StartPresentation()
        {
            IsPlaying = true;
            _presentationTimer.Start();
        }

        private void StopPresentation()
        {
            IsPlaying = false;
            _presentationTimer.Stop();
        }

        private void PreviousSlide_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentSlide > 1)
            {
                CurrentSlide--;
                var mainWindow = Owner as global::ComicReader.MainWindow;
                mainWindow?.PrevPage_Click(null, null);
            }
        }

        private void NextSlide_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentSlide < TotalSlides)
            {
                CurrentSlide++;
                var mainWindow = Owner as global::ComicReader.MainWindow;
                mainWindow?.NextPage_Click(null, null);
            }
        }

        private void ResetPresentation_Click(object sender, RoutedEventArgs e)
        {
            StopPresentation();
            CurrentSlide = 1;
            // Ir a la primera página
            var mainWindow = Owner as global::ComicReader.MainWindow;
            // mainWindow?.GoToFirstPage();
        }

        private void ClosePresentation_Click(object sender, RoutedEventArgs e)
        {
            StopPresentation();
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            StopPresentation();
            base.OnClosing(e);
        }
    }
}