// =============================================================
// TmdbService — poster images via The Movie Database API v3
//
// Optionnel : activé uniquement si "tmdb_api_key" est défini
// dans %AppData%\SysViewManager\runtime_config.json.
//
// Clé API gratuite :
//   https://www.themoviedb.org/settings/api
//   → "Créer → Application de développeur" (gratuit, instantané)
//   → copier la valeur "API Key (v3 auth)" dans runtime_config.json :
//     { "tmdb_api_key": "votre_clé_ici" }
//
// Fonctionnement :
//   ▸ Recherche multi (film + série) par titre nettoyé
//   ▸ Retourne l'URL du poster TMDB (https://image.tmdb.org/…)
//   ▸ Cache mémoire en-process pour éviter les appels répétés
//   ▸ Silencieux si non configuré ou en cas d'erreur réseau
// =============================================================

using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace SysViewManager;

public sealed class TmdbService : IDisposable
{
    // Résolution w342 : 342 px de large — suffisant pour une vignette wallpaper
    private const string IMAGE_BASE = "https://image.tmdb.org/t/p/w342";
    private const string SEARCH_URL  = "https://api.themoviedb.org/3/search/multi";

    private readonly HttpClient _http;
    // Cache mémoire : titre → URL poster (ou "" si introuvable)
    private readonly ConcurrentDictionary<string, string> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    private string _apiKey = "";

    public TmdbService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "SysViewManager/6");
        _http.Timeout = TimeSpan.FromSeconds(8);
    }

    // ─── Configuration ────────────────────────────────────────────────────────

    /// <summary>Initialise la clé API. Appelé au démarrage depuis RuntimeConfig.</summary>
    public void Configure(string apiKey)
    {
        _apiKey = apiKey?.Trim() ?? "";
        if (!string.IsNullOrEmpty(_apiKey))
            Logger.Info("TMDB", "Service activé — clé API configurée");
        else
            Logger.Debug("TMDB", "Service non configuré (tmdb_api_key absent du runtime_config.json)");
    }

    /// <summary>True si une clé API est définie.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    // ─── Recherche de poster ──────────────────────────────────────────────────

    /// <summary>
    /// Cherche un film ou une série par titre et retourne l'URL complète du poster.
    /// Retourne "" si introuvable ou en cas d'erreur.
    /// </summary>
    public async Task<string> GetPosterAsync(string title)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(title)) return "";

        // Cache hit
        if (_cache.TryGetValue(title, out var cached))
        {
            Logger.Debug("TMDB", $"  Cache : \"{title}\" → {(string.IsNullOrEmpty(cached) ? "(sans poster)" : "poster")}");
            return cached;
        }

        try
        {
            var url = $"{SEARCH_URL}"
                    + $"?query={Uri.EscapeDataString(title)}"
                    + $"&api_key={_apiKey}"
                    + $"&language=fr-FR"
                    + $"&include_adult=false";

            var json     = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var results  = doc.RootElement.GetProperty("results");

            string posterUrl = "";
            for (int i = 0; i < results.GetArrayLength(); i++)
            {
                var r = results[i];
                if (r.TryGetProperty("poster_path", out var pp)
                    && pp.ValueKind == JsonValueKind.String
                    && pp.GetString() is { Length: > 0 } path)
                {
                    posterUrl = IMAGE_BASE + path;

                    // Log du titre TMDB trouvé (movie → "title", tv → "name")
                    string? found = null;
                    if (r.TryGetProperty("title", out var t)) found = t.GetString();
                    else if (r.TryGetProperty("name",  out var n)) found = n.GetString();

                    Logger.Info("TMDB", $"  Poster pour \"{title}\" → \"{found ?? "?"}\"  {posterUrl}");
                    break;
                }
            }

            _cache[title] = posterUrl;

            if (string.IsNullOrEmpty(posterUrl))
                Logger.Debug("TMDB", $"  Aucun poster pour \"{title}\"");

            return posterUrl;
        }
        catch (Exception ex)
        {
            Logger.Warn("TMDB", $"  Erreur recherche \"{title}\" : {ex.GetType().Name}: {ex.Message}");
            _cache[title] = "";   // évite de retenter à chaque update
            return "";
        }
    }

    // ─── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        _http.Dispose();
    }
}
