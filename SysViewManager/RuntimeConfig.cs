// =============================================================
// RuntimeConfig — %AppData%\SysViewManager\runtime_config.json
// Migration automatique depuis l'ancien API/runtime_config.json.
// =============================================================
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SysViewManager;

public sealed class RuntimeConfig
{
    private readonly string _path;
    private readonly object _mu = new();

    // ─── Valeurs courantes ────────────────────────────────────────────────────
    public double Lat                { get; private set; } = 50.73;
    public double Lon                { get; private set; } = 3.13;
    public string City               { get; private set; } = "HALLUIN";
    public int    WeatherIntervalMin { get; private set; } = 10;
    public string NetworkIface       { get; private set; } = "auto";
    public bool   LhmEnabled         { get; private set; } = true;
    /// <summary>Modèle météo ("best_match" → auto-résolution AROME si France).</summary>
    public string WeatherModel       { get; private set; } = "best_match";

    /// <param name="appDataDir">%AppData%\SysViewManager\</param>
    public RuntimeConfig(string appDataDir)
    {
        _path = Path.Combine(appDataDir, "runtime_config.json");

        // Migration depuis l'ancien emplacement API/runtime_config.json
        MigrateFromOldPath(appDataDir);

        Load();
    }

    // ─── Mise à jour partielle ────────────────────────────────────────────────

    public void Update(double? lat = null, double? lon = null, string? city = null,
                       int? intervalMin = null, string? netIface = null,
                       bool? lhmEnabled = null, string? weatherModel = null)
    {
        lock (_mu)
        {
            if (lat          != null) Lat               = lat.Value;
            if (lon          != null) Lon               = lon.Value;
            if (city         != null) City              = city;
            if (intervalMin  != null) WeatherIntervalMin = Math.Clamp(intervalMin.Value, 1, 60);
            if (netIface     != null && netIface is "auto" or "eth" or "wifi") NetworkIface = netIface;
            if (lhmEnabled   != null) LhmEnabled        = lhmEnabled.Value;
            if (weatherModel != null && WeatherService.WEATHER_MODELS.ContainsKey(weatherModel))
                WeatherModel = weatherModel;
        }
        Save();
    }

    // ─── Snapshot thread-safe ─────────────────────────────────────────────────

    public (double Lat, double Lon, string City, int IntervalMin, string NetIface, bool LhmEnabled) Snapshot()
    {
        lock (_mu) return (Lat, Lon, City, WeatherIntervalMin, NetworkIface, LhmEnabled);
    }

    // ─── Persistance ─────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = JsonNode.Parse(File.ReadAllText(_path, System.Text.Encoding.UTF8));
            if (json == null) return;
            if (json["lat"]  is JsonNode la) Lat  = la.GetValue<double>();
            if (json["lon"]  is JsonNode lo) Lon  = lo.GetValue<double>();
            if (json["city"] is JsonNode ci && ci.GetValue<string>() is { Length: > 0 } c)
                City = c;
            if (json["weather_interval_min"] is JsonNode wi)
                WeatherIntervalMin = Math.Clamp(wi.GetValue<int>(), 1, 60);
            if (json["network_iface"] is JsonNode ni)
            {
                var v = ni.GetValue<string>();
                if (v is "auto" or "eth" or "wifi") NetworkIface = v;
            }
            if (json["weather_model"] is JsonNode wm)
            {
                var v = wm.GetValue<string>();
                if (v != null && WeatherService.WEATHER_MODELS.ContainsKey(v)) WeatherModel = v;
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            double lat; double lon; string city; int intv; string iface; string model;
            lock (_mu)
            {
                lat   = Lat; lon = Lon; city = City;
                intv  = WeatherIntervalMin; iface = NetworkIface; model = WeatherModel;
            }
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var obj  = new
            {
                lat,
                lon,
                city,
                weather_interval_min = intv,
                network_iface        = iface,
                weather_model        = model,
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(obj, opts), System.Text.Encoding.UTF8);
        }
        catch { }
    }

    // ─── Migration depuis API/runtime_config.json ─────────────────────────────

    private void MigrateFromOldPath(string appDataDir)
    {
        if (File.Exists(_path)) return; // déjà migré

        // Chercher l'ancien fichier (API/ dans un dossier parent quelconque)
        try
        {
            // Ex : si l'exe est dans SysView V6\SysViewManager\... on remonte
            var exeDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(exeDir, "API", "runtime_config.json"),
                Path.Combine(exeDir, "..", "API", "runtime_config.json"),
                Path.Combine(exeDir, "..", "..", "API", "runtime_config.json"),
            };
            foreach (var old in candidates)
            {
                if (!File.Exists(old)) continue;
                File.Copy(old, _path, overwrite: false);
                // Ne pas supprimer l'ancien — robocopy le fera lors d'une mise à jour
                break;
            }
        }
        catch { }
    }
}
