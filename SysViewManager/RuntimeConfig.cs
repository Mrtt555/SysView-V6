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
        Logger.Info("Config", $"Fichier config : {_path}");

        // Migration depuis l'ancien emplacement API/runtime_config.json
        MigrateFromOldPath(appDataDir);

        Load();
    }

    // ─── Mise à jour partielle ────────────────────────────────────────────────

    public void Update(double? lat = null, double? lon = null, string? city = null,
                       int? intervalMin = null, string? netIface = null,
                       bool? lhmEnabled = null, string? weatherModel = null)
    {
        var changes = new List<string>();
        lock (_mu)
        {
            if (lat != null && lat.Value != Lat)
                { Lat = lat.Value; changes.Add($"lat={Lat}"); }
            if (lon != null && lon.Value != Lon)
                { Lon = lon.Value; changes.Add($"lon={Lon}"); }
            if (city != null && city != City)
                { City = city; changes.Add($"city={City}"); }
            if (intervalMin != null)
            {
                var v = Math.Clamp(intervalMin.Value, 1, 60);
                if (v != WeatherIntervalMin) { WeatherIntervalMin = v; changes.Add($"interval={v}min"); }
            }
            if (netIface != null && netIface is "auto" or "eth" or "wifi" && netIface != NetworkIface)
                { NetworkIface = netIface; changes.Add($"net={NetworkIface}"); }
            if (lhmEnabled != null && lhmEnabled.Value != LhmEnabled)
                { LhmEnabled = lhmEnabled.Value; changes.Add($"lhm={LhmEnabled}"); }
            if (weatherModel != null && WeatherService.WEATHER_MODELS.ContainsKey(weatherModel)
                && weatherModel != WeatherModel)
                { WeatherModel = weatherModel; changes.Add($"modèle={WeatherModel}"); }
        }

        if (changes.Count > 0)
            Logger.Info("Config", $"Mise à jour : {string.Join(" | ", changes)}");
        else
            Logger.Debug("Config", "Update() appelé sans changement effectif");

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
        if (!File.Exists(_path))
        {
            Logger.Info("Config", "Fichier absent — valeurs par défaut utilisées");
            Logger.Info("Config", $"  lat={Lat} lon={Lon} ville={City} intervalle={WeatherIntervalMin}min modèle={WeatherModel} réseau={NetworkIface}");
            return;
        }

        try
        {
            var json = JsonNode.Parse(File.ReadAllText(_path, System.Text.Encoding.UTF8));
            if (json == null) { Logger.Warn("Config", "Fichier JSON invalide — valeurs par défaut"); return; }

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

            Logger.Info("Config", "Config chargée :");
            Logger.Info("Config", $"  ville={City}  lat={Lat}  lon={Lon}");
            Logger.Info("Config", $"  météo : intervalle={WeatherIntervalMin}min  modèle={WeatherModel}");
            Logger.Info("Config", $"  réseau={NetworkIface}  lhm={LhmEnabled}");
        }
        catch (Exception ex)
        {
            Logger.Error("Config", "Erreur lecture config — valeurs par défaut utilisées", ex);
        }
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
            Logger.Debug("Config", $"Sauvegardé → {_path}");
        }
        catch (Exception ex)
        {
            Logger.Error("Config", "Erreur sauvegarde config", ex);
        }
    }

    // ─── Migration depuis API/runtime_config.json ─────────────────────────────

    private void MigrateFromOldPath(string appDataDir)
    {
        if (File.Exists(_path)) return; // déjà migré

        try
        {
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
                Logger.Info("Config", $"Migration depuis l'ancien chemin : {old}");
                break;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Config", $"Migration échouée : {ex.Message}");
        }
    }
}
