
using System;
using ComicReader.Services;

namespace ComicReader.Models
{
    public class UserSettings
    {
        public bool EnableContinuousScroll { get; set; } = false;
        public int PrefetchWindow { get; set; } = 8;
        public string Theme { get; set; } = "Comic Bright";
        public double Brightness { get; set; } = 1.0;
        public bool IsNightMode { get; set; } = false;
        public int ConcurrencyCap { get; set; } = 4;

        public static UserSettings Default() => new UserSettings();

        public static UserSettings LoadFromStubs()
        {
            try
            {
                // If SettingsStubs provides a Settings instance convert it, otherwise default
                var s = SettingsManager.Settings as dynamic;
                if (s == null) return Default();
                return new UserSettings
                {
                    EnableContinuousScroll = s.EnableContinuousScroll ?? false,
                    PrefetchWindow = s.PrefetchWindow ?? 8,
                    Theme = s.Theme ?? "Comic Bright",
                    Brightness = s.Brightness ?? 1.0,
                    IsNightMode = s.IsNightMode ?? false,
                    ConcurrencyCap = s.ConcurrencyCap ?? 4
                };
            }
            catch { return Default(); }
        }
    }
}
