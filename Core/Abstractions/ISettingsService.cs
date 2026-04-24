namespace ComicReader.Core.Abstractions
{
    public interface ISettingsService
    {
        // Contrato mínimo: exponer tema actual y permitir persistencia/initialización (stubs aceptables).
        string Theme { get; }
        void Save();
        void Initialize();
    }
}
