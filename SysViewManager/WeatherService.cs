// =============================================================
// WeatherService — intégration directe d'Aether
// Remplace le sous-processus Python par des appels Open-Meteo natifs.
//
// Logique portée depuis Aether/main.py :
//   - _resolve_model()        → ResolveModel()
//   - _build_live_response()  → FetchLiveAsync() (3 appels parallèles)
//   - WEATHER_MODELS          → dictionnaire statique
//   - /api/config  (write)    → RuntimeConfig.Update()
//   - /api/live_data          → GetData() + GetFullResponse()
// =============================================================
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SysViewManager;

// ── Données météo parsées ─────────────────────────────────────────────────────

public sealed class WeatherData
{
    // Météo courante
    public double? Temp, FeelsLike, Humidity, Uv, Precip, Wind, WindGusts;
    public int?    PrecipProb, WindDir, WeatherCode, CloudCover;

    // Qualité de l'air
    public int?    Aqi, Pm10, Pm25;
    public double? Pollen;
    public string  AqiLabel    = "—";
    public string  PollenLabel = "—";

    // Modèle météo
    public string? AetherModel;    // nom lisible du modèle effectif
    public string? WeatherModelId; // identifiant technique

    // Prévisions horaires brutes (JSON string — 48 h)
    public string? ForecastJson;
}

// ── Service ───────────────────────────────────────────────────────────────────

public sealed class WeatherService : IDisposable
{
    // ── URLs Open-Meteo ───────────────────────────────────────────────────────
    private const string OM_FORECAST   = "https://api.open-meteo.com/v1/forecast";
    private const string OM_AIR        = "https://air-quality-api.open-meteo.com/v1/air-quality";
    private const string OM_GEOCODING  = "https://geocoding-api.open-meteo.com/v1/search";

    // ── Paramètres par défaut (union Aether DEFAULT_CONFIG + bridge params) ───
    private static readonly string[] DEFAULT_WEATHER_PARAMS = new[]
    {
        "temperature_2m", "apparent_temperature", "relative_humidity_2m",
        "precipitation", "precipitation_probability", "weather_code",
        "cloud_cover", "wind_speed_10m", "wind_direction_10m",
        "wind_gusts_10m", "uv_index",
    };

    private static readonly string[] DEFAULT_AIR_PARAMS = new[]
    {
        "european_aqi", "pm10", "pm2_5",
        "grass_pollen", "birch_pollen", "alder_pollen", "ragweed_pollen",
    };

    // ── Modèles météo disponibles (depuis Aether WEATHER_MODELS) ─────────────
    public static readonly IReadOnlyDictionary<string, (string Name, string NameEn, string Provider, string Region)>
        WEATHER_MODELS = new Dictionary<string, (string, string, string, string)>
    {
        ["best_match"]               = ("Sélection automatique",               "Automatic selection",              "Open-Meteo (ECMWF · DWD · Météo-France · NOAA GFS…)", "Mondial"),
        ["ecmwf_ifs025"]             = ("ECMWF IFS",                           "ECMWF IFS",                       "ECMWF",          "Mondial"),
        ["meteofrance_seamless"]     = ("Météo-France (ARPEGE + AROME)",       "Météo-France (ARPEGE + AROME)",   "Météo-France",   "Europe / France"),
        ["meteofrance_arome_france"] = ("Météo-France AROME France (1.3 km)",  "Météo-France AROME France",       "Météo-France",   "France métropolitaine"),
        ["dwd_icon_seamless"]        = ("DWD ICON",                            "DWD ICON",                        "Deutscher Wetterdienst", "Europe centrale"),
        ["dwd_icon_eu"]              = ("DWD ICON-EU (7 km)",                  "DWD ICON-EU (7 km)",              "Deutscher Wetterdienst", "Europe"),
        ["gfs_seamless"]             = ("NOAA GFS",                            "NOAA GFS",                        "NOAA",           "Mondial"),
        ["ukmo_seamless"]            = ("UK Met Office",                       "UK Met Office",                   "Met Office",     "Europe NW"),
    };

    private readonly RuntimeConfig _cfg;
    private readonly HttpClient    _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private WeatherData   _data = new();
    private readonly object _mu  = new();

    // Annulation de l'attente entre deux rafraîchissements
    private CancellationTokenSource _waitCts = new();
    private volatile bool           _running = true;
    private readonly Task           _loopTask;

    // Export JSON
    private readonly string _dataDir;
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public WeatherService(RuntimeConfig cfg, string dataDir = "")
    {
        _cfg      = cfg;
        _dataDir  = dataDir;
        _loopTask = Task.Run(LoopAsync);
    }

    // ─── Lecture ──────────────────────────────────────────────────────────────

    public WeatherData GetData() { lock (_mu) return _data; }

    public void TriggerRefresh()
    {
        _waitCts.Cancel();        // réveille le délai en cours
        _waitCts = new CancellationTokenSource();
    }

    // ─── Géocodage ────────────────────────────────────────────────────────────

    public async Task<(double Lat, double Lon, string City)?> GeocodeAsync(string query)
    {
        try
        {
            var url  = $"{OM_GEOCODING}?name={Uri.EscapeDataString(query)}&count=1&language=fr&format=json";
            var resp = await _http.GetStringAsync(url);
            var json = JsonNode.Parse(resp);
            var results = json?["results"]?.AsArray();
            if (results == null || results.Count == 0) return null;
            var first = results[0]!;
            return (first["latitude"]!.GetValue<double>(),
                    first["longitude"]!.GetValue<double>(),
                    first["name"]?.GetValue<string>() ?? query);
        }
        catch { return null; }
    }

    // ─── Résolution de modèle (portée depuis Aether _resolve_model) ──────────

    public static string ResolveModel(string modelId, double lat, double lon)
    {
        if (modelId != "best_match") return modelId;
        // France métropolitaine (mainland + Corse) → AROME 1.3 km
        if (lat >= 41.3 && lat <= 51.1 && lon >= -5.2 && lon <= 9.6)
            return "meteofrance_arome_france";
        return "best_match";
    }

    // ─── Boucle principale (async) ────────────────────────────────────────────

    private async Task LoopAsync()
    {
        await Task.Delay(3000);  // laisser le reste de l'app s'initialiser

        int fail = 0;

        while (_running)
        {
            try
            {
                var (lat, lon, city, intervalMin, _, _) = _cfg.Snapshot();
                string modelCfg = _cfg.WeatherModel;
                string modelId  = ResolveModel(modelCfg, lat, lon);

                bool ok = false;
                try
                {
                    var data = await FetchLiveAsync(lat, lon, modelId);
                    lock (_mu) _data = data;
                    WriteWeatherJson(data);
                    fail = 0;
                    ok   = true;
                }
                catch { fail++; }

                // Délai : intervalle normal si ok, backoff exponentiel sinon
                int delay = ok
                    ? intervalMin * 60
                    : Math.Min(30 * (int)Math.Pow(2, Math.Min(fail - 1, 4)), intervalMin * 60);

                try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(delay, 30)), _waitCts.Token); }
                catch (OperationCanceledException) { /* refresh déclenché manuellement */ }
            }
            catch { await Task.Delay(30_000); }
        }
    }

    // ─── 3 appels parallèles Open-Meteo (= _build_live_response d'Aether) ────

    private async Task<WeatherData> FetchLiveAsync(double lat, double lon, string modelId)
    {
        // Appels parallèles : météo courante + prévisions 48h + qualité de l'air
        var tWeather  = FetchCurrentAsync(lat, lon, modelId);
        var tForecast = FetchForecastAsync(lat, lon, modelId);
        var tAir      = FetchAirAsync(lat, lon);
        await Task.WhenAll(tWeather, tForecast, tAir);

        var cu = tWeather.Result?["current"];
        var aq = tAir.Result?["current"];

        double? temp       = N<double>(cu?["temperature_2m"]);
        double? feelsLike  = N<double>(cu?["apparent_temperature"]);
        double? humidity   = N<double>(cu?["relative_humidity_2m"]);
        double? precip     = N<double>(cu?["precipitation"]);
        int?    code       = N<int>(cu?["weather_code"]);
        double? wind       = N<double>(cu?["wind_speed_10m"]);
        double? windGusts  = N<double>(cu?["wind_gusts_10m"]);
        int?    windDir    = N<int>(cu?["wind_direction_10m"]);
        double? uv         = N<double>(cu?["uv_index"]);
        int?    cloudCover = N<int>(cu?["cloud_cover"]);
        int?    precipProb = N<int>(cu?["precipitation_probability"]);

        // Fallback precipitation_probability (AROME ne la fournit pas en current)
        if (precipProb == null)
        {
            try
            {
                var url   = $"{OM_FORECAST}?latitude={G(lat)}&longitude={G(lon)}" +
                            "&current=precipitation_probability&timezone=auto&format=json";
                var resp2 = await _http.GetStringAsync(url);
                precipProb = N<int>(JsonNode.Parse(resp2)?["current"]?["precipitation_probability"]);
            }
            catch { }
        }

        int?    aqi    = N<int>(aq?["european_aqi"]);
        double? grass  = N<double>(aq?["grass_pollen"]);
        double? birch  = N<double>(aq?["birch_pollen"]);
        double? alder  = N<double>(aq?["alder_pollen"]);
        double? ragweed= N<double>(aq?["ragweed_pollen"]);
        int?    pm10   = N<int>(aq?["pm10"]);
        int?    pm25   = N<int>(aq?["pm2_5"]);

        double? pollen = (grass == null && birch == null && alder == null && ragweed == null)
            ? null
            : Math.Round((grass ?? 0) + (birch ?? 0) + (alder ?? 0) + (ragweed ?? 0), 1);

        string aqiLabel = aqi == null ? "—"
            : aqi <= 20 ? "Bon" : aqi <= 40 ? "Correct"
            : aqi <= 60 ? "Modere" : aqi <= 80 ? "Mauvais" : "Tres mauvais";

        string pollenLabel = pollen == null ? "—"
            : pollen == 0 ? "Nul" : pollen < 20 ? "Faible"
            : pollen < 75 ? "Modere" : pollen < 150 ? "Eleve" : "Tres eleve";

        var modelInfo = WEATHER_MODELS.GetValueOrDefault(modelId, WEATHER_MODELS["best_match"]);

        return new WeatherData
        {
            Temp        = temp,
            FeelsLike   = feelsLike,
            Humidity    = humidity,
            Uv          = uv,
            Precip      = precip.HasValue ? Math.Round(precip.Value, 1) : null,
            PrecipProb  = precipProb,
            Wind        = wind.HasValue    ? Math.Round(wind.Value,    1) : null,
            WindGusts   = windGusts.HasValue ? Math.Round(windGusts.Value, 1) : null,
            WindDir     = windDir,
            WeatherCode = code,
            CloudCover  = cloudCover,
            Aqi         = aqi,
            AqiLabel    = aqiLabel,
            Pollen      = pollen,
            PollenLabel = pollenLabel,
            Pm10        = pm10,
            Pm25        = pm25,
            AetherModel  = modelInfo.NameEn,
            WeatherModelId = modelId,
            ForecastJson = tForecast.Result?.ToJsonString(),
        };
    }

    private async Task<JsonNode?> FetchCurrentAsync(double lat, double lon, string modelId)
    {
        var url = $"{OM_FORECAST}?latitude={G(lat)}&longitude={G(lon)}" +
                  $"&current={string.Join(",", DEFAULT_WEATHER_PARAMS)}" +
                  $"&models={modelId}&timezone=auto&forecast_days=1";
        return JsonNode.Parse(await _http.GetStringAsync(url));
    }

    private async Task<JsonNode?> FetchForecastAsync(double lat, double lon, string modelId)
    {
        var url = $"{OM_FORECAST}?latitude={G(lat)}&longitude={G(lon)}" +
                  "&hourly=temperature_2m,precipitation,weather_code,wind_speed_10m" +
                  $"&models={modelId}&timezone=auto&forecast_days=2";
        return JsonNode.Parse(await _http.GetStringAsync(url));
    }

    private async Task<JsonNode?> FetchAirAsync(double lat, double lon)
    {
        var url = $"{OM_AIR}?latitude={G(lat)}&longitude={G(lon)}" +
                  $"&current={string.Join(",", DEFAULT_AIR_PARAMS)}&timezone=auto";
        return JsonNode.Parse(await _http.GetStringAsync(url));
    }

    // ─── Export Weather.json ──────────────────────────────────────────────────

    private void WriteWeatherJson(WeatherData w)
    {
        if (string.IsNullOrEmpty(_dataDir)) return;
        try
        {
            // Prévisions brutes → JsonElement pour inclure directement
            System.Text.Json.JsonElement? forecastEl = null;
            if (w.ForecastJson is { Length: > 0 } fj)
            {
                try { forecastEl = JsonSerializer.Deserialize<System.Text.Json.JsonElement>(fj); }
                catch { }
            }

            var obj = new
            {
                timestamp          = DateTime.UtcNow.ToString("o"),
                temp               = w.Temp,
                feels_like         = w.FeelsLike,
                humidity           = w.Humidity,
                uv                 = w.Uv,
                precip             = w.Precip,
                precip_prob        = w.PrecipProb,
                wind               = w.Wind,
                wind_gusts         = w.WindGusts,
                wind_dir           = w.WindDir,
                weather_code       = w.WeatherCode,
                cloud_cover        = w.CloudCover,
                aqi                = w.Aqi,
                aqi_label          = w.AqiLabel,
                pollen             = w.Pollen,
                pollen_label       = w.PollenLabel,
                pm10               = w.Pm10,
                pm25               = w.Pm25,
                weather_model      = w.WeatherModelId,
                weather_model_name = w.AetherModel,
                forecast           = (object?)forecastEl,
            };

            var json = JsonSerializer.Serialize(obj, _jsonOpts);
            File.WriteAllText(
                Path.Combine(_dataDir, "Weather.json"),
                json, System.Text.Encoding.UTF8);
        }
        catch { }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Formate un double avec la culture invariante (point comme séparateur décimal).
    /// OBLIGATOIRE pour les URLs Open-Meteo — sinon la locale fr-FR produit "50,78628"
    /// au lieu de "50.78628", ce qui cause une erreur 400 "Latitude out of range".
    /// </summary>
    private static string G(double v) => v.ToString("G", CultureInfo.InvariantCulture);

    private static T? N<T>(JsonNode? node) where T : struct
    {
        if (node is null) return null;
        try { return node.GetValue<T>(); } catch { return null; }
    }

    public void Dispose()
    {
        _running = false;
        _waitCts.Cancel();
        _http.Dispose();
    }
}
