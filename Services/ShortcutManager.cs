using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using ComicReader.Utils;

namespace ComicReader.Services
{
    /// <summary>
    /// Gestor de atajos de teclado personalizables
    /// Permite al usuario definir sus propias combinaciones de teclas
    /// </summary>
    public sealed class ShortcutManager
    {
        private static readonly Lazy<ShortcutManager> _instance = 
            new Lazy<ShortcutManager>(() => new ShortcutManager());
        
        public static ShortcutManager Instance => _instance.Value;

        private readonly string _shortcutsFilePath;
        private Dictionary<string, ShortcutBinding> _shortcuts;
        private Dictionary<string, Action> _actions;

        private ShortcutManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "PercysLibrary");
            _shortcutsFilePath = Path.Combine(appFolder, "shortcuts.json");

            _shortcuts = new Dictionary<string, ShortcutBinding>();
            _actions = new Dictionary<string, Action>();

            LoadShortcuts();
            RegisterDefaultShortcuts();
            
            ModernLogger.Info("✓ ShortcutManager inicializado");
        }

        /// <summary>
        /// Registra una acción con su atajo predeterminado
        /// </summary>
        public void RegisterAction(string actionId, string actionName, Key defaultKey, 
            ModifierKeys defaultModifiers, Action action)
        {
            _actions[actionId] = action;

            if (!_shortcuts.ContainsKey(actionId))
            {
                _shortcuts[actionId] = new ShortcutBinding
                {
                    ActionId = actionId,
                    ActionName = actionName,
                    Key = defaultKey,
                    Modifiers = defaultModifiers,
                    IsEnabled = true
                };
            }

            ModernLogger.Info($"✓ Acción registrada: {actionName} ({defaultKey})");
        }

        /// <summary>
        /// Procesa una tecla presionada
        /// </summary>
        public bool ProcessKeyPress(Key key, ModifierKeys modifiers)
        {
            foreach (var shortcut in _shortcuts.Values)
            {
                if (!shortcut.IsEnabled) continue;

                if (shortcut.Key == key && shortcut.Modifiers == modifiers)
                {
                    if (_actions.TryGetValue(shortcut.ActionId, out var action))
                    {
                        try
                        {
                            action?.Invoke();
                            ModernLogger.Info($"⌨ Atajo ejecutado: {shortcut.ActionName}");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            ModernLogger.Error($"Error ejecutando atajo {shortcut.ActionName}: {ex.Message}");
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Cambia el atajo para una acción
        /// </summary>
        public bool SetShortcut(string actionId, Key newKey, ModifierKeys newModifiers)
        {
            if (!_shortcuts.ContainsKey(actionId))
            {
                ModernLogger.Warning($"Acción no encontrada: {actionId}");
                return false;
            }

            // Verificar si ya existe este atajo
            var existing = _shortcuts.Values.FirstOrDefault(s => 
                s.Key == newKey && s.Modifiers == newModifiers && s.ActionId != actionId);

            if (existing != null)
            {
                ModernLogger.Warning($"Atajo ya está en uso: {existing.ActionName}");
                return false;
            }

            _shortcuts[actionId].Key = newKey;
            _shortcuts[actionId].Modifiers = newModifiers;

            SaveShortcuts();
            ModernLogger.Info($"✓ Atajo actualizado: {_shortcuts[actionId].ActionName}");
            return true;
        }

        /// <summary>
        /// Habilita/deshabilita un atajo
        /// </summary>
        public void SetShortcutEnabled(string actionId, bool enabled)
        {
            if (_shortcuts.ContainsKey(actionId))
            {
                _shortcuts[actionId].IsEnabled = enabled;
                SaveShortcuts();
            }
        }

        /// <summary>
        /// Obtiene el atajo de una acción
        /// </summary>
        public ShortcutBinding GetShortcut(string actionId)
        {
            return _shortcuts.GetValueOrDefault(actionId);
        }

        /// <summary>
        /// Obtiene todos los atajos
        /// </summary>
        public IReadOnlyDictionary<string, ShortcutBinding> GetAllShortcuts()
        {
            return _shortcuts;
        }

        /// <summary>
        /// Restaura atajos predeterminados
        /// </summary>
        public void ResetToDefaults()
        {
            _shortcuts.Clear();
            RegisterDefaultShortcuts();
            SaveShortcuts();
            ModernLogger.Info("✓ Atajos restaurados a valores predeterminados");
        }

        /// <summary>
        /// Convierte un atajo a string legible
        /// </summary>
        public static string ShortcutToString(Key key, ModifierKeys modifiers)
        {
            var parts = new List<string>();

            if (modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");

            parts.Add(key.ToString());

            return string.Join(" + ", parts);
        }

        // ============================================================
        // ATAJOS PREDETERMINADOS
        // ============================================================

        private void RegisterDefaultShortcuts()
        {
            // Navegación
            RegisterDefaultShortcut("NextPage", "Página Siguiente", Key.Right, ModifierKeys.None);
            RegisterDefaultShortcut("PrevPage", "Página Anterior", Key.Left, ModifierKeys.None);
            RegisterDefaultShortcut("FirstPage", "Primera Página", Key.Home, ModifierKeys.None);
            RegisterDefaultShortcut("LastPage", "Última Página", Key.End, ModifierKeys.None);
            RegisterDefaultShortcut("GoToPage", "Ir a Página", Key.G, ModifierKeys.Control);

            // Zoom
            RegisterDefaultShortcut("ZoomIn", "Aumentar Zoom", Key.Add, ModifierKeys.Control);
            RegisterDefaultShortcut("ZoomOut", "Reducir Zoom", Key.Subtract, ModifierKeys.Control);
            RegisterDefaultShortcut("ZoomFit", "Ajustar a Ventana", Key.D0, ModifierKeys.Control);
            RegisterDefaultShortcut("ZoomActual", "Zoom 100%", Key.D1, ModifierKeys.Control);

            // Vista
            RegisterDefaultShortcut("FullScreen", "Pantalla Completa", Key.F11, ModifierKeys.None);
            RegisterDefaultShortcut("Immersive", "Modo Inmersivo", Key.I, ModifierKeys.Control);
            RegisterDefaultShortcut("Thumbnails", "Panel Miniaturas", Key.T, ModifierKeys.Control);
            RegisterDefaultShortcut("Bookmarks", "Marcadores", Key.B, ModifierKeys.Control);

            // Archivo
            RegisterDefaultShortcut("OpenFile", "Abrir Archivo", Key.O, ModifierKeys.Control);
            RegisterDefaultShortcut("CloseFile", "Cerrar Archivo", Key.W, ModifierKeys.Control);
            RegisterDefaultShortcut("Settings", "Configuración", Key.OemComma, ModifierKeys.Control);
            RegisterDefaultShortcut("Exit", "Salir", Key.Q, ModifierKeys.Control);

            // Marcadores
            RegisterDefaultShortcut("AddBookmark", "Agregar Marcador", Key.D, ModifierKeys.Control);
            RegisterDefaultShortcut("NextBookmark", "Siguiente Marcador", Key.N, ModifierKeys.Control | ModifierKeys.Shift);
            RegisterDefaultShortcut("PrevBookmark", "Marcador Anterior", Key.P, ModifierKeys.Control | ModifierKeys.Shift);

            // Lectura
            RegisterDefaultShortcut("ToggleDirection", "Cambiar Dirección", Key.R, ModifierKeys.Control);
            RegisterDefaultShortcut("ToggleFitMode", "Cambiar Ajuste", Key.F, ModifierKeys.Control);
            RegisterDefaultShortcut("RotateLeft", "Rotar Izquierda", Key.L, ModifierKeys.Control | ModifierKeys.Shift);
            RegisterDefaultShortcut("RotateRight", "Rotar Derecha", Key.R, ModifierKeys.Control | ModifierKeys.Shift);

            // Extras
            RegisterDefaultShortcut("Help", "Ayuda", Key.F1, ModifierKeys.None);
            RegisterDefaultShortcut("Refresh", "Recargar", Key.F5, ModifierKeys.None);
        }

        private void RegisterDefaultShortcut(string actionId, string actionName, 
            Key defaultKey, ModifierKeys defaultModifiers)
        {
            if (!_shortcuts.ContainsKey(actionId))
            {
                _shortcuts[actionId] = new ShortcutBinding
                {
                    ActionId = actionId,
                    ActionName = actionName,
                    Key = defaultKey,
                    Modifiers = defaultModifiers,
                    IsEnabled = true
                };
            }
        }

        // ============================================================
        // PERSISTENCIA
        // ============================================================

        private void LoadShortcuts()
        {
            try
            {
                // ✅ v3.0: Cargar desde PersistenceIntegrator
                var config = PersistenceIntegrator.Instance.GetConfiguration();
                if (config?.Shortcuts != null && config.Shortcuts.Count > 0)
                {
                    _shortcuts = new Dictionary<string, ShortcutBinding>();
                    foreach (var kvp in config.Shortcuts)
                    {
                        try
                        {
                            var binding = new ShortcutBinding
                            {
                                ActionId = kvp.Key,
                                ActionName = kvp.Key,
                                Key = Enum.TryParse<Key>(kvp.Value.Key, out var k) ? k : Key.None,
                                Modifiers = Enum.TryParse<ModifierKeys>(kvp.Value.Modifiers, out var m) ? m : ModifierKeys.None,
                                IsEnabled = kvp.Value.IsEnabled
                            };
                            _shortcuts[kvp.Key] = binding;
                        }
                        catch { }
                    }
                    ModernLogger.Info($"✓ Atajos cargados desde v3.0: {_shortcuts.Count}");
                    return;
                }
                
                // Fallback: cargar desde archivo legacy si no hay en config.json
                if (File.Exists(_shortcutsFilePath))
                {
                    var json = File.ReadAllText(_shortcutsFilePath);
                    var shortcuts = JsonSerializer.Deserialize<List<ShortcutBinding>>(json);

                    if (shortcuts != null)
                    {
                        _shortcuts = shortcuts.ToDictionary(s => s.ActionId);
                        ModernLogger.Info($"✓ Atajos cargados (legacy fallback): {_shortcuts.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error cargando atajos: {ex.Message}");
            }
        }

        private void SaveShortcuts()
        {
            try
            {
                // ✅ v3.0: Guardar con PersistenceIntegrator
                foreach (var shortcut in _shortcuts)
                {
                    _ = PersistenceIntegrator.Instance.UpdateShortcutAsync(
                        shortcut.Value.ActionId,
                        shortcut.Value.Key.ToString(),
                        shortcut.Value.Modifiers.ToString()
                    );
                }
                ModernLogger.Info($"✓ Atajos guardados en v3.0: {_shortcuts.Count}");
                
                // Ya no necesitamos guardar en archivo legacy (será eliminado por ConfigurationManager)
                return;
                
                #pragma warning disable CS0162 // Código inalcanzable detectado
                var directory = Path.GetDirectoryName(_shortcutsFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var shortcuts = _shortcuts.Values.ToList();
                var json = JsonSerializer.Serialize(shortcuts, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_shortcutsFilePath, json);
                ModernLogger.Info($"✓ Atajos guardados (legacy): {shortcuts.Count}");
                
                // Nota: El archivo shortcuts.json será eliminado por ConfigurationManager en el próximo inicio
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error guardando atajos: {ex.Message}");
            }
        }

        // ============================================================
        // CLASE INTERNA
        // ============================================================

        public class ShortcutBinding
        {
            public string ActionId { get; set; }
            public string ActionName { get; set; }
            public Key Key { get; set; }
            public ModifierKeys Modifiers { get; set; }
            public bool IsEnabled { get; set; }

            public string KeyString => ShortcutToString(Key, Modifiers);
        }
    }
}
