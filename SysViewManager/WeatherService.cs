// =============================================================
// WeatherService — thread météo via Aether + Open-Meteo
// Équivalent de weather_loop() + _aether_configure() + _aether_geocode()
// du bridge Python.
// =============================================================
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SysViewManager;

public sealed class WeatherData
{
    public double? Temp, FeelsLike, Humidity, Uv, Precip, Wind;
    public int?    PrecipProb, WindDir, WeatherCode;
    public int?    Aqi, Pm10, Pm25;
    public double? Pollen;
    public string? AqiLabel = "—", PollenLabel = "—", AetherModel;
}

public sealed class WeatherService : IDisposable
{
    private const string AETHER_URL     = "http://127.0.0.1:8001";
    private const string GEOCODING_URL  = "https://geocoding-api.open-meteo.com/v1/search";
    private const string OPEN_METEO_URL = "https://api.open-meteo.com/v1/forecast";

    private readonly RuntimeConfig _cfg;
    private readonly HttpClient    _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private WeatherData           _data = new();
    private readonly object       _mu   = new();

    private readonly ManualResetEventSlim _trigger = new(false);
    private readonly Thread _thread;
    private volatile bool   _running = true;
    private bool _configured;

    public WeatherService(RuntimeConfig cfg)
    {
        _cfg    = cfg;
        _thread = new Thread(Loop) { IsBackground = true, Name = "weather" };
        _thread.Start();
    }

    // ─── Lecture ──────────────────────────────────────────────────────────────

    public WeatherData GetData() { lock (_mu) return _data; }

    public void TriggerRefresh() => _trigger.Set();

    // ─── Configuration d'Aether (lat/lon + paramètres Open-Meteo) ────────────

    public async Task ConfigureAetherAsync(double lat, double lon, string? city = null)
    {
        var payload = new
        {
            latitude   = lat,
            longitude  = lon,
            city,
            weather_params = new[]
            {
                "temperature_2m", "apparent_temperature", "relative_humidity_2m",
                "precipitation", "weather_code", "cloud_cover",
                "wind_speed_10m", "wind_direction_10m", "uv_index",
            },
            air_quality_params = new[]
            {
                "european_aqi", "pm10", "pm2_5",
                "grass_pollen", "birch_pollen", "alder_pollen", "ragweed_pollen",
            },
        };
        try
        {
            var body = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _http.PostAsync($"{AETHER_URL}/api/config", body);
        }
        catch { }
    }

    // ─── Géocodage via Open-Meteo Geocoding API ───────────────────────────────

    public async Task<(double Lat, double Lon, string City)?> GeocodeAsync(string query)
    {
        try
        {
            var url  = $"{GEOCODING_URL}?name={Uri.EscapeDataString(query)}&count=1&language=fr&format=json";
            var resp = await _http.GetStringAsync(url);
            var json = JsonNode.Parse(resp);
            var results = json?["results"]?.AsArray();
            if (results == null || results.Count == 0) return null;
            var first = results[0]!;
            double lat  = first["latitude"]!.GetValue<double>();
            double lon  = first["longitude"]!.GetValue<double>();
            string name = first["name"]?.GetValue<string>() ?? query;
            return (lat, lon, name);
        }
        catch { return null; }
    }

    // ─── Thread météo ─────────────────────────────────────────────────────────

    private void Loop()
    {
        Thread.Sleep(5000);  // attendre qu'Aether démarre et lie le port 8001

        int fail = 0;

        while (_running)
        {
            _trigger.Reset();
            try
            {
                var (lat, lon, city, intervalMin, _, _) = _cfg.Snapshot();

                // Configuration initiale d'Aether (une seule fois au démarrage)
                if (!_configured)
                {
                    ConfigureAetherAsync(lat, lon, city).GetAwaiter().GetResult();
                    _configured = true;
                }

                bool ok = false;
                try
                {
                    var resp = _http.GetStringAsync($"{AETHER_URL}/api/live_data").GetAwaiter().GetResult();
                    var json = JsonNode.Parse(resp);
                    var cu   = json?["weather"]?["current"];
                    var aq   = json?["air_quality"]?["current"];

                    double? temp       = N<double>(cu?["temperature_2m"]);
                    double? feelsLike  = N<double>(cu?["apparent_temperature"]);
                    double? humidity   = N<double>(cu?["relative_humidity_2m"]);
                    double? precip     = N<double>(cu?["precipitation"]);
                    int?    code       = N<int>(cu?["weather_code"]);
                    double? wind       = N<double>(cu?["wind_speed_10m"]);
                    int?    windDir    = N<int>(cu?["wind_direction_10m"]);
                    double? uv         = N<double>(cu?["uv_index"]);
                    int?    precipProb = N<int>(cu?["precipitation_probability"]);

                    // Fallback probabilité de précipitation (AROME ne l'inclut pas en current)
                    if (precipProb == null)
                    {
                        try
                        {
                            var url   = $"{OPEN_METEO_URL}?latitude={lat}&longitude={lon}" +
                                        $"&current=precipitation_probability&timezone=auto&format=json";
                            var resp2 = _http.GetStringAsync(url).GetAwaiter().GetResult();
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

                    string aqiLabel = aqi == null       ? "—"
                        : aqi <= 20 ? "Bon"    : aqi <= 40 ? "Correct"
                        : aqi <= 60 ? "Modere" : aqi <= 80 ? "Mauvais" : "Tres mauvais";

                    string pollenLabel = pollen == null   ? "—"
                        : pollen == 0   ? "Nul"    : pollen < 20  ? "Faible"
                        : pollen < 75   ? "Modere" : pollen < 150 ? "Eleve" : "Tres eleve";

                    string? model = json?["sources"]?["weather"]?["name_en"]?.GetValue<string>();

                    lock (_mu) _data = new WeatherData
                    {
                        Temp        = temp,
                        FeelsLike   = feelsLike,
                        Humidity    = humidity,
                        Uv          = uv,
                        Precip      = precip.HasValue ? Math.Round(precip.Value, 1) : null,
                        PrecipProb  = precipProb,
                        Wind        = wind.HasValue   ? Math.Round(wind.Value,   1) : null,
                        WindDir     = windDir,
                        WeatherCode = code,
                        Aqi         = aqi,
                        AqiLabel    = aqiLabel,
                        Pollen      = pollen,
                        PollenLabel = pollenLabel,
                        Pm10        = pm10,
                        Pm25        = pm25,
                        AetherModel = model,
                    };

                    fail = 0;
                    ok   = true;
                }
                catch { fail++; }

                int delay = ok
                    ? intervalMin * 60
                    : Math.Min(30 * (int)Math.Pow(2, Math.Min(fail - 1, 4)), intervalMin * 60);

                if (_running) _trigger.Wait(TimeSpan.FromSeconds(Math.Max(delay, 30)));
            }
            catch { if (_running) Thread.Sleep(30_000); }
        }
    }

    // ─── Helper nullable JSON ─────────────────────────────────────────────────

    private static T? N<T>(JsonNode? node) where T : struct
    {
        if (node is null) return null;
        try { return node.GetValue<T>(); } catch { return null; }
    }

    public void Dispose()
    {
        _running = false;
        _trigger.Set();
        _http.Dispose();
    }
}
