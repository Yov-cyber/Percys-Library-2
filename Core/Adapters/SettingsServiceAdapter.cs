using ComicReader.Core.Abstractions;
using ComicReader.Services;

namespace ComicReader.Core.Adapters
{
    // Stub adapter: implementa la interfaz mínima sin efectuar persistencia.
    public class SettingsServiceAdapter : ISettingsService
    {
        public void Initialize() { /* no-op after config removal */ }

        public string Theme => SettingsManager.Settings.Theme;

        public void Save() => SettingsManager.SaveSettings();
    }
}
