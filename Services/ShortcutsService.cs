using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ComicReader.Services
{
    public static class ShortcutsService
    {
        private static readonly string _fileName = "shortcuts.json";

        public static string GetFilePath()
        {
            try
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appDir = Path.Combine(baseDir ?? string.Empty, "PercysLibrary");
                Directory.CreateDirectory(appDir);
                return Path.Combine(appDir, _fileName);
            }
            catch { return _fileName; }
        }

        public static Dictionary<string, string> Load()
        {
            try
            {
                var p = GetFilePath();
                if (!File.Exists(p)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var txt = File.ReadAllText(p);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(txt);
                return dict ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        public static void Save(Dictionary<string, string> shortcuts)
        {
            try
            {
                var p = GetFilePath();
                var txt = JsonSerializer.Serialize(shortcuts ?? new Dictionary<string, string>(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(p, txt);
            }
            catch { }
        }
    }
}
