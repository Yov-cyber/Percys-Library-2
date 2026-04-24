using System;
using System.Windows;
using System.Windows.Input;
using ComicReader.ViewModels;
using Microsoft.Win32;
using ComicReader.Core.Abstractions;
using ComicReader.Core.Services;
using System.IO;

namespace ComicReader.Views
{
    public partial class StatisticsWindow : Window
    {
        public StatisticsWindow()
        {
            // Cargar XAML normalmente; si hay un error de recursos o bindings queremos verlo en logs
            InitializeComponent();
            this.DataContext = new ReadingStatsViewModel();
            
            // ✅ SUSCRIBIRSE A CAMBIOS DE TEMA
            try
            {
                ComicReader.Themes.ThemeManager.ThemeChanged += OnThemeChanged;
                UpdateThemeResources();
            }
            catch { }
        }
        
        private void OnThemeChanged(ComicReader.Services.ThemeMode mode)
        {
            // Ejecutar en UI thread
            this.Dispatcher?.BeginInvoke(new Action(() =>
            {
                try
                {
                    UpdateThemeResources();
                    this.InvalidateVisual();
                    this.UpdateLayout();
                    ComicReader.Utils.ModernLogger.Info("✓ StatisticsWindow actualizada con nuevo tema");
                }
                catch { }
            }));
        }
        
        private void UpdateThemeResources()
        {
            try
            {
                this.Background = this.TryFindResource("WindowBackgroundBrush") as System.Windows.Media.Brush 
                    ?? this.TryFindResource("BackgroundBrush") as System.Windows.Media.Brush
                    ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 18));
            }
            catch { }
        }

        private void SessionItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // detect double-click using ClickCount since Border doesn't expose MouseDoubleClick
                if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is ReadingStatsViewModel.SessionItem item)
                {
                    OpenComicPath(item.ComicPath);
                }
            }
            catch { }
        }

        private void SessionItem_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Enter && sender is FrameworkElement fe && fe.DataContext is ReadingStatsViewModel.SessionItem item)
                {
                    OpenComicPath(item.ComicPath);
                }
            }
            catch { }
        }

        private void OpenComicPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                {
                    ComicReader.Services.Notifications.NotificationService.Instance.Info("El archivo no existe o ha sido movido", "Archivo no encontrado");
                    return;
                }

                var mainWindow = Window.GetWindow(this) as global::ComicReader.MainWindow;
                if (mainWindow != null)
                {
                    var method = mainWindow.GetType().GetMethod("OpenComicFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(mainWindow, new object[] { path });
                }
            }
            catch { }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog()
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = $"reading-sessions-{DateTime.Now:yyyyMMdd}.csv",
                    DefaultExt = ".csv",
                    Title = "Exportar sesiones a CSV"
                };

                if (dlg.ShowDialog(this) == true)
                {
                    var svc = ServiceLocator.TryGet<IReadingStatsService>();
                    if (svc != null)
                    {
                        svc.ExportSessionsToCsv(dlg.FileName);
                        ComicReader.Services.Notifications.NotificationService.Instance.Success("Exportación completada", "Exportación exitosa");
                    }
                    else
                    {
                        ComicReader.Services.Notifications.NotificationService.Instance.Error("Servicio de estadísticas no disponible", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(ex, "Exportar estadísticas", ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var res = MessageBox.Show(this, "¿Deseas resetear todas las estadísticas y la configuración relacionada? Esta acción no se puede deshacer.", "Confirmar reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;

                var svc = ServiceLocator.TryGet<IReadingStatsService>();
                if (svc == null)
                {
                    ComicReader.Services.Notifications.NotificationService.Instance.Error("Servicio de estadísticas no disponible", "Error");
                    return;
                }

                // Reset stored stats
                svc.ResetAll();

                // Clear thumbnail cache
                try
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var dir = System.IO.Path.Combine(appData, "PercysLibrary", "Thumbs");
                    if (Directory.Exists(dir))
                    {
                        foreach (var f in Directory.GetFiles(dir))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                }
                catch { }

                // Reset settings to defaults and persist immediately
                try
                {
                    ComicReader.Services.SettingsManager.ResetToDefaults();
                    ComicReader.Services.SettingsManager.SaveNow();
                }
                catch { }

                // Refresh VM/UI
                try { if (this.DataContext is ReadingStatsViewModel vm) vm.Refresh(); } catch { }

                ComicReader.Services.Notifications.NotificationService.Instance.Success("Reinicio completo. La aplicación reflejará los valores por defecto", "Reset exitoso");
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(ex, "Resetear estadísticas", ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}


