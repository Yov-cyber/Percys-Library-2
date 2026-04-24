using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ComicReader.Services;

namespace ComicReader.Views
{
    public partial class ShortcutsWindow : Window
    {
        private ObservableCollection<ShortcutEntry> _shortcuts;
        private ShortcutEntry _recordingTarget;

        public ObservableCollection<ShortcutEntry> Shortcuts => _shortcuts;

        public ShortcutsWindow()
        {
            InitializeComponent();
            // Load known actions and current gestures
            var defaults = GetDefaultShortcuts();
            var saved = ShortcutsService.Load();

            _shortcuts = new ObservableCollection<ShortcutEntry>(defaults.Select(d => new ShortcutEntry
            {
                Id = d.Key,
                Description = d.Value,
                Gesture = saved.ContainsKey(d.Key) ? saved[d.Key] : string.Empty,
                Default = string.Empty
            }));

            // Fill Default values (if available)
            foreach (var s in _shortcuts)
            {
                if (defaults.TryGetValue(s.Id, out var desc)) { /* desc already used */ }
            }

            this.DataContext = this;
            this.KeyDown += ShortcutsWindow_KeyDown;
        }

        private void ShortcutsWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (_recordingTarget == null) return;
            if (e.Key == Key.Escape)
            {
                _recordingTarget.Gesture = _recordingTarget.Gesture; // no-op
                _recordingTarget = null;
                this.Title = "Editor de atajos";
                return;
            }

            // Build gesture string
            var parts = new System.Collections.Generic.List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            // Ignore modifier-only
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift)
            {
                return;
            }
            parts.Add(key.ToString());
            var gesture = string.Join("+", parts);
            _recordingTarget.Gesture = gesture;
            _recordingTarget = null;
            this.Title = "Editor de atajos";
            e.Handled = true;
        }

        private System.Collections.Generic.Dictionary<string, string> GetDefaultShortcuts()
        {
            // Minimal set of actions - extend as needed
            return new System.Collections.Generic.Dictionary<string, string>
            {
                ["OpenFile"] = "Abrir archivo",
                ["EnterReadingMode"] = "Entrar en modo lectura",
                ["NextPage"] = "Página siguiente",
                ["PrevPage"] = "Página anterior",
                ["ToggleThumbnails"] = "Mostrar/ocultar miniaturas",
                ["ToggleNightMode"] = "Alternar modo noche"
            };
        }

        private void Record_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.Button btn) || !(btn.Tag is ShortcutEntry entry)) return;
            _recordingTarget = entry;
            this.Title = "Editor de atajos — presiona la combinación... (Esc para cancelar)";
            // Ensure window focused to capture keys
            this.Activate();
            Keyboard.Focus(this);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.Button btn) || !(btn.Tag is ShortcutEntry entry)) return;
            entry.Gesture = string.Empty;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dict = _shortcuts.ToDictionary(s => s.Id, s => s.Gesture ?? string.Empty);
                ShortcutsService.Save(dict);
                ComicReader.Services.Notifications.NotificationService.Instance.Success("Atajos guardados", "Configuración guardada");
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex) 
            { 
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(ex, "Guardar atajos", ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify); 
            }
        }

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            var defs = GetDefaultShortcuts();
            foreach (var s in _shortcuts)
            {
                s.Gesture = defs.ContainsKey(s.Id) ? string.Empty : string.Empty;
            }
            ShortcutsService.Save(new System.Collections.Generic.Dictionary<string, string>());
            ComicReader.Services.Notifications.NotificationService.Instance.Success("Atajos restablecidos", "Configuración restaurada");
        }
    }

    public class ShortcutEntry : System.ComponentModel.INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Description { get; set; }
        private string _gesture;
        public string Gesture { get => _gesture; set { _gesture = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Gesture))); } }
        public string Default { get; set; }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }
}
