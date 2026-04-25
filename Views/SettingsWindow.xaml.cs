using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ComicReader.ViewModels;
using ComicReader.Themes;
using ComicReader.Services;
using Notifications = ComicReader.Services.Notifications;
using ErrorHandling = ComicReader.Services.ErrorHandling;

namespace ComicReader.Views
{
    /// <summary>
    /// Configuración (Phase 4): tres pestañas — Lectura, Apariencia, Biblioteca.
    /// La sidebar de 8 categorías y los paneles de "Personalización avanzada"
    /// fueron eliminados; las opciones legacy persisten en AppSettings pero
    /// dejaron de exponerse.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _vm;
        private ThemeMode _originalTheme;
        private bool _themeChanged = false;
        private bool _isInitializing = true;

        public SettingsWindow()
        {
            InitializeComponent();

            try
            {
                _originalTheme = ThemeManager.CurrentTheme;
            }
            catch
            {
                _originalTheme = ThemeMode.Dark;
            }

            _vm = this.Resources["SettingsVM"] as SettingsViewModel ?? this.DataContext as SettingsViewModel;
            if (_vm == null)
            {
                _vm = new SettingsViewModel();
                try { this.Resources["SettingsVM"] = _vm; } catch { }
                this.DataContext = _vm;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                try { InitializeComboBoxValues(); } catch { }
                _isInitializing = false;
            }));
        }

        // ============================================================
        // Cambio de pestaña
        // ============================================================
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Primitives.ToggleButton clicked) return;

                // Forzamos comportamiento radio: el click siempre marca la pestaña activa
                // y desmarca las otras dos, incluso si el usuario clickea la ya activa.
                clicked.IsChecked = true;
                if (this.FindName("TabLectura") is System.Windows.Controls.Primitives.ToggleButton tl && tl != clicked) tl.IsChecked = false;
                if (this.FindName("TabApariencia") is System.Windows.Controls.Primitives.ToggleButton ta && ta != clicked) ta.IsChecked = false;
                if (this.FindName("TabBiblioteca") is System.Windows.Controls.Primitives.ToggleButton tb && tb != clicked) tb.IsChecked = false;

                void SetVis(string name, bool visible)
                {
                    if (this.FindName(name) is FrameworkElement el)
                        el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }

                string key = clicked.Name;
                SetVis("Panel_Lectura", key == "TabLectura");
                SetVis("Panel_Apariencia", key == "TabApariencia");
                SetVis("Panel_Biblioteca", key == "TabBiblioteca");
            }
            catch { }
        }

        // ============================================================
        // Sincronizar combo boxes con el AppSettings al cargar
        // ============================================================
        private void InitializeComboBoxValues()
        {
            try
            {
                var settings = SettingsManager.Settings;
                if (settings == null) return;

                if (this.FindName("ReadingDirectionCombo") is ComboBox dirCombo)
                {
                    var dirTag = settings.CurrentReadingDirection.ToString();
                    foreach (ComboBoxItem item in dirCombo.Items)
                        if (item.Tag?.ToString() == dirTag) { dirCombo.SelectedItem = item; break; }
                }

                if (this.FindName("ReadingModeCombo") is ComboBox modeCombo)
                {
                    // Default a "Continuous" si EnableContinuousScroll esta activo, si no
                    // respeta el valor de ReadingMode legacy.
                    var preferred = settings.EnableContinuousScroll ? "Continuous" : (settings.ReadingMode ?? "PageByPage");
                    foreach (ComboBoxItem item in modeCombo.Items)
                        if (item.Tag?.ToString() == preferred) { modeCombo.SelectedItem = item; break; }
                }

                if (this.FindName("PageTurnAnimationCombo") is ComboBox animCombo)
                {
                    foreach (ComboBoxItem item in animCombo.Items)
                        if (item.Tag?.ToString() == settings.PageTurnAnimation) { animCombo.SelectedItem = item; break; }
                }

                if (this.FindName("UIScaleCombo") is ComboBox scaleCombo)
                {
                    foreach (ComboBoxItem item in scaleCombo.Items)
                        if (item.Tag?.ToString() == settings.UIScale) { scaleCombo.SelectedItem = item; break; }
                }
            }
            catch { }
        }

        // ============================================================
        // Acciones del header (Cancelar / Aplicar)
        // ============================================================
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_themeChanged)
                {
                    ThemeManager.ApplyTheme(_originalTheme);
                }
                this.Close();
            }
            catch { this.Close(); }
        }

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = this.DataContext as SettingsViewModel;

                if (vm?.SelectedThemeInfo != null)
                {
                    var themeMode = vm.SelectedThemeInfo.Mode;
                    var themeName = themeMode.ToString();

                    try
                    {
                        await PersistenceIntegrator.Instance.ChangeThemeAsync(themeName);
                        SettingsManager.Settings.Theme = themeName;
                        SettingsManager.SaveNow();

                        try
                        {
                            this.InvalidateVisual();
                            this.UpdateLayout();
                            foreach (Window window in Application.Current.Windows)
                            {
                                try { window.InvalidateVisual(); window.UpdateLayout(); } catch { }
                            }
                        }
                        catch { }

                        _originalTheme = themeMode;
                        _themeChanged = false;

                        Notifications.NotificationService.Instance?.Success(
                            $"Tema '{vm.SelectedThemeInfo.Name}' aplicado",
                            "Se mantendrá al reiniciar.");
                    }
                    catch (Exception ex)
                    {
                        ThemeManager.ApplyTheme(themeMode);
                        _originalTheme = themeMode;
                        _themeChanged = false;
                        Notifications.NotificationService.Instance?.Warning(
                            "Tema aplicado pero error al guardar",
                            ex.Message);
                    }
                }
                else
                {
                    SettingsManager.SaveNow();
                    Notifications.NotificationService.Instance?.Success(
                        "Configuración guardada",
                        "Los cambios se han aplicado correctamente.");
                }

                try
                {
                    AnimationService.RefreshAnimationState();
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.Dispatcher.Invoke(() => mainWindow.ApplySettingsRuntime());
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                ErrorHandling.ErrorHandler.Instance?.HandleException(
                    ex, "Aplicar configuración",
                    ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        // ============================================================
        // Aplicar tema en vivo cuando cambia la selección
        // ============================================================
        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing) return;
                var combo = sender as ComboBox;
                if (combo?.SelectedItem is ThemeInfo selectedTheme)
                {
                    ThemeManager.ApplyTheme(selectedTheme.Mode);
                    _themeChanged = true;
                }
            }
            catch { }
        }

        // ============================================================
        // Export / Import
        // ============================================================
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = this.DataContext as SettingsViewModel;
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON settings|*.json", FileName = "percys-settings.json", Title = "Exportar ajustes" };
                if (dlg.ShowDialog(this) == true)
                {
                    vm?.ExportTo(dlg.FileName);
                    Notifications.NotificationService.Instance?.Success("Ajustes exportados", "Exportación completada");
                }
            }
            catch (Exception ex)
            {
                ErrorHandling.ErrorHandler.Instance?.HandleException(ex, "Exportar ajustes", ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = this.DataContext as SettingsViewModel;
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON settings|*.json", Title = "Importar ajustes" };
                if (dlg.ShowDialog(this) == true)
                {
                    vm?.ImportFrom(dlg.FileName);
                    Notifications.NotificationService.Instance?.Success("Ajustes importados", "Importación completada");
                }
            }
            catch (Exception ex)
            {
                ErrorHandling.ErrorHandler.Instance?.HandleException(ex, "Importar ajustes", ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        // ============================================================
        // Combos de configuración
        // ============================================================
        private void ReadingDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing) return;
                if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                {
                    if (Enum.TryParse<ReadingDirection>(tag, out var direction))
                    {
                        SettingsManager.Settings.CurrentReadingDirection = direction;
                        SettingsManager.SaveSettings();
                    }
                }
            }
            catch { }
        }

        private void ReadingModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing) return;
                if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is string mode)
                {
                    SettingsManager.Settings.ReadingMode = mode;
                    SettingsManager.Settings.EnableContinuousScroll = (mode == "Continuous");
                    SettingsManager.SaveSettings();
                }
            }
            catch { }
        }

        private void PageTurnAnimationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing) return;
                if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is string animation)
                {
                    SettingsManager.Settings.PageTurnAnimation = animation;
                    SettingsManager.SaveSettings();
                }
            }
            catch { }
        }

        private void UIScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing) return;
                if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is string scale)
                {
                    SettingsManager.Settings.UIScale = scale;
                    SettingsManager.SaveSettings();
                }
            }
            catch { }
        }
    }
}
