// =============================================================
// BridgeServer — ASP.NET Core Minimal API port 5001
// Remplace intégralement SysViewBridge.pyw (Python/FastAPI).
// API identique : /v1/health  /v1/perf  /v1/weather
//                 /v1/media   /v1/status  POST /v1/config
// =============================================================
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SysViewManager;

public sealed class BridgeServer
{
    private const int PORT = 5001;

    private readonly HardwareService _hw;
    private readonly DiskService     _disk;
    private readonly WeatherService  _weather;
    private readonly MediaState      _media;
    private readonly RuntimeConfig   _cfg;
    private readonly AetherProcess   _aether;
    private readonly double          _startTime;

    public BridgeServer(HardwareService hw, DiskService disk, WeatherService weather,
                        MediaState media, RuntimeConfig cfg, AetherProcess aether)
    {
        _hw        = hw;
        _disk      = disk;
        _weather   = weather;
        _media     = media;
        _cfg       = cfg;
        _aether    = aether;
        _startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());

        // Kestrel — port 5001 uniquement en loopback
        builder.WebHost.UseUrls($"http://127.0.0.1:{PORT}");

        // Silence les logs Kestrel dans la console (app silencieuse)
        builder.Logging.ClearProviders();

        // ── CORS ──────────────────────────────────────────────────────────────
        // Mêmes origines que le bridge Python :
        //   null (Wallpaper Engine renderer), localhost, extension Chrome
        builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
            p.SetIsOriginAllowed(o =>
                   o == "null"
                || o == $"http://127.0.0.1:{PORT}"
                || o == $"http://localhost:{PORT}"
                || o.StartsWith("chrome-extension://"))
             .WithMethods("GET", "POST", "OPTIONS")
             .WithHeaders("Content-Type")));

        // ── Rate limiting ─────────────────────────────────────────────────────
        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddFixedWindowLimiter("api", o => {
                o.PermitLimit       = 350;
                o.Window            = TimeSpan.FromMinutes(1);
                o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                o.QueueLimit        = 0;
            });
            opt.AddFixedWindowLimiter("config", o => {
                o.PermitLimit = 60;
                o.Window      = TimeSpan.FromMinutes(1);
                o.QueueLimit  = 0;
            });
            opt.OnRejected = async (ctx, _) => {
                ctx.HttpContext.Response.StatusCode = 429;
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many requests", retry_after = 60 });
            };
        });

        var app = builder.Build();

        app.UseCors();
        app.UseRateLimiter();

        // Access-Control-Allow-Private-Network (requis par Chromium — réseau local)
        app.Use(async (ctx, next) => {
            await next(ctx);
            ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        });

        // ─────────────────────────────────────────────────────────────────────
        // GET /v1/health
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/v1/health", (HttpContext _) =>
            Results.Json(new { status = "online", version = "6.0" }))
           .RequireRateLimiting("api");

        // ─────────────────────────────────────────────────────────────────────
        // GET /v1/perf
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/v1/perf", (HttpContext _) =>
        {
            var snap = _hw.GetSnapshot();

            // Mettre à jour le cache disque depuis le snapshot LHM (source primaire)
            _disk.UpdateFromSnapshot(snap);
            var disks = _disk.GetDisks();

            // Sélection interface réseau selon config
            var (_, _, _, _, netIface, _) = _cfg.Snapshot();
            double dlKb, ulKb;
            switch (netIface)
            {
                case "wifi": dlKb = snap.NetDlKb;    ulKb = snap.NetUlKb;    break;
                case "eth" : dlKb = snap.NetEthDlKb; ulKb = snap.NetEthUlKb; break;
                default    : dlKb = snap.NetDlKb + snap.NetEthDlKb;
                             ulKb = snap.NetUlKb + snap.NetEthUlKb;          break;
            }

            var diskObj = disks.ToDictionary(
                kv => kv.Key,
                kv => (object)new {
                    used_gb    = kv.Value.UsedGb,
                    total_gb   = kv.Value.TotalGb,
                    free_gb    = kv.Value.FreeGb,
                    used_unit  = kv.Value.UsedUnit,
                    total_unit = kv.Value.TotalUnit,
                    free_unit  = kv.Value.FreeUnit,
                    percent    = kv.Value.Percent,
                    display    = kv.Value.Display,
                });

            return Results.Json(new {
                lhm_online = snap.LhmOnline,
                cpu = new { name  = snap.CpuName,  usage = snap.CpuUsage, temp = snap.CpuTemp },
                gpu = new { name  = snap.GpuName,  usage = snap.GpuUsage, temp = snap.GpuTemp },
                ram = new { usage = snap.RamUsage, used_mb = snap.RamUsedMb, total_mb = snap.RamTotalMb },
                vram= new { used_mb = snap.VramUsed, total_mb = snap.VramTotal },
                network = new { download_kb = Math.Round(dlKb, 1), upload_kb = Math.Round(ulKb, 1) },
                disks = diskObj,
            });
        }).RequireRateLimiting("api");

        // ─────────────────────────────────────────────────────────────────────
        // GET /v1/weather
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/v1/weather", (HttpContext _) =>
        {
            var w = _weather.GetData();
            return Results.Json(new {
                om_temp          = w.Temp,
                om_feels_like    = w.FeelsLike,
                om_humidity      = w.Humidity,
                om_uv            = w.Uv,
                om_precip        = w.Precip,
                om_precip_prob   = w.PrecipProb,
                om_wind          = w.Wind,
                om_wind_dir      = w.WindDir,
                om_weather_code  = w.WeatherCode,
                om_aqi           = w.Aqi,
                om_aqi_label     = w.AqiLabel,
                om_pollen        = w.Pollen,
                om_pollen_label  = w.PollenLabel,
                om_pm10          = w.Pm10,
                om_pm25          = w.Pm25,
                aether_model     = w.AetherModel,
            });
        }).RequireRateLimiting("api");

        // ─────────────────────────────────────────────────────────────────────
        // GET /v1/media
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/v1/media", (HttpContext _) =>
        {
            var m = _media.Get();
            // Interpolation de position : même logique que le bridge Python
            double pos = m.Position;
            if (m.Playing && m.LastUpdate > 0)
            {
                double elapsed = Math.Min(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - m.LastUpdate,
                    30.0);
                pos = m.Duration > 0
                    ? Math.Min(m.Position + elapsed, m.Duration)
                    : m.Position + elapsed;
            }
            return Results.Json(new {
                title      = m.Title,
                artist     = m.Artist,
                source     = m.Source,
                playing    = m.Playing,
                position   = pos,
                duration   = m.Duration,
                thumb_url  = m.ThumbUrl,
                last_update= m.LastUpdate,
            });
        }).RequireRateLimiting("api");

        // ─────────────────────────────────────────────────────────────────────
        // GET /v1/status
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/v1/status", (HttpContext _) =>
        {
            var snap = _hw.GetSnapshot();
            var w    = _weather.GetData();
            var m    = _media.Get();
            double now    = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double extAge = _media.ExtLastPost > 0 ? now - _media.ExtLastPost : -1;

            string Uptime(double s) {
                int si = (int)s, h = si / 3600, mn = si % 3600 / 60, sc = si % 60;
                return h > 0 ? $"{h}h {mn:D2}m" : mn > 0 ? $"{mn}m {sc:D2}s" : $"{sc}s";
            }

            string mediaState = m.Title.Length > 0 && m.Playing ? "playing"
                              : m.Title.Length > 0 ? "paused" : "idle";

            return Results.Json(new {
                name   = "SysView Bridge v6",
                uptime = Uptime(now - _startTime),
                port   = PORT,
                modules = new {
                    lhm    = snap.LhmOnline     ? "ok" : "offline",
                    aether = w.Temp.HasValue    ? "ok" : "pending",
                    model  = w.AetherModel ?? "—",
                },
                endpoints = new {
                    health    = "ok",
                    perf      = "ok",
                    weather   = w.Temp.HasValue ? "ok" : "pending",
                    aether_ui = "http://127.0.0.1:8001",
                    media     = mediaState,
                },
                extension = new {
                    active      = extAge >= 0 && extAge < 10,
                    last_seen_s = extAge >= 0 ? (object)(int)extAge : null,
                },
            });
        }).RequireRateLimiting("config");

        // ─────────────────────────────────────────────────────────────────────
        // POST /v1/media  (extension Chrome)
        // ─────────────────────────────────────────────────────────────────────
        app.MapPost("/v1/media", async (HttpContext ctx) =>
        {
            try
            {
                var d = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
                _media.Update(
                    title    : d.TryGetProperty("title",     out var t)  ? t.GetString()  ?? "" : "",
                    artist   : d.TryGetProperty("artist",    out var a)  ? a.GetString()  ?? "" : "",
                    playing  : d.TryGetProperty("playing",   out var pl) && pl.GetBoolean(),
                    position : d.TryGetProperty("position",  out var po) ? po.GetDouble() : 0.0,
                    duration : d.TryGetProperty("duration",  out var du) ? du.GetDouble() : 0.0,
                    thumbUrl : d.TryGetProperty("thumb_url", out var th) ? th.GetString() ?? "" : ""
                );
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
        }).RequireRateLimiting("api");

        // ─────────────────────────────────────────────────────────────────────
        // POST /v1/config  (Wallpaper Engine)
        // ─────────────────────────────────────────────────────────────────────
        app.MapPost("/v1/config", async (HttpContext ctx) =>
        {
            try
            {
                var d       = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
                var changed = new List<string>();

                // ── Ville → géocodage asynchrone ──────────────────────────
                if (d.TryGetProperty("city", out var cp) && cp.GetString() is { Length: > 0 } city)
                {
                    changed.Add("city");
                    _cfg.Update(city: city);
                    _ = Task.Run(async () => {
                        var geo = await _weather.GeocodeAsync(city);
                        if (geo.HasValue)
                        {
                            _cfg.Update(lat: geo.Value.Lat, lon: geo.Value.Lon, city: geo.Value.City);
                            await _weather.ConfigureAetherAsync(geo.Value.Lat, geo.Value.Lon, geo.Value.City);
                            _weather.TriggerRefresh();
                        }
                    });
                }

                if (d.TryGetProperty("weather_interval_min", out var wi))
                { _cfg.Update(intervalMin: wi.GetInt32()); changed.Add("weather_interval_min"); _weather.TriggerRefresh(); }

                if (d.TryGetProperty("network_iface", out var ni))
                { _cfg.Update(netIface: ni.GetString()); changed.Add("network_iface"); }

                if (d.TryGetProperty("lhm_enabled", out var le))
                { _cfg.Update(lhmEnabled: le.GetBoolean()); changed.Add("lhm_enabled"); }

                return Results.Json(new { ok = true, updated = changed });
            }
            catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
        }).RequireRateLimiting("config");

        await app.RunAsync(ct);
    }
}
