using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ComicReader.Converters
{
    // MultiValue converter: [0]=coverPath (string), [1]=coverThumbnail (BitmapImage)
    // Devuelve la imagen cargada desde disco si existe y es válida; en caso contrario devuelve el thumbnail en memoria.
    public class CoverPathOrThumbnailConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values != null && values.Length >= 1)
                {
                    var path = values[0] as string;
                    // Nunca bloquear el binding aquí. Si hay un thumbnail en memoria, devolverlo inmediatamente.
                    if (values.Length >= 2 && values[1] is BitmapImage bmpInMemory)
                    {
                        TryLog($"Cover converter: returning in-memory thumbnail fallback");
                        return bmpInMemory;
                    }
                    // Si no hay thumbnail en memoria, evitamos intentar leer el archivo desde disco en el Convert,
                    // porque Convert se ejecuta en el hilo de binding y puede bloquear la UI.
                    // La restauración del cover desde disco se hace por los servicios en background (ContinueReadingService/EnsurePersistedCoverAsync).
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        TryLog($"Cover converter: cover file exists but no in-memory thumbnail; skipping sync load: {path}");
                        return null;
                    }
                }
            }
            catch (Exception ex) { TryLog($"Cover converter: unexpected failure: {ex.Message}"); }
            return null;
        }

        private void TryLog(string message)
        {
            try
            {
                var ws = AppDomain.CurrentDomain.BaseDirectory;
                var repoRoot = Path.GetFullPath(Path.Combine(ws, "..", "..", ".."));
                var logDir = Path.Combine(repoRoot, "logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "cover_debug.log");
                File.AppendAllText(logPath, DateTime.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
