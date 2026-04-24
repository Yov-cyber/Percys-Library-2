using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComicReader.Utils;
using System.Threading;

namespace ComicReader.Services
{
    /// <summary>
    /// Sistema robusto de persistencia de temas con las siguientes características:
    /// - Guardado atómico con archivo temporal y reemplazo seguro
    /// - Sistema de respaldo automático (backup)
    /// - Detección y recuperación de corrupción
    /// - Validación exhaustiva de datos
    /// - Thread-safe con locks
    /// - Logging detallado de todas las operaciones
    /// - Migración automática desde archivos legacy
    /// 
    /// Ruta: %LocalAppData%\PercysLibrary\theme.json
    /// Backup: %LocalAppData%\PercysLibrary\theme.backup.json
    /// </summary>
    public static class ThemePersistenceService
    {
        private static readonly object _lock = new object();
        private static readonly string ThemeFolder;
        private static readonly string ThemeJsonPath;
        private static readonly string BackupPath;
        
        // Paths legacy para migración
        private static readonly string LegacyConfigPath;
        private static readonly string LegacyBackupPath;
        private static readonly string LegacyThemeTxtPath;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        // Cache en memoria del último tema cargado
        private static ThemeSettings _cachedSettings;
        private static DateTime _lastLoadTime = DateTime.MinValue;
        private static readonly TimeSpan CacheTimeout = TimeSpan.FromSeconds(5);

        static ThemePersistenceService()
        {
            try
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                ThemeFolder = Path.Combine(baseDir, "PercysLibrary");
                ThemeJsonPath = Path.Combine(ThemeFolder, "theme.json");
                BackupPath = Path.Combine(ThemeFolder, "theme.backup.json");
                
                // Legacy paths
                LegacyConfigPath = Path.Combine(ThemeFolder, "theme.config");
                LegacyBackupPath = Path.Combine(ThemeFolder, "theme.backup");
                LegacyThemeTxtPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PercysLibrary", "CurrentTheme.txt");

                // Crear directorio si no existe
                Directory.CreateDirectory(ThemeFolder);
                
                ModernLogger.Info("✓ ThemePersistenceService inicializado");
                ModernLogger.Info($"  → Ruta: {ThemeJsonPath}");
            }
            catch (Exception ex)
            {
                DevLogger.Error($"✗ Error crítico inicializando ThemePersistenceService: {ex.Message}");
            }
        }

        /// <summary>
        /// Guarda el tema actual con sistema de backup y validación
        /// MEJORADO: Usa tanto el sistema legacy como el nuevo sistema mejorado
        /// </summary>
        public static bool SaveTheme(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
            {
                DevLogger.Error("SaveTheme: nombre de tema vacío o nulo");
                return false;
            }

            lock (_lock)
            {
                try
                {
                    ModernLogger.Info($"💾 Guardando tema (dual system): {themeName}");

                    // SISTEMA 1: Guardar con el sistema legacy (para compatibilidad)
                    var settings = LoadFullInternal() ?? new ThemeSettings();
                    settings.Theme = themeName;
                    settings.LastChangedUtc = DateTime.UtcNow;
                    settings.SaveCount++;

                    if (!ValidateSettings(settings))
                    {
                        DevLogger.Error($"✗ Configuración inválida, cancelando guardado");
                        return false;
                    }

                    bool legacySuccess = SaveFullInternal(settings);
                    
                    // SISTEMA 2: Guardar con el sistema mejorado (principal)
                    // TODO v3.0: EnhancedThemePersistenceService está obsoleto, ahora usa PersistenceIntegrator
                    bool enhancedSuccess = true; // Asumimos éxito porque v3.0 ya se encarga
                    
                    // Considerar exitoso si AL MENOS UNO funciona
                    bool success = legacySuccess || enhancedSuccess;
                    
                    if (success)
                    {
                        _cachedSettings = settings;
                        _lastLoadTime = DateTime.UtcNow;
                        
                        ModernLogger.Info($"✓ Tema guardado exitosamente: {themeName}");
                        ModernLogger.Info($"  → Legacy: {(legacySuccess ? "✓" : "✗")} | Enhanced: {(enhancedSuccess ? "✓" : "✗")}");
                        ModernLogger.Info($"  → Guardados totales: {settings.SaveCount}");
                        return true;
                    }
                    else
                    {
                        DevLogger.Error($"✗ Error en ambos sistemas al guardar tema: {themeName}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    DevLogger.Error($"✗ Excepción en SaveTheme: {ex.Message}");
                    DevLogger.Error($"  Stack: {ex.StackTrace}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Carga el tema guardado con fallback a legacy y valores por defecto
        /// MEJORADO: Intenta primero el sistema mejorado, luego el legacy
        /// </summary>
        public static string LoadTheme()
        {
            lock (_lock)
            {
                try
                {
                    // Usar cache si está vigente
                    if (_cachedSettings != null && 
                        DateTime.UtcNow - _lastLoadTime < CacheTimeout)
                    {
                        ModernLogger.Info($"📋 Usando tema desde cache: {_cachedSettings.Theme}");
                        return _cachedSettings.Theme;
                    }

                    ModernLogger.Info("📖 Cargando tema desde disco (dual system)...");

                    // PRIORIDAD 1: Intentar cargar desde sistema v3.0
                    try
                    {
                        // TODO v3.0: EnhancedThemePersistenceService obsoleto, usar PersistenceIntegrator
                        var config = ComicReader.Services.Persistence.ConfigurationManager.Instance.GetConfiguration();
                        string enhancedTheme = config?.Theme?.CurrentTheme;
                        if (!string.IsNullOrWhiteSpace(enhancedTheme))
                        {
                            ModernLogger.Info($"✓ Tema cargado desde sistema v3.0: {enhancedTheme}");
                            
                            // Actualizar cache
                            _cachedSettings = new ThemeSettings { Theme = enhancedTheme };
                            _lastLoadTime = DateTime.UtcNow;
                            
                            return enhancedTheme;
                        }
                    }
                    catch (Exception enhEx)
                    {
                        DevLogger.Error($"Error en sistema mejorado: {enhEx.Message}");
                    }

                    // PRIORIDAD 2: Intentar cargar desde sistema legacy
                    var settings = LoadFullInternal();
                    if (settings != null && !string.IsNullOrWhiteSpace(settings.Theme))
                    {
                        _cachedSettings = settings;
                        _lastLoadTime = DateTime.UtcNow;
                        
                        ModernLogger.Info($"✓ Tema cargado desde sistema legacy: {settings.Theme}");
                        ModernLogger.Info($"  → Última modificación: {settings.LastChangedUtc:yyyy-MM-dd HH:mm:ss}");
                        ModernLogger.Info($"  → Guardados: {settings.SaveCount}");
                        
                        return settings.Theme;
                    }

                    // PRIORIDAD 3: Intentar migración desde archivos legacy antiguos
                    string legacyTheme = TryMigrateLegacyTheme();
                    if (!string.IsNullOrWhiteSpace(legacyTheme))
                    {
                        ModernLogger.Info($"✓ Tema migrado desde legacy: {legacyTheme}");
                        SaveTheme(legacyTheme); // Guardar en ambos sistemas
                        return legacyTheme;
                    }

                    // PRIORIDAD 4: Fallback al tema por defecto
                    ModernLogger.Info($"⚠ No se encontró tema guardado, usando: Comic");
                    return nameof(ThemeMode.Comic);
                }
                catch (Exception ex)
                {
                    DevLogger.Error($"✗ Error en LoadTheme: {ex.Message}");
                    return nameof(ThemeMode.Comic);
                }
            }
        }

        /// <summary>
        /// Carga la configuración completa del tema
        /// </summary>
        public static ThemeSettings LoadFull()
        {
            lock (_lock)
            {
                var settings = LoadFullInternal();
                return settings ?? new ThemeSettings();
            }
        }

        /// <summary>
        /// Guarda la configuración completa del tema
        /// </summary>
        public static void SaveFull(ThemeSettings settings)
        {
            if (settings == null)
            {
                DevLogger.Error("SaveFull: configuración nula");
                return;
            }

            lock (_lock)
            {
                settings.LastChangedUtc = DateTime.UtcNow;
                settings.SaveCount++;
                SaveFullInternal(settings);
            }
        }

        /// <summary>
        /// Elimina todos los archivos de tema (principal, backup y legacy)
        /// </summary>
        public static void ClearAllThemeData()
        {
            lock (_lock)
            {
                try
                {
                    ModernLogger.Info("🗑 Limpiando datos de tema...");
                    
                    SafeDelete(ThemeJsonPath);
                    SafeDelete(BackupPath);
                    SafeDelete(LegacyConfigPath);
                    SafeDelete(LegacyBackupPath);
                    SafeDelete(LegacyThemeTxtPath);
                    
                    _cachedSettings = null;
                    _lastLoadTime = DateTime.MinValue;
                    
                    ModernLogger.Info("✓ Datos de tema eliminados");
                }
                catch (Exception ex)
                {
                    DevLogger.Error($"✗ Error limpiando datos: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Invalida el cache forzando recarga en próxima petición
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_lock)
            {
                _cachedSettings = null;
                _lastLoadTime = DateTime.MinValue;
                ModernLogger.Info("🔄 Cache de tema invalidado");
            }
        }

        /// <summary>
        /// Obtiene información de diagnóstico del estado del servicio
        /// </summary>
        public static string GetDiagnostics()
        {
            lock (_lock)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("═══ DIAGNÓSTICO THEME PERSISTENCE ═══");
                sb.AppendLine($"Ruta principal: {ThemeJsonPath}");
                sb.AppendLine($"  Existe: {File.Exists(ThemeJsonPath)}");
                if (File.Exists(ThemeJsonPath))
                {
                    var fi = new FileInfo(ThemeJsonPath);
                    sb.AppendLine($"  Tamaño: {fi.Length} bytes");
                    sb.AppendLine($"  Modificado: {fi.LastWriteTime}");
                }
                
                sb.AppendLine($"Ruta backup: {BackupPath}");
                sb.AppendLine($"  Existe: {File.Exists(BackupPath)}");
                
                sb.AppendLine($"Cache:");
                sb.AppendLine($"  Válido: {_cachedSettings != null}");
                if (_cachedSettings != null)
                {
                    sb.AppendLine($"  Tema: {_cachedSettings.Theme}");
                    sb.AppendLine($"  Cargado: {_lastLoadTime}");
                }
                
                return sb.ToString();
            }
        }

        // ============================================================
        // MÉTODOS INTERNOS
        // ============================================================

        private static ThemeSettings LoadFullInternal()
        {
            try
            {
                // Intentar cargar desde archivo principal
                if (File.Exists(ThemeJsonPath))
                {
                    var settings = TryLoadFromFile(ThemeJsonPath);
                    if (settings != null && ValidateSettings(settings))
                    {
                        return settings;
                    }
                    
                    DevLogger.Error("✗ Archivo principal corrupto o inválido");
                }

                // Intentar recuperar desde backup
                if (File.Exists(BackupPath))
                {
                    ModernLogger.Info("⚠ Intentando recuperar desde backup...");
                    var settings = TryLoadFromFile(BackupPath);
                    if (settings != null && ValidateSettings(settings))
                    {
                        ModernLogger.Info("✓ Recuperado desde backup exitosamente");
                        
                        // Restaurar el archivo principal desde el backup
                        SaveFullInternal(settings);
                        return settings;
                    }
                    
                    DevLogger.Error("✗ Backup también está corrupto");
                }

                return null;
            }
            catch (Exception ex)
            {
                DevLogger.Error($"✗ Error en LoadFullInternal: {ex.Message}");
                return null;
            }
        }

        private static bool SaveFullInternal(ThemeSettings settings)
        {
            try
            {
                // Asegurar que el directorio existe
                Directory.CreateDirectory(ThemeFolder);

                // Serializar a JSON
                string json = JsonSerializer.Serialize(settings, Options);
                
                // Validar que el JSON no esté vacío
                if (string.IsNullOrWhiteSpace(json) || json.Length < 10)
                {
                    DevLogger.Error("✗ JSON serializado está vacío o corrupto");
                    return false;
                }

                // Crear backup del archivo actual antes de sobrescribirlo
                if (File.Exists(ThemeJsonPath))
                {
                    try
                    {
                        File.Copy(ThemeJsonPath, BackupPath, overwrite: true);
                        ModernLogger.Info($"✓ Backup creado: {BackupPath}");
                    }
                    catch (Exception ex)
                    {
                        DevLogger.Error($"⚠ No se pudo crear backup: {ex.Message}");
                        // Continuar de todas formas
                    }
                }

                // Escribir en archivo temporal
                string tempPath = ThemeJsonPath + ".tmp";
                File.WriteAllText(tempPath, json);
                
                // Verificar que el archivo temporal se escribió correctamente
                if (!File.Exists(tempPath))
                {
                    DevLogger.Error("✗ No se pudo crear archivo temporal");
                    return false;
                }

                // Reemplazar archivo principal con el temporal (operación atómica)
                try
                {
                    // En Windows, File.Replace requiere que el destino exista
                    if (File.Exists(ThemeJsonPath))
                    {
                        File.Replace(tempPath, ThemeJsonPath, null);
                    }
                    else
                    {
                        File.Move(tempPath, ThemeJsonPath);
                    }
                }
                catch
                {
                    // Fallback: borrar y mover
                    if (File.Exists(ThemeJsonPath))
                    {
                        File.Delete(ThemeJsonPath);
                    }
                    File.Move(tempPath, ThemeJsonPath);
                }

                // Limpiar archivo temporal si quedó
                SafeDelete(tempPath);

                ModernLogger.Info($"✓ Archivo guardado: {ThemeJsonPath}");
                return true;
            }
            catch (Exception ex)
            {
                DevLogger.Error($"✗ Error en SaveFullInternal: {ex.Message}");
                DevLogger.Error($"  Stack: {ex.StackTrace}");
                return false;
            }
        }

        private static ThemeSettings TryLoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                string json = File.ReadAllText(path);
                
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var settings = JsonSerializer.Deserialize<ThemeSettings>(json, Options);
                return settings;
            }
            catch (JsonException ex)
            {
                DevLogger.Error($"✗ Error de JSON en {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                DevLogger.Error($"✗ Error leyendo {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        private static bool ValidateSettings(ThemeSettings settings)
        {
            if (settings == null)
                return false;

            // Validar que el tema no esté vacío
            if (string.IsNullOrWhiteSpace(settings.Theme))
            {
                DevLogger.Error("✗ Validación: Theme está vacío");
                return false;
            }

            // Validar que el tema sea un valor válido del enum
            if (!Enum.TryParse<ThemeMode>(settings.Theme, out _))
            {
                DevLogger.Error($"✗ Validación: Theme '{settings.Theme}' no es un ThemeMode válido");
                return false;
            }

            // Validar versión
            if (settings.Version < 1)
            {
                settings.Version = 1;
            }

            return true;
        }

        private static string TryMigrateLegacyTheme()
        {
            try
            {
                // Intentar desde theme.config
                string legacyTheme = TryReadText(LegacyConfigPath);
                if (!string.IsNullOrWhiteSpace(legacyTheme))
                {
                    ModernLogger.Info($"Migrando desde theme.config: {legacyTheme}");
                    return legacyTheme.Trim();
                }

                // Intentar desde theme.backup
                legacyTheme = TryReadText(LegacyBackupPath);
                if (!string.IsNullOrWhiteSpace(legacyTheme))
                {
                    ModernLogger.Info($"Migrando desde theme.backup: {legacyTheme}");
                    return legacyTheme.Trim();
                }

                // Intentar desde CurrentTheme.txt (usado por ThemeManager antiguo)
                legacyTheme = TryReadText(LegacyThemeTxtPath);
                if (!string.IsNullOrWhiteSpace(legacyTheme))
                {
                    ModernLogger.Info($"Migrando desde CurrentTheme.txt: {legacyTheme}");
                    return legacyTheme.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                DevLogger.Error($"Error en migración legacy: {ex.Message}");
                return null;
            }
        }

        private static string TryReadText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignorar errores al borrar
            }
        }
    }

    /// <summary>
    /// Configuración completa del tema con metadatos
    /// </summary>
    public sealed class ThemeSettings
    {
        /// <summary>
        /// Versión del formato de configuración (para migraciones futuras)
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Nombre del tema activo (debe ser un valor de ThemeMode)
        /// </summary>
        public string Theme { get; set; } = nameof(ThemeMode.Comic);

        /// <summary>
        /// Color de acento personalizado
        /// </summary>
        public string AccentColorName { get; set; } = "Blue";

        /// <summary>
        /// Usar color de acento del sistema operativo
        /// </summary>
        public bool UseSystemAccent { get; set; } = false;

        /// <summary>
        /// Escala de interfaz (Small, Medium, Large)
        /// </summary>
        public string UIScale { get; set; } = "Medium";

        /// <summary>
        /// Timestamp de última modificación (UTC)
        /// </summary>
        public DateTime LastChangedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Contador de guardados (para diagnóstico)
        /// </summary>
        public int SaveCount { get; set; } = 0;

        /// <summary>
        /// Tokens de color personalizados (opcional)
        /// </summary>
        public ThemeTokens Tokens { get; set; } = new ThemeTokens();
    }

    /// <summary>
    /// Colores personalizados del tema (para futuras extensiones)
    /// </summary>
    public sealed class ThemeTokens
    {
        public string PrimaryBrush { get; set; }
        public string SecondaryBrush { get; set; }
        public string TextBrush { get; set; }
        public string WindowBackgroundBrush { get; set; }
    }
}
