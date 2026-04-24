using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ComicReader.Utils;

namespace ComicReader.Services.Persistence
{
    /// <summary>
    /// Sistema de persistencia completo, seguro y definitivo
    /// Reemplaza TODOS los sistemas antiguos de configuración
    /// Único sistema de persistencia activo en la aplicación
    /// </summary>
    public sealed class ConfigurationManager
    {
        private static readonly Lazy<ConfigurationManager> _instance =
            new Lazy<ConfigurationManager>(() => new ConfigurationManager());

        public static ConfigurationManager Instance => _instance.Value;

        // Rutas de archivos
        private readonly string _appDataPath;
        private readonly string _configFilePath;
        private readonly string _backupFilePath;
        private readonly string _legacyPath; // Para eliminar archivos antiguos

        // Configuración actual en memoria
        private AppConfiguration _currentConfig;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        // Opciones de serialización JSON
        private readonly JsonSerializerOptions _jsonOptions;
        
        // Contadores para estadísticas
        private int _saveCount = 0;
        private int _loadCount = 0;
        private int _backupCount = 0;
        private int _legacyFilesDeleted = 0;

        private ConfigurationManager()
        {
            // Configurar rutas
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PercysLibrary"
            );
            _configFilePath = Path.Combine(_appDataPath, "config.json");
            _backupFilePath = Path.Combine(_appDataPath, "config_backup.json");
            _legacyPath = Path.Combine(_appDataPath, "legacy");

            // Configurar opciones JSON
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter() }
            };

            // Inicializar sistema
            InitializeAsync().Wait();

            ModernLogger.Info("✓ ConfigurationManager inicializado (Sistema Nuevo)");
        }

        /// <summary>
        /// Inicializa el sistema de persistencia
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // Crear directorio si no existe
                Directory.CreateDirectory(_appDataPath);

                // PASO 1: Eliminar archivos antiguos del sistema legacy
                await CleanupLegacyFilesAsync();

                // PASO 2: Verificar y cargar configuración
                if (File.Exists(_configFilePath))
                {
                    try
                    {
                        await LoadConfigurationAsync();
                        ModernLogger.Info("✓ Configuración cargada desde archivo existente");
                    }
                    catch (Exception ex)
                    {
                        ModernLogger.Error($"⚠ Archivo corrupto detectado: {ex.Message}");
                        await HandleCorruptConfigAsync();
                    }
                }
                else
                {
                    // Archivo no existe, crear configuración por defecto
                    _currentConfig = AppConfiguration.CreateDefault();
                    await SaveConfigurationAsync();
                    ModernLogger.Info("✓ Configuración por defecto creada");
                }
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error inicializando ConfigurationManager: {ex.Message}");
                _currentConfig = AppConfiguration.CreateDefault();
            }
        }

        /// <summary>
        /// Elimina TODOS los archivos del sistema antiguo
        /// </summary>
        private async Task CleanupLegacyFilesAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var filesToDelete = new[]
                    {
                        // Archivos antiguos de temas
                        Path.Combine(_appDataPath, "theme.json"),
                        Path.Combine(_appDataPath, "theme.backup.json"),
                        Path.Combine(_appDataPath, "theme-config.json"),
                        Path.Combine(_appDataPath, "theme-config.backup.json"),
                        
                        // Archivos antiguos de configuración
                        Path.Combine(_appDataPath, "settings.json"),
                        Path.Combine(_appDataPath, "settings.xml"),
                        Path.Combine(_appDataPath, "preferences.json"),
                        Path.Combine(_appDataPath, "user-config.json"),
                        
                        // Archivos de shortcuts antiguos
                        Path.Combine(_appDataPath, "shortcuts.json"),
                        Path.Combine(_appDataPath, "shortcuts.xml"),
                        
                        // Archivos de bookmarks antiguos
                        Path.Combine(_appDataPath, "bookmarks.json"),
                        Path.Combine(_appDataPath, "smart-bookmarks.json"),
                        
                        // Otros archivos legacy
                        Path.Combine(_appDataPath, "cache.json"),
                        Path.Combine(_appDataPath, "temp.json")
                    };

                    int deleted = 0;
                    foreach (var file in filesToDelete)
                    {
                        if (File.Exists(file))
                        {
                            try
                            {
                                File.Delete(file);
                                deleted++;
                            }
                            catch
                            {
                                // Ignorar errores al eliminar archivos legacy
                            }
                        }
                    }

                    // Eliminar directorio legacy completo si existe
                    if (Directory.Exists(_legacyPath))
                    {
                        try
                        {
                            Directory.Delete(_legacyPath, true);
                            deleted++;
                        }
                        catch
                        {
                            // Ignorar
                        }
                    }

                    _legacyFilesDeleted = deleted;
                    if (deleted > 0)
                    {
                        ModernLogger.Info($"🧹 Limpieza completada: {deleted} archivos legacy eliminados");
                    }
                }
                catch (Exception ex)
                {
                    ModernLogger.Warning($"Advertencia limpiando archivos legacy: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Maneja archivos de configuración corruptos
        /// </summary>
        private async Task HandleCorruptConfigAsync()
        {
            try
            {
                // Intentar restaurar desde backup
                if (File.Exists(_backupFilePath))
                {
                    try
                    {
                        var backupJson = await File.ReadAllTextAsync(_backupFilePath);
                        _currentConfig = JsonSerializer.Deserialize<AppConfiguration>(backupJson, _jsonOptions);
                        
                        if (_currentConfig != null && _currentConfig.Validate())
                        {
                            // Backup válido, restaurar
                            await File.WriteAllTextAsync(_configFilePath, backupJson);
                            ModernLogger.Info("✓ Configuración restaurada desde backup");
                            return;
                        }
                    }
                    catch
                    {
                        // Backup también corrupto
                    }
                }

                // No hay backup válido, crear nuevo
                File.Delete(_configFilePath);
                if (File.Exists(_backupFilePath))
                {
                    File.Delete(_backupFilePath);
                }

                _currentConfig = AppConfiguration.CreateDefault();
                await SaveConfigurationAsync();
                ModernLogger.Info("✓ Configuración corrupta reemplazada con valores por defecto");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error manejando configuración corrupta: {ex.Message}");
                _currentConfig = AppConfiguration.CreateDefault();
            }
        }

        /// <summary>
        /// Carga la configuración desde el archivo JSON
        /// </summary>
        private async Task LoadConfigurationAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                var config = JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions);

                if (config == null || !config.Validate())
                {
                    throw new InvalidOperationException("Configuración inválida");
                }

                _currentConfig = config;
                _loadCount++;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>
        /// Guarda la configuración en el archivo JSON
        /// </summary>
        public async Task SaveConfigurationAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                // Validar antes de guardar
                if (!_currentConfig.Validate())
                {
                    ModernLogger.Warning("⚠ Configuración inválida, no se guardará");
                    return;
                }

                // Crear backup del archivo actual
                if (File.Exists(_configFilePath))
                {
                    File.Copy(_configFilePath, _backupFilePath, true);
                    _backupCount++;
                }

                // Serializar y guardar
                var json = JsonSerializer.Serialize(_currentConfig, _jsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
                _saveCount++;

                ModernLogger.Info("💾 Configuración guardada exitosamente");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error guardando configuración: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>
        /// Obtiene la configuración actual
        /// </summary>
        public AppConfiguration GetConfiguration()
        {
            return _currentConfig ?? AppConfiguration.CreateDefault();
        }

        /// <summary>
        /// Actualiza la configuración y guarda automáticamente
        /// </summary>
        public async Task UpdateConfigurationAsync(Action<AppConfiguration> updateAction)
        {
            try
            {
                updateAction(_currentConfig);
                await SaveConfigurationAsync();
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error actualizando configuración: {ex.Message}");
            }
        }

        /// <summary>
        /// Resetea la configuración a valores por defecto
        /// </summary>
        public async Task ResetToDefaultsAsync()
        {
            _currentConfig = AppConfiguration.CreateDefault();
            await SaveConfigurationAsync();
            ModernLogger.Info("🔄 Configuración reseteada a valores por defecto");
        }

        /// <summary>
        /// Obtiene estadísticas del sistema de persistencia
        /// </summary>
        public PersistenceStatistics GetStatistics()
        {
            return new PersistenceStatistics
            {
                ConfigFilePath = _configFilePath,
                ConfigFileExists = File.Exists(_configFilePath),
                ConfigFileSize = File.Exists(_configFilePath) ? new FileInfo(_configFilePath).Length : 0,
                BackupExists = File.Exists(_backupFilePath),
                LastModified = File.Exists(_configFilePath) ? File.GetLastWriteTime(_configFilePath) : DateTime.MinValue,
                IsValid = _currentConfig?.Validate() ?? false,
                TotalSaves = _saveCount,
                TotalLoads = _loadCount,
                TotalBackups = _backupCount,
                LegacyFilesDeleted = _legacyFilesDeleted
            };
        }
    }

    /// <summary>
    /// Estadísticas del sistema de persistencia
    /// </summary>
    public class PersistenceStatistics
    {
        public string ConfigFilePath { get; set; }
        public bool ConfigFileExists { get; set; }
        public long ConfigFileSize { get; set; }
        public bool BackupExists { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsValid { get; set; }
        
        // Contadores adicionales para diagnóstico
        public int TotalSaves { get; set; }
        public int TotalLoads { get; set; }
        public int TotalBackups { get; set; }
        public int LegacyFilesDeleted { get; set; }
    }
}
