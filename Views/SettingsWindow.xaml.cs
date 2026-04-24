using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ComicReader.ViewModels;
using ComicReader.Themes;
using ComicReader.Services;

namespace ComicReader.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _vm;
        private ThemeMode _originalTheme;
        private bool _themeChanged = false;
        private bool _isInitializing = true;

        public SettingsWindow()
        {
            InitializeComponent();
            
            // Guardar tema original para poder revertirlo si no se aplica
            try
            {
                _originalTheme = ThemeManager.CurrentTheme;
                ComicReader.Utils.DevLogger.Info($"Tema actual al abrir configuración: {_originalTheme}");
                ComicReader.Utils.DevLogger.Info($"Tema guardado en Settings: {SettingsManager.Settings.Theme}");
            }
            catch
            {
                _originalTheme = ThemeMode.Comic;
            }
                // Try to obtain the VM from resources or DataContext; fallback to a new instance so UI code is safe
                _vm = this.Resources["SettingsVM"] as SettingsViewModel ?? this.DataContext as SettingsViewModel;
                if (_vm == null)
                {
                    _vm = new SettingsViewModel();
                    // register in resources so XAML bindings that reference StaticResource will still work
                    try { this.Resources["SettingsVM"] = _vm; } catch { }
                    this.DataContext = _vm;
                }

                // after load, sync visibility with SelectedSection (defensive: guard nulls)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    try { UpdateSectionVisibility(_vm?.SelectedSection); } catch { }
                    try { InitializeComboBoxValues(); } catch { }
                    
                    // Desactivar flag de inicialización para permitir cambios de tema
                    _isInitializing = false;
                    
                    if (_vm != null)
                    {
                        try
                        {
                            _vm.PropertyChanged += (s, e) =>
                            {
                                try
                                {
                                    if (e.PropertyName == nameof(SettingsViewModel.SelectedSection))
                                        UpdateSectionVisibility(_vm.SelectedSection);
                                }
                                catch { }
                            };
                        }
                        catch { }
                    }
                }));
        }

        private void UpdateSectionVisibility(string section)
        {
            try
            {
                // Use FindName to avoid relying on generated fields which may be out-of-sync in some build states
                void SetVis(string name, string key)
                {
                    try
                    {
                        var el = this.FindName(name) as FrameworkElement;
                        if (el != null)
                            el.Visibility = (section == key) ? Visibility.Visible : Visibility.Collapsed;
                    }
                    catch { }
                }

                SetVis("Panel_General", "General");
                SetVis("Panel_Apariencia", "Apariencia");
                SetVis("Panel_Lectura", "Lectura");
                SetVis("Panel_Controles", "Controles");
                SetVis("Panel_Rendimiento", "Rendimiento");
                SetVis("Panel_Seguridad", "Seguridad");
                SetVis("Panel_Personalizacion", "Personalizacion");
                SetVis("Panel_Acerca", "Acerca");
            }
            catch { /* safe-ignore UI update errors */ }
        }

        private void InitializeComboBoxValues()
        {
            try
            {
                var settings = ComicReader.Services.SettingsManager.Settings;
                
                // ReadingDirection
                var dirCombo = this.FindName("ReadingDirectionCombo") as ComboBox;
                if (dirCombo != null)
                {
                    var dirTag = settings.CurrentReadingDirection.ToString();
                    foreach (ComboBoxItem item in dirCombo.Items)
                    {
                        if (item.Tag?.ToString() == dirTag)
                        {
                            dirCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // ReadingMode
                var modeCombo = this.FindName("ReadingModeCombo") as ComboBox;
                if (modeCombo != null)
                {
                    foreach (ComboBoxItem item in modeCombo.Items)
                    {
                        if (item.Tag?.ToString() == settings.ReadingMode)
                        {
                            modeCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // PageTurnAnimation
                var animCombo = this.FindName("PageTurnAnimationCombo") as ComboBox;
                if (animCombo != null)
                {
                    foreach (ComboBoxItem item in animCombo.Items)
                    {
                        if (item.Tag?.ToString() == settings.PageTurnAnimation)
                        {
                            animCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // UIScale
                var scaleCombo = this.FindName("UIScaleCombo") as ComboBox;
                if (scaleCombo != null)
                {
                    foreach (ComboBoxItem item in scaleCombo.Items)
                    {
                        if (item.Tag?.ToString() == settings.UIScale)
                        {
                            scaleCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Si el tema cambió pero no se aplicó, revertir al original
                if (_themeChanged)
                {
                    ComicReader.Utils.ModernLogger.Info($"⚠ Tema cambiado pero no aplicado, revirtiendo a: {_originalTheme}");
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
                    
                    ComicReader.Utils.ModernLogger.Info($"═══════════════════════════════════");
                    ComicReader.Utils.ModernLogger.Info($"  💾 GUARDANDO Y APLICANDO TEMA (v3.0): {vm.SelectedThemeInfo.Name}");
                    ComicReader.Utils.ModernLogger.Info($"═══════════════════════════════════");
                    
                    // PASO 1: GUARDAR Y APLICAR TEMA CON SISTEMA v3.0
                    try
                    {
                        await ComicReader.Services.PersistenceIntegrator.Instance.ChangeThemeAsync(themeName);
                        ComicReader.Utils.ModernLogger.Info($"✓ Tema guardado con v3.0: {themeName}");
                        ComicReader.Utils.ModernLogger.Info($"✓ Tema aplicado visualmente: {themeMode}");
                        
                        // PASO 2: SINCRONIZAR CON SETTINGSMANAGER PARA COHERENCIA (temporal)
                        SettingsManager.Settings.Theme = themeName;
                        SettingsManager.SaveNow();
                        ComicReader.Utils.ModernLogger.Info($"✓ Sincronizado con SettingsManager (legacy)");
                        
                        // PASO 4: FORZAR ACTUALIZACIÓN VISUAL DE TODAS LAS VENTANAS
                        try
                        {
                            ComicReader.Utils.DevLogger.Info("→ Actualizando visuales...");
                            
                            // Actualizar esta ventana
                            this.InvalidateVisual();
                            this.UpdateLayout();
                            
                            // Actualizar ventana principal
                            var mainWindow = Application.Current.MainWindow;
                            if (mainWindow != null && mainWindow != this)
                            {
                                mainWindow.InvalidateVisual();
                                mainWindow.UpdateLayout();
                            }
                            
                            // Actualizar todas las ventanas abiertas
                            foreach (Window window in Application.Current.Windows)
                            {
                                try
                                {
                                    window.InvalidateVisual();
                                    window.UpdateLayout();
                                }
                                catch { }
                            }
                            
                            ComicReader.Utils.DevLogger.Info("✓ Actualización visual completada");
                        }
                        catch (Exception updateEx)
                        {
                            ComicReader.Utils.DevLogger.Error($"Error en actualización visual: {updateEx.Message}");
                        }
                        
                        // PASO 5: ACTUALIZAR ESTADO INTERNO
                        _originalTheme = themeMode;
                        _themeChanged = false;
                        
                        ComicReader.Utils.ModernLogger.Info($"═══════════════════════════════════");
                        ComicReader.Utils.ModernLogger.Info($"   ✓✓✓ TEMA GUARDADO Y APLICADO");
                        ComicReader.Utils.ModernLogger.Info($"   → Persistirá al reiniciar");
                        ComicReader.Utils.ModernLogger.Info($"═══════════════════════════════════");
                        
                        ComicReader.Services.Notifications.NotificationService.Instance?.Success(
                            $"Tema '{vm.SelectedThemeInfo.Name}' aplicado", 
                            "Se mantendrá al reiniciar la aplicación");
                    }
                    catch (Exception ex)
                    {
                        ComicReader.Utils.DevLogger.Error($"═══════════════════════════════════");
                        ComicReader.Utils.DevLogger.Error($"   ✗✗✗ ERROR GUARDANDO TEMA: {ex.Message}");
                        ComicReader.Utils.DevLogger.Error($"═══════════════════════════════════");
                        
                        // Aplicar de todas formas pero advertir
                        ThemeManager.ApplyTheme(themeMode);
                        _originalTheme = themeMode;
                        _themeChanged = false;
                        
                        ComicReader.Services.Notifications.NotificationService.Instance?.Warning(
                            "Tema aplicado pero error al guardar",
                            $"Error: {ex.Message}");
                    }
                }
                else
                {
                    // Si no hay tema seleccionado, guardar otros cambios
                    SettingsManager.SaveNow();
                    ComicReader.Services.Notifications.NotificationService.Instance?.Success(
                        "Configuración guardada", 
                        "Los cambios se han aplicado correctamente");
                }
                
                // PASO 6: APLICAR OTROS CAMBIOS EN CALIENTE
                try
                {
                    // Refrescar estado de animaciones
                    ComicReader.Services.AnimationService.RefreshAnimationState();
                    
                    // Aplicar configuración runtime en MainWindow
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    if (mainWindow != null)
                    {
                        mainWindow.Dispatcher.Invoke(() => mainWindow.ApplySettingsRuntime());
                        ComicReader.Utils.ModernLogger.Info("✓ Configuración aplicada en tiempo real");
                    }
                }
                catch (Exception applyEx)
                {
                    ComicReader.Utils.ModernLogger.Error($"Error aplicando configuración en caliente: {applyEx.Message}");
                }
            }
            catch (Exception ex)
            {
                ComicReader.Utils.DevLogger.Error($"✗✗✗ ERROR CRÍTICO al aplicar configuración: {ex.Message}");
                ComicReader.Utils.DevLogger.Error($"Stack trace: {ex.StackTrace}");
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance?.HandleException(
                    ex, 
                    "Aplicar configuración", 
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // No cambiar el tema durante la inicialización
                if (_isInitializing) return;
                
                var combo = sender as ComboBox;
                var selectedTheme = combo?.SelectedItem as ThemeInfo;
                
                if (selectedTheme != null)
                {
                    // Aplicar vista previa en tiempo real (sin guardar)
                    ThemeManager.ApplyTheme(selectedTheme.Mode);
                    _themeChanged = true;
                }
            }
            catch { }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = this.DataContext as SettingsViewModel;
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON settings|*.json", FileName = "percys-settings.json", Title = "Exportar ajustes" };
                if (dlg.ShowDialog(this) == true)
                {
                    vm?.ExportTo(dlg.FileName);
                    ComicReader.Services.Notifications.NotificationService.Instance.Success("Ajustes exportados correctamente", "Exportación completada");
                }
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(ex, "Exportar ajustes", ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
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
                    ComicReader.Services.Notifications.NotificationService.Instance.Success("Ajustes importados y aplicados", "Importación completada");
                }
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(ex, "Importar ajustes", ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = this.DataContext as SettingsViewModel;
                vm?.PreviewCommand?.Execute(null);
            }
            catch { }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var vm = this.DataContext as SettingsViewModel;
                vm?.FilterSections(((TextBox)sender).Text);
            }
            catch { }
        }

        private void ReadingDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var combo = sender as ComboBox;
                var item = combo?.SelectedItem as ComboBoxItem;
                if (item?.Tag is string tag)
                {
                    if (Enum.TryParse<ComicReader.Services.ReadingDirection>(tag, out var direction))
                    {
                        ComicReader.Services.SettingsManager.Settings.CurrentReadingDirection = direction;
                        ComicReader.Services.SettingsManager.SaveSettings();
                    }
                }
            }
            catch { }
        }

        private void ReadingModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var combo = sender as ComboBox;
                var item = combo?.SelectedItem as ComboBoxItem;
                if (item?.Tag is string mode)
                {
                    ComicReader.Services.SettingsManager.Settings.ReadingMode = mode;
                    ComicReader.Services.SettingsManager.SaveSettings();
                }
            }
            catch { }
        }

        private void PageTurnAnimationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var combo = sender as ComboBox;
                var item = combo?.SelectedItem as ComboBoxItem;
                if (item?.Tag is string animation)
                {
                    ComicReader.Services.SettingsManager.Settings.PageTurnAnimation = animation;
                    ComicReader.Services.SettingsManager.SaveSettings();
                }
            }
            catch { }
        }

        private void UIScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var combo = sender as ComboBox;
                var item = combo?.SelectedItem as ComboBoxItem;
                if (item?.Tag is string scale)
                {
                    ComicReader.Services.SettingsManager.Settings.UIScale = scale;
                    ComicReader.Services.SettingsManager.SaveSettings();
                }
            }
            catch { }
        }
    }
}