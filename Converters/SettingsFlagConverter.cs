using System;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using ComicReader.Services;

namespace ComicReader.Converters
{
    /// <summary>
    /// Devuelve el valor de una propiedad booleana dentro de SettingsManager.Settings dado el nombre de la propiedad
    /// pasado en ConverterParameter (ej. "EnableAnimationsButtons").
    /// Esto permite enlazar en XAML sin depender del orden de carga de recursos como AppSettingsProxy.
    /// </summary>
    public class SettingsFlagConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var propName = parameter as string;
                if (string.IsNullOrEmpty(propName)) return Binding.DoNothing;

                // Intentar obtener Settings directo (si existe la clase accesible)
                try
                {
                    var s = SettingsManager.Settings;
                    if (s != null)
                    {
                        var pi = s.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (pi != null)
                        {
                            var v = pi.GetValue(s);
                            if (v is bool b) return b;
                            if (v is int i) return i != 0;
                            if (v is string ss && bool.TryParse(ss, out var rb)) return rb;
                        }
                    }
                }
                catch { }

                // Fallback por reflexión (ensamblado actual)
                var asm = Assembly.GetExecutingAssembly();
                var t = asm.GetType("ComicReader.SettingsManager") ?? asm.GetType("ComicReader.Services.SettingsManager") ?? asm.GetType("ComicReader.Core.SettingsManager");
                if (t != null)
                {
                    var p = t.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
                    if (p != null)
                    {
                        var settings = p.GetValue(null);
                        if (settings != null)
                        {
                            var pi2 = settings.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                            if (pi2 != null)
                            {
                                var v = pi2.GetValue(settings);
                                if (v is bool b2) return b2;
                                if (v is int i2) return i2 != 0;
                                if (v is string s2 && bool.TryParse(s2, out var rb2)) return rb2;
                            }
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Settings are application-owned; updating them via ConvertBack is not supported here.
            return Binding.DoNothing;
        }
    }
}
