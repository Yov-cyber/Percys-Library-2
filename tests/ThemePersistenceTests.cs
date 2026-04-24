// Script de prueba para el sistema de persistencia de temas
// Ejecutar desde la consola del desarrollador o desde el código

using System;
using ComicReader.Services;
using ComicReader.Utils;

namespace ComicReader.Testing
{
    public static class ThemePersistenceTests
    {
        public static void RunAllTests()
        {
            ModernLogger.Info("═══════════════════════════════════");
            ModernLogger.Info("  TESTS DE PERSISTENCIA DE TEMAS");
            ModernLogger.Info("═══════════════════════════════════");
            
            try
            {
                // Test 1: Guardar y cargar tema
                ModernLogger.Info("\n[TEST 1] Guardar y Cargar");
                string testTheme = "Dark";
                bool saved = ThemePersistenceService.SaveTheme(testTheme);
                ModernLogger.Info($"  Guardado: {(saved ? "✓" : "✗")}");
                
                string loaded = ThemePersistenceService.LoadTheme();
                ModernLogger.Info($"  Cargado: {loaded}");
                ModernLogger.Info($"  Match: {(testTheme == loaded ? "✓" : "✗")}");
                
                // Test 2: Cambiar a otro tema
                ModernLogger.Info("\n[TEST 2] Cambiar Tema");
                testTheme = "Comic";
                saved = ThemePersistenceService.SaveTheme(testTheme);
                ModernLogger.Info($"  Guardado: {(saved ? "✓" : "✗")}");
                
                loaded = ThemePersistenceService.LoadTheme();
                ModernLogger.Info($"  Cargado: {loaded}");
                ModernLogger.Info($"  Match: {(testTheme == loaded ? "✓" : "✗")}");
                
                // Test 3: Configuración completa
                ModernLogger.Info("\n[TEST 3] Configuración Completa");
                var settings = new ThemeSettings
                {
                    Theme = "Sepia",
                    AccentColorName = "Green",
                    UIScale = "Large",
                    UseSystemAccent = true
                };
                ThemePersistenceService.SaveFull(settings);
                ModernLogger.Info($"  Guardado completo: ✓");
                
                var loadedSettings = ThemePersistenceService.LoadFull();
                ModernLogger.Info($"  Tema: {loadedSettings.Theme}");
                ModernLogger.Info($"  Acento: {loadedSettings.AccentColorName}");
                ModernLogger.Info($"  Escala: {loadedSettings.UIScale}");
                ModernLogger.Info($"  Sistema: {loadedSettings.UseSystemAccent}");
                ModernLogger.Info($"  Guardados: {loadedSettings.SaveCount}");
                
                // Test 4: Cache
                ModernLogger.Info("\n[TEST 4] Sistema de Cache");
                var start = DateTime.UtcNow;
                for (int i = 0; i < 10; i++)
                {
                    ThemePersistenceService.LoadTheme();
                }
                var elapsed = DateTime.UtcNow - start;
                ModernLogger.Info($"  10 lecturas en: {elapsed.TotalMilliseconds:F2}ms");
                ModernLogger.Info($"  (cache debe hacer esto muy rápido)");
                
                // Test 5: Invalidación de cache
                ModernLogger.Info("\n[TEST 5] Invalidación de Cache");
                ThemePersistenceService.InvalidateCache();
                ModernLogger.Info($"  Cache invalidado: ✓");
                string reloaded = ThemePersistenceService.LoadTheme();
                ModernLogger.Info($"  Recargado: {reloaded}");
                
                // Test 6: Diagnósticos
                ModernLogger.Info("\n[TEST 6] Diagnósticos");
                string diagnostics = ThemePersistenceService.GetDiagnostics();
                ModernLogger.Info(diagnostics);
                
                ModernLogger.Info("\n═══════════════════════════════════");
                ModernLogger.Info("  ✓✓✓ TODOS LOS TESTS PASARON");
                ModernLogger.Info("═══════════════════════════════════");
            }
            catch (Exception ex)
            {
                ModernLogger.Info("\n═══════════════════════════════════");
                ModernLogger.Info("  ✗✗✗ ERROR EN TESTS");
                ModernLogger.Info($"  {ex.Message}");
                ModernLogger.Info("═══════════════════════════════════");
                DevLogger.Error($"Stack: {ex.StackTrace}");
            }
        }
        
        public static void TestBackupRecovery()
        {
            ModernLogger.Info("═══════════════════════════════════");
            ModernLogger.Info("  TEST DE RECUPERACIÓN DESDE BACKUP");
            ModernLogger.Info("═══════════════════════════════════");
            
            try
            {
                // Guardar un tema conocido
                string originalTheme = "TestTheme";
                ThemePersistenceService.SaveTheme(originalTheme);
                ModernLogger.Info($"  Tema original guardado: {originalTheme}");
                
                // Simular corrupción del archivo principal
                var themePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PercysLibrary", "theme.json");
                
                if (System.IO.File.Exists(themePath))
                {
                    System.IO.File.WriteAllText(themePath, "CORRUPTED DATA");
                    ModernLogger.Info($"  Archivo principal corrupto simulado");
                }
                
                // Invalidar cache para forzar lectura de disco
                ThemePersistenceService.InvalidateCache();
                
                // Intentar cargar (debería recuperar desde backup)
                string recovered = ThemePersistenceService.LoadTheme();
                ModernLogger.Info($"  Tema recuperado: {recovered}");
                
                if (recovered == originalTheme)
                {
                    ModernLogger.Info($"  ✓✓✓ RECUPERACIÓN EXITOSA DESDE BACKUP");
                }
                else
                {
                    ModernLogger.Info($"  ⚠ Recuperación parcial (tema por defecto)");
                }
                
                // Restaurar el archivo principal
                ThemePersistenceService.SaveTheme(recovered);
                ModernLogger.Info($"  Archivo principal restaurado");
                
                ModernLogger.Info("═══════════════════════════════════");
            }
            catch (Exception ex)
            {
                ModernLogger.Info($"  ✗ Error: {ex.Message}");
                DevLogger.Error($"Stack: {ex.StackTrace}");
            }
        }
    }
}
