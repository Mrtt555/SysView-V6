// =============================================================
// RuntimeConfig — persistance de runtime_config.json
// Même fichier que le bridge Python (API/runtime_config.json).
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

    public RuntimeConfig(string apiDir)
    {
        _path = Path.Combine(apiDir, "runtime_config.json");
        Load();
    }

    // ─── Mise à jour partielle ────────────────────────────────────────────────
    public void Update(double? lat = null, double? lon = null, string? city = null,
                       int? intervalMin = null, string? netIface = null, bool? lhmEnabled = null)
    {
        lock (_mu)
        {
            if (lat        != null) Lat               = lat.Value;
            if (lon        != null) Lon               = lon.Value;
            if (city       != null) City              = city;
            if (intervalMin!= null) WeatherIntervalMin = Math.Clamp(intervalMin.Value, 1, 15);
            if (netIface   != null && netIface is "auto" or "eth" or "wifi") NetworkIface = netIface;
            if (lhmEnabled != null) LhmEnabled        = lhmEnabled.Value;
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
                WeatherIntervalMin = Math.Clamp(wi.GetValue<int>(), 1, 15);
            if (json["network_iface"] is JsonNode ni)
            {
                var v = ni.GetValue<string>();
                if (v is "auto" or "eth" or "wifi") NetworkIface = v;
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            double lat; double lon; string city; int intv; string iface;
            lock (_mu) { lat = Lat; lon = Lon; city = City; intv = WeatherIntervalMin; iface = NetworkIface; }
            var obj = new
            {
                lat,
                lon,
                city,
                weather_interval_min = intv,
                network_iface        = iface,
            };
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_path, JsonSerializer.Serialize(obj, opts), System.Text.Encoding.UTF8);
        }
        catch { }
    }
}
