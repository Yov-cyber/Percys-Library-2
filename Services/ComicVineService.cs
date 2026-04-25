using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace ComicReader.Services
{
    /// <summary>
    /// Cliente minimo de ComicVine API para enriquecer metadatos de comics.
    ///
    /// Endpoint: https://comicvine.gamespot.com/api/
    /// Auth: query string ?api_key=...
    /// Header obligatorio: User-Agent (rechazan llamadas sin UA).
    /// Rate limit: ~200 req/hora por key.
    /// Formato: ?format=json
    ///
    /// Este servicio se mantiene deliberadamente simple: solo expone search
    /// (issue / volume) que devuelve el primer resultado parseado a un POCO
    /// liviano. La integracion superior decide cuando llamarlo (opt-in via
    /// AppSettings.EnableComicVineEnrichment + ComicVineApiKey no vacia).
    ///
    /// La app funciona 100% offline si el servicio no se invoca.
    /// </summary>
    public sealed class ComicVineService
    {
        private const string BaseUrl = "https://comicvine.gamespot.com/api";
        private const string UserAgent = "PercysLibrary/1.0 (+https://github.com/Yov-cyber/Percys-Library-2)";

        private static readonly Lazy<HttpClient> _http = new Lazy<HttpClient>(CreateHttpClient);
        private static readonly Lazy<ComicVineService> _instance = new Lazy<ComicVineService>(() => new ComicVineService());

        public static ComicVineService Instance => _instance.Value;

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return c;
        }

        public bool IsConfigured()
        {
            try
            {
                var s = SettingsManager.Settings;
                return s != null
                    && s.EnableComicVineEnrichment
                    && !string.IsNullOrWhiteSpace(s.ComicVineApiKey);
            }
            catch { return false; }
        }

        /// <summary>
        /// Busca el primer issue cuyo nombre coincida (best-effort) con el query.
        /// Devuelve null si no esta configurado, si no hay resultados, o si hay
        /// cualquier error de red/parseo. Nunca lanza.
        /// </summary>
        public async Task<ComicVineIssueSummary> SearchIssueAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            if (!IsConfigured()) return null;

            string apiKey;
            try { apiKey = SettingsManager.Settings.ComicVineApiKey.Trim(); }
            catch { return null; }
            if (string.IsNullOrWhiteSpace(apiKey)) return null;

            try
            {
                var url = $"{BaseUrl}/search/?api_key={Uri.EscapeDataString(apiKey)}"
                        + $"&format=json&resources=issue&limit=1"
                        + $"&query={Uri.EscapeDataString(query.Trim())}";

                using var resp = await _http.Value.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);

                var root = doc.RootElement;
                if (!root.TryGetProperty("results", out var results)) return null;
                if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return null;

                var first = results[0];
                return ParseIssue(first);
            }
            catch { return null; }
        }

        private static ComicVineIssueSummary ParseIssue(JsonElement el)
        {
            string GetStr(string name)
            {
                if (!el.TryGetProperty(name, out var p)) return null;
                if (p.ValueKind == JsonValueKind.String) return p.GetString();
                if (p.ValueKind == JsonValueKind.Null) return null;
                return p.ToString();
            }

            string volumeName = null;
            if (el.TryGetProperty("volume", out var vol) && vol.ValueKind == JsonValueKind.Object)
            {
                if (vol.TryGetProperty("name", out var vn) && vn.ValueKind == JsonValueKind.String)
                    volumeName = vn.GetString();
            }

            string coverUrl = null;
            if (el.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.Object)
            {
                if (img.TryGetProperty("medium_url", out var mu) && mu.ValueKind == JsonValueKind.String)
                    coverUrl = mu.GetString();
                else if (img.TryGetProperty("small_url", out var su) && su.ValueKind == JsonValueKind.String)
                    coverUrl = su.GetString();
            }

            return new ComicVineIssueSummary
            {
                Name = GetStr("name"),
                IssueNumber = GetStr("issue_number"),
                CoverDate = GetStr("cover_date"),
                StoreDate = GetStr("store_date"),
                Description = GetStr("description"),
                Deck = GetStr("deck"),
                VolumeName = volumeName,
                CoverUrl = coverUrl,
                SiteUrl = GetStr("site_detail_url")
            };
        }
    }

    public sealed class ComicVineIssueSummary
    {
        public string Name { get; set; }
        public string IssueNumber { get; set; }
        public string CoverDate { get; set; }
        public string StoreDate { get; set; }
        public string Description { get; set; }
        public string Deck { get; set; }
        public string VolumeName { get; set; }
        public string CoverUrl { get; set; }
        public string SiteUrl { get; set; }
    }
}
