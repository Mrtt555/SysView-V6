// =============================================================
// MusicArtService — pochette d'album sans clé API
//
// Chaîne de fallback (première URL non vide gagne) :
//   1. Deezer Search  — cover_xl 1000×1000, aucune clé requise
//   2. iTunes Search  — artwork 600×600,    aucune clé requise
//   3. MusicBrainz    — Cover Art Archive,  aucune clé requise
//
// Toutes les réponses sont mises en cache en mémoire (max 120 entrées).
// Le service retourne une URL directe (CDN) — le front-end charge
// l'image nativement via <img src>, pas besoin de base64.
// =============================================================

using System.Text.Json;

namespace SysViewManager;

public sealed class MusicArtService : IDisposable
{
    private readonly HttpClient _http;

    // Cache "artist|title" → url (ou "" si introuvable)
    private readonly Dictionary<string, string> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public MusicArtService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        // MusicBrainz impose un User-Agent identifiant l'application
        _http.DefaultRequestHeaders.Add(
            "User-Agent",
            "SysView/6.0 (https://github.com/Mrtt555/SysView-V6)");
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    /// <summary>
    /// Retourne une URL de poster de film/série (≥ 600 px) ou "" si introuvable.
    /// Chaîne sans clé : iTunes Movie → iTunes TV → Wikipedia.
    /// </summary>
    public async Task<string> GetPosterAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";

        var key = $"__video__|{title.Trim()}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (_cache.Count >= 120) _cache.Clear();

        var url = await TryItunesVideoAsync(title, "movie");
        if (string.IsNullOrEmpty(url))
            url = await TryItunesVideoAsync(title, "tvSeason");
        if (string.IsNullOrEmpty(url))
            url = await TryWikipediaAsync(title);

        _cache[key] = url;

        if (!string.IsNullOrEmpty(url))
            Logger.Info("MusicArt", $"Poster vidéo : \"{title}\" → {UrlShort(url)}");
        else
            Logger.Debug("MusicArt", $"Aucun poster : \"{title}\"");

        return url;
    }

    // ─── iTunes Movie / TV (sans clé) ─────────────────────────────────────────

    private async Task<string> TryItunesVideoAsync(string title, string entity)
    {
        try
        {
            var q    = Uri.EscapeDataString(title.Trim());
            var json = await _http.GetStringAsync(
                $"https://itunes.apple.com/search?term={q}&entity={entity}&limit=3");

            using var doc     = JsonDocument.Parse(json);
            var       root    = doc.RootElement;
            if (root.GetProperty("resultCount").GetInt32() == 0) return "";

            var results = root.GetProperty("results");
            for (int i = 0; i < results.GetArrayLength(); i++)
            {
                if (!results[i].TryGetProperty("artworkUrl100", out var art)) continue;
                var url = art.GetString() ?? "";
                if (!string.IsNullOrEmpty(url))
                    return url.Replace("100x100bb", "600x600bb");
            }
        }
        catch (Exception ex) { Logger.Debug("MusicArt", $"iTunes {entity}: {ex.Message}"); }
        return "";
    }

    // ─── Wikipedia REST (thumbnail de la page de l'œuvre) ────────────────────

    private async Task<string> TryWikipediaAsync(string title)
    {
        try
        {
            // L'API REST accepte espaces et casse → encodage minimal
            var q    = Uri.EscapeDataString(title.Trim());
            var json = await _http.GetStringAsync(
                $"https://en.wikipedia.org/api/rest_v1/page/summary/{q}");

            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;
            if (root.TryGetProperty("thumbnail", out var thumb) &&
                thumb.TryGetProperty("source", out var src))
            {
                var url = src.GetString() ?? "";
                if (!string.IsNullOrEmpty(url)) return url;
            }
        }
        catch (Exception ex) { Logger.Debug("MusicArt", $"Wikipedia: {ex.Message}"); }
        return "";
    }

    /// <summary>
    /// Retourne une URL CDN de pochette musicale (600-1000 px) ou "" si introuvable.
    /// Chaîne : Deezer → iTunes → MusicBrainz+CAA.
    /// </summary>
    public async Task<string> GetArtworkAsync(string artist, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";

        var key = $"{artist.Trim()}|{title.Trim()}";

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // ── Limite mémoire simple ─────────────────────────────────────────────
        if (_cache.Count >= 120) _cache.Clear();

        var url = await TryDeezerAsync(artist, title);

        if (string.IsNullOrEmpty(url))
            url = await TryItunesAsync(artist, title);

        if (string.IsNullOrEmpty(url))
            url = await TryMusicBrainzAsync(artist, title);

        _cache[key] = url;

        if (!string.IsNullOrEmpty(url))
            Logger.Info("MusicArt", $"Pochette : \"{title}\" / {artist} → {UrlShort(url)}");
        else
            Logger.Debug("MusicArt", $"Aucune pochette : \"{title}\" / {artist}");

        return url;
    }

    // ─── Deezer ───────────────────────────────────────────────────────────────

    private async Task<string> TryDeezerAsync(string artist, string title)
    {
        try
        {
            var q    = Q(artist, title);
            var json = await _http.GetStringAsync(
                $"https://api.deezer.com/search?q={q}&limit=3");

            using var doc  = JsonDocument.Parse(json);
            var       data = doc.RootElement.GetProperty("data");

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                var item = data[i];
                if (!item.TryGetProperty("album", out var album))  continue;
                if (!album.TryGetProperty("cover_xl", out var img)) continue;

                var url = img.GetString() ?? "";
                // "default_album_xxx" est le placeholder de Deezer → ignorer
                if (!string.IsNullOrEmpty(url) && !url.Contains("default_album"))
                    return url;
            }
        }
        catch (Exception ex) { Logger.Debug("MusicArt", $"Deezer : {ex.Message}"); }
        return "";
    }

    // ─── iTunes Search ────────────────────────────────────────────────────────

    private async Task<string> TryItunesAsync(string artist, string title)
    {
        try
        {
            var q    = Q(artist, title);
            var json = await _http.GetStringAsync(
                $"https://itunes.apple.com/search?term={q}&entity=musicTrack&limit=3");

            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;

            if (root.GetProperty("resultCount").GetInt32() == 0) return "";

            var results = root.GetProperty("results");
            for (int i = 0; i < results.GetArrayLength(); i++)
            {
                if (!results[i].TryGetProperty("artworkUrl100", out var art)) continue;
                var url = art.GetString() ?? "";
                if (!string.IsNullOrEmpty(url))
                    // Upgrade 100 px → 600 px (format iTMS standard)
                    return url.Replace("100x100bb", "600x600bb");
            }
        }
        catch (Exception ex) { Logger.Debug("MusicArt", $"iTunes : {ex.Message}"); }
        return "";
    }

    // ─── MusicBrainz + Cover Art Archive ─────────────────────────────────────

    private async Task<string> TryMusicBrainzAsync(string artist, string title)
    {
        try
        {
            // ── Étape 1 : trouver le releaseId via MusicBrainz ─────────────────
            var mbq = string.IsNullOrEmpty(artist)
                ? Uri.EscapeDataString($"recording:\"{title}\"")
                : Uri.EscapeDataString($"recording:\"{title}\" AND artist:\"{artist}\"");

            var mbJson = await _http.GetStringAsync(
                $"https://musicbrainz.org/ws/2/recording/?query={mbq}&limit=3&fmt=json");

            using var mbDoc = JsonDocument.Parse(mbJson);
            var recordings  = mbDoc.RootElement.GetProperty("recordings");
            if (recordings.GetArrayLength() == 0) return "";

            // Chercher un releaseId valide parmi les premiers résultats
            string? releaseId = null;
            for (int r = 0; r < recordings.GetArrayLength() && releaseId == null; r++)
            {
                var rec = recordings[r];
                if (!rec.TryGetProperty("releases", out var rels)) continue;
                for (int ri = 0; ri < rels.GetArrayLength() && releaseId == null; ri++)
                {
                    var rel = rels[ri];
                    if (rel.TryGetProperty("id", out var idEl))
                        releaseId = idEl.GetString();
                }
            }
            if (string.IsNullOrEmpty(releaseId)) return "";

            // ── Étape 2 : Cover Art Archive ────────────────────────────────────
            var caJson = await _http.GetStringAsync(
                $"https://coverartarchive.org/release/{releaseId}");

            using var caDoc = JsonDocument.Parse(caJson);
            var images = caDoc.RootElement.GetProperty("images");
            if (images.GetArrayLength() == 0) return "";

            // Préférer l'image taguée "front"
            for (int i = 0; i < images.GetArrayLength(); i++)
            {
                var img     = images[i];
                bool isFront = img.TryGetProperty("front", out var fp) && fp.GetBoolean();
                if (!isFront) continue;

                // Petite vignette CDN en priorité (rapidité)
                if (img.TryGetProperty("thumbnails", out var tb))
                {
                    foreach (var size in new[] { "small", "250", "500" })
                    {
                        if (tb.TryGetProperty(size, out var s))
                        {
                            var u = s.GetString() ?? "";
                            if (!string.IsNullOrEmpty(u)) return u;
                        }
                    }
                }
                if (img.TryGetProperty("image", out var full))
                {
                    var u = full.GetString() ?? "";
                    if (!string.IsNullOrEmpty(u)) return u;
                }
            }

            // Aucune image "front" → prendre la première disponible
            if (images[0].TryGetProperty("image", out var fallback))
                return fallback.GetString() ?? "";
        }
        catch (Exception ex) { Logger.Debug("MusicArt", $"MusicBrainz : {ex.Message}"); }
        return "";
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string Q(string artist, string title) =>
        Uri.EscapeDataString(
            string.IsNullOrWhiteSpace(artist) ? title.Trim()
                                              : $"{artist.Trim()} {title.Trim()}");

    private static string UrlShort(string url) =>
        url.Length > 72 ? url[..72] + "…" : url;

    // ─── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose() => _http.Dispose();
}
