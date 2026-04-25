using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ComicReader.Models;
using ComicReader.Services;
using ComicReader.Themes;

namespace ComicReader.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    // Expose the app settings (requires SettingsManager in your project)
    // Use the existing AppSettings type declared in ComicReader.Services
    public AppSettings AppSettings => SettingsManager.Settings;

        // Sections
        public ObservableCollection<string> Sections { get; } = new ObservableCollection<string>(new[] {
            "General", "Apariencia", "Lectura", "Controles", "Rendimiento", "Seguridad", "Personalizacion", "Acerca"
        });

        private string _selectedSection = "General";
        public string SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (_selectedSection == value) return;
                _selectedSection = value;
                Raise();
            }
        }

        // Theme & appearance lists
        public ObservableCollection<string> AvailableThemes { get; } = new ObservableCollection<string>(new[] { "Comic Clásico", "Comic Moderno", "Manga", "Vintage", "Nocturno" });
        public ObservableCollection<string> AccentColors { get; } = new ObservableCollection<string>(new[] { "Rojo", "Azul", "Amarillo", "Verde", "Naranja" });
        public ObservableCollection<string> AvailableFonts { get; } = new ObservableCollection<string>(new[] { "Komika Axis", "Bangers", "Comic Sans MS", "Segoe UI" });

        // Commands (many)
        public ICommand SaveCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand ClearCacheCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand ExportCommandWithDialog { get; }
        public ICommand ImportCommandWithDialog { get; }
        public ICommand EditBackgroundCommand { get; }
        public ICommand RestartUICommand { get; }
        public ICommand SelectSectionCommand { get; }
        public ICommand PreviewThemeCommand { get; }
        public ICommand ChangeFolderCommand { get; }
        public ICommand ViewLogsCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand OptimizeCommand { get; }
        public ICommand ConfigureShortcutsCommand { get; }
        public ICommand ViewCreditsCommand { get; }
        public ICommand OpenWebsiteCommand { get; }
        public ICommand ApplyAccentCommand { get; }
        public ICommand PreviewFontCommand { get; }
        public ICommand ResetAdvancedAppearanceCommand { get; }

        // Small fallback Relay in case project lacks a RelayCommand
        private class SimpleRelay : ICommand
        {
            private readonly Action<object> _act;
            private readonly Func<object, bool> _can;
            public SimpleRelay(Action<object> act, Func<object, bool> can = null) { _act = act; _can = can; }
            public event EventHandler CanExecuteChanged;
            public bool CanExecute(object parameter) => _can?.Invoke(parameter) ?? true;
            public void Execute(object parameter) => _act?.Invoke(parameter);
            public void RaiseCanExecute() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public SettingsViewModel()
        {
            // El selector publico de temas se reduce a las dos opciones que el
            // sistema de diseno nuevo soporta: Oscuro (default) y Claro. El
            // resto de modos definidos en ThemeManager queda disponible
            // internamente pero no se expone en la UI.
            try
            {
                var avail = ThemeManager.GetAvailableThemes();
                var allowed = new[] { ThemeMode.Dark, ThemeMode.Light };
                foreach (var t in avail.Where(x => allowed.Contains(x.Mode)))
                {
                    if (!ThemeInfos.Any(x => x.Mode == t.Mode)) ThemeInfos.Add(t);
                }
            }
            catch { }

            // Build commands (prefer existing RelayCommand if present)
            Func<Action, ICommand> mk = (act) =>
            {
                try
                {
                    var rcType = Type.GetType("ComicReader.Commands.RelayCommand, ComicReader");
                    if (rcType != null)
                    {
                        return (ICommand)Activator.CreateInstance(rcType, (Action)act);
                    }
                }
                catch { }
                return new SimpleRelay(_ => act());
            };

            SaveCommand = mk(() =>
            {
                try { SettingsManager.SaveNow(); MessageBox.Show("Ajustes guardados.", "Guardar", MessageBoxButton.OK, MessageBoxImage.Information); } catch { MessageBox.Show("Error al guardar ajustes.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            });

            ApplyCommand = mk(() =>
            {
                try
                {
                    SettingsManager.SaveSettings();
                    try { ThemeManager.CurrentTheme = (ThemeMode)Enum.Parse(typeof(ThemeMode), SettingsManager.Settings.Theme); } catch { }
                    try { ThemeManager.ApplyAccent(SettingsManager.Settings.AccentColorName); } catch { }
                    try { if (!string.IsNullOrWhiteSpace(SettingsManager.Settings.ReaderFontName)) Application.Current.Resources["ReaderFontFamily"] = new System.Windows.Media.FontFamily(SettingsManager.Settings.ReaderFontName); } catch { }
                    try { Application.Current.Resources["UIScale"] = SettingsManager.Settings.UIScale; } catch { }
                    Raise(nameof(AppSettings));
                    MessageBox.Show("Cambios aplicados.", "Aplicar", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { MessageBox.Show("No se pudieron aplicar cambios.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            });

            RestoreDefaultsCommand = mk(() =>
            {
                try { SettingsManager.ResetToDefaults(); SettingsManager.SaveNow(); Raise(nameof(AppSettings)); MessageBox.Show("Ajustes restaurados.", "Restaurar", MessageBoxButton.OK, MessageBoxImage.Information); } catch { }
            });

            ClearCacheCommand = mk(() =>
            {
                try { DiskImageCache.CleanupOld(0); MessageBox.Show("Caché limpiado.", "Caché", MessageBoxButton.OK, MessageBoxImage.Information); } catch { MessageBox.Show("No se pudo limpiar caché.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            });

            PreviewCommand = mk(() =>
            {
                try { if (SelectedThemeInfo != null) ThemeManager.ApplyTheme(SelectedThemeInfo.Mode); } catch { }
            });

            ExportCommandWithDialog = new SimpleRelay(_ =>
            {
                try
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON settings|*.json", FileName = "percys-settings.json", Title = "Exportar ajustes" };
                    if (dlg.ShowDialog() == true) ExportTo(dlg.FileName);
                }
                catch { MessageBox.Show("Error exportando ajustes.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            });

            ImportCommandWithDialog = new SimpleRelay(_ =>
            {
                try
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON settings|*.json", Title = "Importar ajustes" };
                    if (dlg.ShowDialog() == true) ImportFrom(dlg.FileName);
                }
                catch { MessageBox.Show("Error importando ajustes.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            });

            EditBackgroundCommand = new SimpleRelay(_ =>
            {
                try
                {
                    var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp" };
                    if (ofd.ShowDialog() == true)
                    {
                        SettingsManager.Settings.ReaderBackgroundCustomPath = ofd.FileName;
                        SettingsManager.SaveSettings();
                        Raise(nameof(AppSettings));
                    }
                }
                catch { }
            });

            RestartUICommand = mk(() => { try { ThemeManager.ApplyTheme(ThemeManager.CurrentTheme); MessageBox.Show("Interfaz reiniciada.", "Reiniciar", MessageBoxButton.OK, MessageBoxImage.Information); } catch { } });

            SelectSectionCommand = new SimpleRelay(p => { try { if (p is string s) SelectedSection = s; } catch { } });

            PreviewThemeCommand = mk(() => { try { if (SelectedThemeInfo != null) ThemeManager.ApplyTheme(SelectedThemeInfo.Mode); } catch { } });

            ChangeFolderCommand = new SimpleRelay(_ =>
            {
                try
                {
                    var dlg = new System.Windows.Forms.FolderBrowserDialog();
                    var result = dlg.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.OK)
                    {
                        SettingsManager.Settings.DefaultComicsFolder = dlg.SelectedPath;
                        SettingsManager.SaveSettings();
                        Raise(nameof(AppSettings));
                    }
                }
                catch { }
            });

            ViewLogsCommand = mk(() =>
            {
                try
                {
                    var path = SettingsManager.GetLogsFolderPath();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo() { FileName = path, UseShellExecute = true });
                }
                catch { MessageBox.Show("No se pudieron abrir los logs.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            });

            OpenFolderCommand = mk(() =>
            {
                try
                {
                    var dir = AppDomain.CurrentDomain.BaseDirectory;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo() { FileName = dir, UseShellExecute = true });
                }
                catch { }
            });

            OptimizeCommand = mk(() =>
            {
                try { DiskImageCache.CleanupOld(50); MessageBox.Show("Optimización completada.", "Optimizar", MessageBoxButton.OK, MessageBoxImage.Information); } catch { MessageBox.Show("No se pudo optimizar.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            });

            ConfigureShortcutsCommand = mk(() => {
                try
                {
                    var win = new ComicReader.Views.ShortcutsWindow();
                    var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
                    if (owner != null) win.Owner = owner;
                    win.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("No se pudo abrir el editor de atajos: " + ex.Message, "Atajos", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            ViewCreditsCommand = mk(() => { MessageBox.Show("Créditos: Equipo Percy - Diseño UI/UX.", "Créditos", MessageBoxButton.OK, MessageBoxImage.Information); });
            OpenWebsiteCommand = mk(() => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo() { FileName = "https://example.com", UseShellExecute = true }); } catch { } });

            ApplyAccentCommand = mk(() =>
            {
                try
                {
                    // example: store accent name and save
                    SettingsManager.SaveSettings();
                    Raise(nameof(AppSettings));
                }
                catch { }
            });

            PreviewFontCommand = mk(() =>
            {
                try { MessageBox.Show("Vista previa de fuente (implementación simple).", "Fuente", MessageBoxButton.OK, MessageBoxImage.Information); } catch { }
            });

            ResetAdvancedAppearanceCommand = mk(() =>
            {
                try
                {
                    SettingsManager.Settings.VignetteBorderThickness = 4;
                    SettingsManager.Settings.EnableComicEffects = true;
                    SettingsManager.SaveSettings();
                    Raise(nameof(AppSettings));
                    MessageBox.Show("Apariencia avanzada restablecida.", "Restablecer", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { }
            });

            // ThemeInfos collection (filtrado a Oscuro/Claro)
            ThemeInfos = new ObservableCollection<ThemeInfo>();
            try
            {
                var themes = ThemeManager.GetAvailableThemes();
                var allowed = new[] { ThemeMode.Dark, ThemeMode.Light };
                foreach (var t in themes.Where(x => allowed.Contains(x.Mode))) ThemeInfos.Add(t);
                // Set initial SelectedThemeInfo based on persisted setting
                try
                {
                    var cur = SettingsManager.Settings.Theme;
                    ThemeInfo match = null;
                    if (!string.IsNullOrWhiteSpace(cur) && Enum.TryParse<ThemeMode>(cur, out var curMode))
                        match = ThemeInfos.FirstOrDefault(x => x.Mode == curMode);
                    // Fallback: cualquier tema legacy no expuesto cae a Oscuro.
                    if (match == null) match = ThemeInfos.FirstOrDefault(x => x.Mode == ThemeMode.Dark) ?? ThemeInfos.FirstOrDefault();
                    if (match != null) _selectedThemeInfo = match; // set backing field to avoid double-save
                }
                catch { }
                // HomeView-specific theme option removed — no per-screen default selection
            }
            catch { }

            // initial selection
            SelectedSection = Sections.FirstOrDefault();
        }

        private ThemeInfo _selectedThemeInfo;
        public ThemeInfo SelectedThemeInfo
        {
            get => _selectedThemeInfo;
            set
            {
                if (_selectedThemeInfo == value) return;
                _selectedThemeInfo = value;
                // NO guardamos automáticamente, solo notificamos el cambio
                // La vista previa se maneja en el code-behind de SettingsWindow
                Raise();
            }
        }

        // Per-screen HomeView theme removed; use global theme only

        // Theme info collection
        public ObservableCollection<ThemeInfo> ThemeInfos { get; }

        // Simple filter helper used by SearchBox
        public void FilterSections(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            var found = Sections.FirstOrDefault(s => s.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            if (found != null) SelectedSection = found;
        }

        // Export/Import implementation
        public void ExportTo(string path)
        {
            try
            {
                var json = JsonSerializer.Serialize(SettingsManager.Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { throw; }
        }

        public void ImportFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var txt = File.ReadAllText(path);
                AppSettings obj = null;
                try { obj = JsonSerializer.Deserialize<AppSettings>(txt); } catch { obj = null; }
                if (obj == null) return;

                try
                {
                    var cur = SettingsManager.GetSettingsFilePath();
                    if (!string.IsNullOrWhiteSpace(cur) && File.Exists(cur))
                    {
                        var bak = cur + ".bak." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                        File.Copy(cur, bak, true);
                    }
                }
                catch { }

                SettingsManager.ReplaceSettings(obj);
                SettingsManager.SaveNow();
                Raise(nameof(AppSettings));
                MessageBox.Show("Ajustes importados y aplicados.", "Importar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { throw; }
        }
    }
}
