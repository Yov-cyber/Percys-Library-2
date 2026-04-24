#if BENCH
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class PrefetchTuner
{
    static async Task<int> Main(string[] args)
    {
        Directory.CreateDirectory("tools/logs");
        var logPath = Path.Combine("tools/logs", "prefetch_tuner.log");
        using var writer = new StreamWriter(logPath, append: false);
        Console.WriteLine("Prefetch Tuner starting. Log: " + logPath);
        writer.WriteLine("Prefetch Tuner run at " + DateTime.UtcNow.ToString("o"));

    var defaultCandidates = new[] { 1, 2, 3, 4, 6 };
    bool runE2E = args != null && args.Length > 0 && args.Contains("--e2e");
    int pageCount = 30;

    // CLI params: --maxWindow N --maxCap M --reps R --csv
    int maxWindow = 6;
    int maxCap = 4;
    int reps = 3;
    bool writeCsv = args != null && args.Length > 0 && args.Contains("--csv");
    if (args != null)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--maxWindow" && i + 1 < args.Length && int.TryParse(args[i + 1], out var mw)) maxWindow = mw;
            if (args[i] == "--maxCap" && i + 1 < args.Length && int.TryParse(args[i + 1], out var mc)) maxCap = mc;
            if (args[i] == "--reps" && i + 1 < args.Length && int.TryParse(args[i + 1], out var r)) reps = r;
        }
    }

        // Try to instantiate ComicPageLoader from the workspace. If fails, we'll simulate.
        
        object loader = null;
        bool loaderUsable = false;
            try
            {
                // Use reflection to avoid compile-time dependency issues if class is internal/needs params
                string[] probe = new[] {
                    Path.GetFullPath("ComicReader.dll"),
                    Path.GetFullPath("bin\\Debug\\net8.0-windows\\ComicReader.dll")
                };
                string found = probe.FirstOrDefault(File.Exists);
                if (found == null) throw new FileNotFoundException("ComicReader.dll not found in expected locations: " + string.Join(",", probe));
                var asm = System.Reflection.Assembly.LoadFrom(found);
            var type = asm.GetType("ComicPageLoader") ?? asm.GetTypes().FirstOrDefault(t => t.Name == "ComicPageLoader");
            if (type != null)
            {
                loader = Activator.CreateInstance(type);
                loaderUsable = loader != null;
                writer.WriteLine("Created ComicPageLoader instance via reflection.");
            }
            else writer.WriteLine("ComicPageLoader type not found in ComicReader.dll");
        }
        catch (Exception ex)
        {
            writer.WriteLine("Could not create ComicPageLoader via reflection: " + ex.Message);
        }

        // Sweep over window x cap
        var windows = Enumerable.Range(1, Math.Max(1, Math.Min(maxWindow, 12))).ToArray();
        var caps = Enumerable.Range(1, Math.Max(1, Math.Min(maxCap, 12))).ToArray();

        // Prepare CSV
        var csvPath = Path.Combine("tools/logs", "prefetch_tuner_extended.csv");
        if (writeCsv)
        {
            using var csvWriter = new StreamWriter(csvPath, append: false);
            csvWriter.WriteLine("window,cap,rep,p50_ms,p90_ms,mean_ms,samples");
            csvWriter.Flush();

            foreach (var window in windows)
            foreach (var cap in caps)
            {
                writer.WriteLine($"\nTesting prefetch window = {window}, cap = {cap}");
                Console.WriteLine($"Testing prefetch window = {window}, cap = {cap}");

                for (int r = 0; r < reps; r++)
                {
                    // If loader supports SetPrefetchWindow and SetConcurrencyCap, try to set them
                    if (loaderUsable)
                    {
                        try
                        {
                            var setMethod = loader.GetType().GetMethod("SetPrefetchWindow");
                            if (setMethod != null) setMethod.Invoke(loader, new object[] { window });
                            var setCap = loader.GetType().GetMethod("SetConcurrencyCap");
                            if (setCap != null) setCap.Invoke(loader, new object[] { cap });
                            writer.WriteLine($"Set prefetch window={window} and cap={cap} on loader (via reflection).");
                        }
                        catch (Exception ex)
                        {
                            writer.WriteLine("Loader.Set* failed: " + ex.Message);
                        }
                    }

                    var samples = new List<long>();
                    int navStart = 0;
                    int navCount = Math.Min(10, pageCount - 1);
                    for (int i = 0; i < navCount; i++)
                    {
                        int page = navStart + i;
                        var sw = Stopwatch.StartNew();
                        try
                        {
                            if (loaderUsable)
                            {
                                var method = loader.GetType().GetMethod("GetPageImageAsync");
                                if (method != null)
                                {
                                    var desiredWidth = 1200;
                                    var task = method.Invoke(loader, new object[] { page, desiredWidth }) as System.Threading.Tasks.Task;
                                    if (task != null) await task.ConfigureAwait(false);
                                }
                                else
                                {
                                    var m2 = loader.GetType().GetMethod("GetPageImage");
                                    if (m2 != null) m2.Invoke(loader, new object[] { page });
                                    else await Task.Delay(50);
                                }
                            }
                            else
                            {
                                await Task.Delay(50 + window * 10);
                            }
                        }
                        catch (Exception ex)
                        {
                            writer.WriteLine($"Error loading page {page}: " + ex.Message);
                        }
                        sw.Stop();
                        samples.Add(sw.ElapsedMilliseconds);
                        writer.WriteLine($"Sample page {page}: {sw.ElapsedMilliseconds}ms");
                    }

                    if (samples.Count == 0)
                    {
                        writer.WriteLine("No samples collected for window " + window);
                        continue;
                    }

                    samples.Sort();
                    double p50 = Percentile(samples, 50);
                    double p90 = Percentile(samples, 90);
                    double mean = samples.Average();
                    writer.WriteLine($"Result window={window} cap={cap} rep={r}: p50={p50}ms p90={p90}ms mean={mean}ms");
                    Console.WriteLine($"Result window={window} cap={cap} rep={r}: p50={p50}ms p90={p90}ms mean={mean}ms");
                    csvWriter.WriteLine($"{window},{cap},{r},{p50},{p90},{mean},{samples.Count}");
                    csvWriter.Flush();
                }
            }
        }
        else
        {
            // fallback: behave like before but with windows based on maxWindow
            foreach (var window in Enumerable.Range(1, Math.Max(1, Math.Min(maxWindow, 12))))
            {
                writer.WriteLine($"\nTesting prefetch window = {window}");
                Console.WriteLine($"Testing prefetch window = {window}");

                if (loaderUsable)
                {
                    try
                    {
                        var setMethod = loader.GetType().GetMethod("SetPrefetchWindow");
                        if (setMethod != null) setMethod.Invoke(loader, new object[] { window });
                        writer.WriteLine("Set prefetch window on loader (via reflection).");
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine("Loader.SetPrefetchWindow failed: " + ex.Message);
                    }
                }

                var samples = new List<long>();
                int navStart = 0;
                int navCount = Math.Min(10, pageCount - 1);
                for (int i = 0; i < navCount; i++)
                {
                    int page = navStart + i;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        if (loaderUsable)
                        {
                            var method = loader.GetType().GetMethod("GetPageImageAsync");
                            if (method != null)
                            {
                                var desiredWidth = 1200;
                                var task = method.Invoke(loader, new object[] { page, desiredWidth }) as System.Threading.Tasks.Task;
                                if (task != null) await task.ConfigureAwait(false);
                            }
                            else
                            {
                                var m2 = loader.GetType().GetMethod("GetPageImage");
                                if (m2 != null) m2.Invoke(loader, new object[] { page });
                                else await Task.Delay(50);
                            }
                        }
                        else
                        {
                            await Task.Delay(50 + window * 10);
                        }
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine($"Error loading page {page}: " + ex.Message);
                    }
                    sw.Stop();
                    samples.Add(sw.ElapsedMilliseconds);
                    writer.WriteLine($"Sample page {page}: {sw.ElapsedMilliseconds}ms");
                }

                if (samples.Count == 0)
                {
                    writer.WriteLine("No samples collected for window " + window);
                    continue;
                }

                samples.Sort();
                double p50 = Percentile(samples, 50);
                double p90 = Percentile(samples, 90);
                double mean = samples.Average();
                writer.WriteLine($"Result window={window}: p50={p50}ms p90={p90}ms mean={mean}ms");
                Console.WriteLine($"Result window={window}: p50={p50}ms p90={p90}ms mean={mean}ms");
            }
        }

        if (runE2E && loaderUsable)
        {
            writer.WriteLine("\nRunning E2E simulated navigation benchmark (10 pages)...");
            Console.WriteLine("Running E2E simulated navigation benchmark (10 pages)...");
            var samplesE2E = new List<long>();
            for (int p = 0; p < 10; p++)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var method = loader.GetType().GetMethod("GetPageImageAsync");
                    if (method != null)
                    {
                        var task = method.Invoke(loader, new object[] { p, 1200 }) as System.Threading.Tasks.Task;
                        if (task != null) await task.ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    writer.WriteLine("E2E page load error: " + ex.Message);
                }
                sw.Stop();
                samplesE2E.Add(sw.ElapsedMilliseconds);
                writer.WriteLine($"E2E sample page {p}: {sw.ElapsedMilliseconds}ms");
            }
            samplesE2E.Sort();
            writer.WriteLine($"E2E result: p50={Percentile(samplesE2E,50)} p90={Percentile(samplesE2E,90)} mean={samplesE2E.Average()}");
        }

        writer.WriteLine("Prefetch tuner finished.");
        return 0;
    }

    static double Percentile(List<long> sortedSamples, double percentile)
    {
        if (sortedSamples == null || sortedSamples.Count == 0) return 0;
        double realIndex = percentile / 100.0 * (sortedSamples.Count - 1);
        int idx = (int)realIndex;
        double frac = realIndex - idx;
        if (idx + 1 < sortedSamples.Count)
            return sortedSamples[idx] * (1 - frac) + sortedSamples[idx + 1] * frac;
        else
            return sortedSamples[idx];
    }
}
#endif
