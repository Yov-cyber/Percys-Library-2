using System;
using System.Reflection;

namespace ComicReader.Utils
{
    /// <summary>
    /// Helper para invocar InitializeComponent por reflexión cuando el partial generado por XAML
    /// no está disponible para el analizador estático del editor. Silencia errores si no existe.
    /// </summary>
    public static class XamlHelper
    {
        public static void EnsureInitializeComponent(object target)
        {
            if (target == null) return;
            try
            {
                var mi = target.GetType().GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(target, null);
                }
            }
            catch
            {
                // swallow - editor analyzers may not have generated the partial class
            }
        }
    }
}
