using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComicReader.Models;
using System.Windows.Media.Imaging;

namespace ComicReader.Services
{
    /// Servicio central para gestionar la lista de "Seguir leyendo" con persistencia en JSON.
    public sealed class ContinueReadingService : IDisposable
    {
        private static readonly Lazy<ContinueReadingService> _lazy = new Lazy<ContinueReadingService>(() => new ContinueReadingService());
        public static ContinueReadingService Instance => _lazy.Value;

        private readonly object _lock = new object();
        private readonly string _dataPath;
        private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _coversDir;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        // Track background tasks started by the service so we can await them on dispose.
        private readonly ConcurrentBag<Task> _backgroundTasks = new ConcurrentBag<Task>();

        // Helper to run background work associated to this service and track it.
        private void RunBackground(Func<CancellationToken, Task> work)
        {
            try
            {
                var task = Task.Run(async () =>
                {
                    try { await work(_cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch { }
                }, _cts.Token);
                _backgroundTasks.Add(task);
            }
            catch { }
        }

    public ObservableCollection<ContinueItem> Items { get; private set; }
    public ObservableCollection<ContinueItem> CompletedItems { get; private set; }

        public event Action ListChanged;

        private ContinueReadingService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "PercysLibrary");
            Directory.CreateDirectory(dir);
            _coversDir = Path.Combine(dir, "covers");
            Directory.CreateDirectory(_coversDir);
            _dataPath = Path.Combine(dir, "continue_reading.json");
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                AllowTrailingCommas = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            Items = new ObservableCollection<ContinueItem>();
            CompletedItems = new ObservableCollection<ContinueItem>();
            Items.CollectionChanged += Items_CollectionChanged;
            CompletedItems.CollectionChanged += CompletedItems_CollectionChanged;
            Load();
        }

        // Logging helper para diagnosticar persistencia de covers (escribe en workspace/logs)
        private void LogCoverDebug(string message)
        {
            try
            {
                var ws = AppDomain.CurrentDomain.BaseDirectory; // bin folder
                var repoRoot = Path.GetFullPath(Path.Combine(ws, "..", "..", ".."));
                var logDir = Path.Combine(repoRoot, "logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "cover_debug.log");
                File.AppendAllText(logPath, DateTime.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch { }
        }

    public async Task EnsurePersistedCoverAsync(ContinueItem item)
        {
            try
            {
                if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;
                // Si ya tiene CoverPath y el archivo existe, nada que hacer
                if (!string.IsNullOrWhiteSpace(item.CoverPath) && File.Exists(item.CoverPath))
                {
                    LogCoverDebug($"EnsurePersistedCoverAsync: already persisted for {item.FilePath} -> {item.CoverPath}");
                    return;
                }

                // Intentar generar un cover usando ComicPageLoader: cargar la estructura y seleccionar la mejor portada
                BitmapImage bmp = null;
                try
                {
                    using (var loader = new ComicReader.Services.ComicPageLoader(item.FilePath))
                    {
                        try { await loader.LoadComicAsync().ConfigureAwait(false); } catch { }
                        bmp = await loader.GetCoverThumbnailAsync(300, 420).ConfigureAwait(false);
                    }
                }
                catch { bmp = null; }
                if (bmp != null)
                {
                    var fileName = HashPathForFileName(item.FilePath) + ".png";
                    var outPath = Path.Combine(_coversDir, fileName);
                    // Guardar BitmapImage como PNG (en hilo de pool) con verificación y reintentos
                    var saved = false;
                    int attempts = 0;
                            while (attempts < 3 && !saved)
                    {
                        attempts++;
                        try
                        {
                                    await Task.Run(() => SaveBitmapImageToPng(bmp, outPath), _cts.Token).ConfigureAwait(false);
                            // Verificar que el archivo existe, tiene tamaño razonable y se puede abrir
                            if (File.Exists(outPath))
                            {
                                var fi = new FileInfo(outPath);
                                if (fi.Length > 1024)
                                {
                                    // intentar abrir
                                    try
                                    {
                                        var test = new System.Windows.Media.Imaging.BitmapImage();
                                        test.BeginInit();
                                        test.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                        test.UriSource = new Uri(outPath);
                                        test.EndInit();
                                        test.Freeze();
                                        saved = true;
                                    }
                                    catch { saved = false; }
                                }
                                else saved = false;
                            }
                        }
                        catch (Exception ex) { LogCoverDebug($"Save attempt {attempts} failed: {ex.Message}"); }
                        if (!saved) await Task.Delay(150).ConfigureAwait(false);
                    }
                    LogCoverDebug($"EnsurePersistedCoverAsync: generated cover for {item.FilePath}, saved={saved}, outPath={outPath}, attempts={attempts}");
                    // Construir BitmapImage desde archivo (se puede hacer en background y freeze)
                    BitmapImage loaded = null;
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.UriSource = new Uri(outPath);
                        bi.EndInit();
                        bi.Freeze();
                        loaded = bi;
                    }
                    catch { loaded = null; }
                    // Asignar la ruta y el thumbnail en el hilo UI y persistir
                    try
                    {
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            try { item.CoverPath = outPath; } catch { }
                            try { if (loaded != null) item.CoverThumbnail = loaded; } catch { }
                            try { Save(); } catch { }
                            LogCoverDebug($"EnsurePersistedCoverAsync: assigned CoverPath and thumbnail for {item.FilePath} -> {outPath}");
                            try { ListChanged?.Invoke(); } catch { }
                            try { SafeNotify(); } catch { }
                            // Intentar forzar un refresh extra en UI (HomeView puede suscribirse a ListChanged)
                            try { System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() => { })); } catch { }
                        }));
                    }
                    catch
                    {
                        try { item.CoverPath = outPath; } catch { }
                        try { if (loaded != null) item.CoverThumbnail = loaded; } catch { }
                        try { Save(); } catch { }
                        LogCoverDebug($"EnsurePersistedCoverAsync: assigned CoverPath (fallback) for {item.FilePath} -> {outPath}");
                    }
                }
            }
            catch { }
        }

        private void TryLoadCoverFromPath(ContinueItem item)
        {
            try
            {
                if (item == null) return;
                if (!string.IsNullOrWhiteSpace(item.CoverPath) && File.Exists(item.CoverPath))
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(item.CoverPath);
                    bi.EndInit();
                    bi.Freeze();
                    item.CoverThumbnail = bi;
                }
            }
            catch { }
        }

        private static string HashPathForFileName(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA1.Create())
                {
                    var b = System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant());
                    var h = sha.ComputeHash(b);
                    return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
                }
            }
            catch { return Guid.NewGuid().ToString("N"); }
        }

        private static void SaveBitmapImageToPng(System.Windows.Media.Imaging.BitmapImage bmp, string outPath)
        {
            try
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }
            }
            catch { }
        }

        private void Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Propagar cambios y persistir
            if (e.NewItems != null)
            {
                foreach (var it in e.NewItems.OfType<ContinueItem>())
                {
                    it.PropertyChanged -= Item_PropertyChanged;
                    it.PropertyChanged += Item_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (var it in e.OldItems.OfType<ContinueItem>())
                {
                    it.PropertyChanged -= Item_PropertyChanged;
                }
            }
            Save();
            SafeNotify();
        }

        private void CompletedItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var it in e.NewItems.OfType<ContinueItem>())
                {
                    it.PropertyChanged -= Item_PropertyChanged;
                    it.PropertyChanged += Item_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (var it in e.OldItems.OfType<ContinueItem>())
                {
                    it.PropertyChanged -= Item_PropertyChanged;
                }
            }
            Save();
            SafeNotify();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                // Evitar bucles de reentrada: cambios en la miniatura no deben provocar
                // una persistencia/refresh que vuelva a disparar la carga de portadas.
                if (e != null && string.Equals(e.PropertyName, nameof(ContinueItem.CoverThumbnail), StringComparison.OrdinalIgnoreCase))
                {
                    // No persistir ni notificar para cambios puramente visuales de la miniatura
                    return;
                }

                // Para cambios en CoverPath, solo persistir el JSON, pero evitar SafeNotify que puede forzar recargas
                if (e != null && string.Equals(e.PropertyName, nameof(ContinueItem.CoverPath), StringComparison.OrdinalIgnoreCase))
                {
                    Save();
                    return;
                }

                Save();
                SafeNotify();
            }
            catch { }
        }

        private void SafeNotify()
        {
            try { ListChanged?.Invoke(); } catch { }
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var json = File.ReadAllText(_dataPath);
                    // Deserialize into a wrapper that can contain both lists
                    var wrapper = JsonSerializer.Deserialize<ContinueStorage>(json, _jsonOptions) ?? new ContinueStorage();
                    Items.CollectionChanged -= Items_CollectionChanged;
                    CompletedItems.CollectionChanged -= CompletedItems_CollectionChanged;
                    Items.Clear();
                    CompletedItems.Clear();

                    // Combine both arrays and group by FilePath to deduplicate and decide destination
                    var all = new List<ContinueItem>();
                    if (wrapper.Items != null) all.AddRange(wrapper.Items);
                    if (wrapper.CompletedItems != null) all.AddRange(wrapper.CompletedItems);

                    var groups = all.GroupBy(x => (x.FilePath ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
                    foreach (var g in groups)
                    {
                        var chosen = g.OrderByDescending(x => x.LastOpened).ThenByDescending(x => x.DateCompleted ?? DateTime.MinValue).FirstOrDefault();
                        if (chosen == null) continue;

                        // Decide si debe ir a CompletedItems
                        bool shouldBeCompleted = false;
                        try
                        {
                            if (chosen.IsCompleted) shouldBeCompleted = true;
                            else if (chosen.PageCount > 0 && chosen.LastPage >= chosen.PageCount) shouldBeCompleted = true;
                        }
                        catch { }

                        // Subscribe property changed; defer loading persisted cover to background to avoid blocking
                        try { chosen.PropertyChanged += Item_PropertyChanged; } catch { }
                        // We'll restore persisted covers in background after assembling collections

                        // If no CoverPath, we'll check for existing cached PNG in background later

                        if (shouldBeCompleted)
                        {
                            // ensure completed flags
                            try { chosen.IsCompleted = true; } catch { }
                            if (chosen.DateCompleted == null || chosen.DateCompleted == DateTime.MinValue)
                            {
                                try { chosen.DateCompleted = DateTime.Now; } catch { }
                            }
                            CompletedItems.Add(chosen);
                        }
                        else
                        {
                            try { chosen.IsCompleted = false; } catch { }
                            Items.Add(chosen);
                        }
                    }
                    // Generar en background portadas faltantes (evitar bloquear Load)
                    RunBackground(async (ct) =>
                    {
                        foreach (var a in Items.Concat(CompletedItems))
                        {
                            if (ct.IsCancellationRequested) break;
                            if (a != null && string.IsNullOrWhiteSpace(a.CoverPath))
                            {
                                try { await EnsurePersistedCoverAsync(a).ConfigureAwait(false); } catch { }
                                try { await Task.Delay(200, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                            }
                        }
                    });

                    // Restaurar en background CoverPath desde carpeta de covers para items que no tienen CoverPath asignado
                    RunBackground(async (ct) =>
                    {
                        foreach (var chosenItem in Items.Concat(CompletedItems))
                        {
                            if (ct.IsCancellationRequested) break;
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(chosenItem.CoverPath)) continue;
                                var candidate = Path.Combine(_coversDir, HashPathForFileName(chosenItem.FilePath) + ".png");
                                if (!File.Exists(candidate)) continue;
                                try
                                {
                                    var bi = new BitmapImage();
                                    using (var fs = File.OpenRead(candidate))
                                    {
                                        bi.BeginInit();
                                        bi.CacheOption = BitmapCacheOption.OnLoad;
                                        bi.StreamSource = fs;
                                        bi.EndInit();
                                        bi.Freeze();
                                    }
                                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                    {
                                        try { chosenItem.CoverPath = candidate; } catch { }
                                        try { chosenItem.CoverThumbnail = bi; } catch { }
                                    }));
                                    LogCoverDebug($"Load(): restored coverPath for {chosenItem.FilePath} -> {candidate}");
                                }
                                catch { }
                            }
                            catch { }
                        }
                        await Task.CompletedTask;
                    });
                    Items.CollectionChanged += Items_CollectionChanged;
                    CompletedItems.CollectionChanged += CompletedItems_CollectionChanged;
                }
            }
            catch { /* Silencioso */ }
        }

        public void Save()
        {
            try
            {
                lock (_lock)
                {
                    var wrapper = new ContinueStorage { Items = Items.ToArray(), CompletedItems = CompletedItems.ToArray() };
                    var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                    File.WriteAllText(_dataPath, json);
                }
            }
            catch { /* Silencioso */ }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try
            {
                // Wait a short time for background tasks to finish gracefully
                Task.WaitAll(_backgroundTasks.ToArray(), 1500);
            }
            catch { }
            try { _cts?.Dispose(); } catch { }
        }

        public void UpsertProgress(string filePath, int currentPageOneBased, int pageCount)
        {
            try
            {
                // Registrar llamada para depuración
                try { System.Diagnostics.Debug.WriteLine($"UpsertProgress called: filePath={filePath}, current={currentPageOneBased}, pageCount={pageCount}"); } catch { }

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    try { System.Diagnostics.Debug.WriteLine("UpsertProgress: filePath is null or whitespace -> skipping"); } catch { }
                    return;
                }
                if (pageCount <= 0)
                {
                    try { System.Diagnostics.Debug.WriteLine("UpsertProgress: pageCount <= 0 -> skipping"); } catch { }
                    return;
                }

                currentPageOneBased = Math.Max(1, Math.Min(pageCount, currentPageOneBased));
                var displayName = Path.GetFileNameWithoutExtension(filePath);

                // Si el usuario ya está en la última página, mover automáticamente a "Completados"
                if (currentPageOneBased >= pageCount)
                {
                    try { System.Diagnostics.Debug.WriteLine($"UpsertProgress: reached last page for {displayName} -> MoveToCompleted"); } catch { }

                    // Mostrar un toast ligero para depuración y confirmación visual
                    try
                    {
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                try { ComicReader.Services.ToastService.Show($"[Depuración] Se alcanzó 100%: {displayName}", ComicReader.Views.ToastWindow.ToastKind.Info); } catch { }
                            }));
                    }
                    catch { }

                    MoveToCompleted(filePath, displayName, pageCount);
                    return;
                }

                var existing = Items.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new ContinueItem
                    {
                        FilePath = filePath,
                        DisplayName = displayName,
                        PageCount = pageCount,
                        LastPage = currentPageOneBased,
                        LastOpened = DateTime.Now,
                    };
                    Items.Insert(0, existing);
                    try { RunBackground(async (ct) => { try { await EnsurePersistedCoverAsync(existing).ConfigureAwait(false); } catch { } }); } catch { }
                }
                else
                {
                    existing.PageCount = pageCount;
                    existing.LastPage = currentPageOneBased;
                    existing.LastOpened = DateTime.Now;
                    // Mover al principio para mantener orden de uso reciente
                    var idx = Items.IndexOf(existing);
                    if (idx > 0)
                    {
                        Items.Move(idx, 0);
                    }
                }

                // Ya gestionamos la eliminación automática cuando se completa; mantener flag en falso aquí
                existing.IsCompleted = false;
                // Si no tiene portada persistida, intentar generarla en background
                try { if (string.IsNullOrWhiteSpace(existing.CoverPath)) RunBackground(async (ct) => { try { await EnsurePersistedCoverAsync(existing).ConfigureAwait(false); } catch { } }); } catch { }
                Save();
                SafeNotify();
            }
            catch (Exception ex)
            {
                try { System.Diagnostics.Debug.WriteLine($"UpsertProgress exception: {ex.Message}"); } catch { }
            }
        }

        public void Remove(string filePath)
        {
            var item = Items.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (item != null) Items.Remove(item);
            var citem = CompletedItems.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (citem != null) CompletedItems.Remove(citem);
        }

        public void Clear()
        {
            Items.Clear();
        }

        public void ClearCompleted()
        {
            CompletedItems.Clear();
        }

        private void MoveToCompleted(string filePath, string displayName, int pageCount)
        {
            try
            {
                var existing = Items.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    Items.Remove(existing);
                    existing.IsCompleted = true;
                    existing.LastPage = pageCount;
                    existing.PageCount = pageCount;
                    existing.DateCompleted = DateTime.Now;
                    if (existing.TotalReadSeconds == null) existing.TotalReadSeconds = 0;
                    try { RunBackground(async (ct) => { try { await EnsurePersistedCoverAsync(existing).ConfigureAwait(false); } catch { } }); } catch { }
                    CompletedItems.Insert(0, existing);
                }
                else
                {
                    var newItem = new ContinueItem
                    {
                        FilePath = filePath,
                        DisplayName = displayName,
                        PageCount = pageCount,
                        LastPage = pageCount,
                        IsCompleted = true,
                        DateCompleted = DateTime.Now
                    };
                    try { RunBackground(async (ct) => { try { await EnsurePersistedCoverAsync(newItem).ConfigureAwait(false); } catch { } }); } catch { }
                    CompletedItems.Insert(0, newItem);
                }
                Save();
                SafeNotify();
                try
                {
                    // Mostrar un toast ligero para depuración: indicar que se movió a completados
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        try { ComicReader.Services.ToastService.Show($"Completado: {displayName}", ComicReader.Views.ToastWindow.ToastKind.Success); } catch { }
                    }));
                }
                catch { }
            }
            catch { }
        }

    }
}
