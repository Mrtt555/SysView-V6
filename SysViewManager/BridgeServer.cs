// =============================================================
// BridgeServer — ASP.NET Core Minimal API port 5001
// Remplace intégralement SysViewBridge.pyw (Python/FastAPI).
// API identique : /v1/health  /v1/perf  /v1/weather
//                 /v1/media   /v1/status  POST /v1/config
//
// Aether est désormais intégré directement dans WeatherService.
// Plus de subprocess Python — tout tourne en in-process.
// =============================================================
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly double          _startTime;

    // Compteurs de requêtes
    private long _totalRequests = 0;
    private long _rateLimitHits = 0;

    public BridgeServer(HardwareService hw, DiskService disk, WeatherService weather,
                        MediaState media, RuntimeConfig cfg)
    {
        _hw        = hw;
        _disk      = disk;
        _weather   = weather;
        _media     = media;
        _cfg       = cfg;
        _startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Logger.Info("Bridge", $"Initialisation du serveur ASP.NET Core sur http://127.0.0.1:{PORT}...");

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());

        // Kestrel — port 5001 uniquement en loopback
        builder.WebHost.UseUrls($"http://127.0.0.1:{PORT}");

        // Silence les logs Kestrel dans la console (app silencieuse)
        builder.Logging.ClearProviders();

        // ── CORS ──────────────────────────────────────────────────────────────
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
                o.PermitLimit    = 900;   // 3 moniteurs × ~240 req/min = 720 max
                o.Window         = TimeSpan.FromMinutes(1);
                o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                o.QueueLimit     = 0;
            });
            opt.AddFixedWindowLimiter("config", o => {
                o.PermitLimit = 60;
                o.Window      = TimeSpan.FromMinutes(1);
                o.QueueLimit  = 0;
            });
            opt.AddFixedWindowLimiter("ext", o => {
                o.PermitLimit = 600;  // ~10 req/s — extension envoie 1/s par onglet
                o.Window      = TimeSpan.FromMinutes(1);
                o.QueueLimit  = 0;
            });
            opt.OnRejected = async (ctx, _) => {
                System.Threading.Interlocked.Increment(ref _rateLimitHits);
                var ip = ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
                Logger.Warn("Bridge", $"Rate limit 429 — {ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path} depuis {ip}  (total hits: {_rateLimitHits})");
                ctx.HttpContext.Response.StatusCode = 429;
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many requests", retry_after = 60 });
            };
        });

        var app = builder.Build();

        // ── Middleware 1 : logging de chaque requête ───────────────────────────
        // GET /v1/perf et /v1/weather : DEBUG (très fréquent — Wallpaper Engine)
        // Tout le reste : INFO
        app.Use(async (ctx, next) =>
        {
            var sw  = Stopwatch.StartNew();
            await next(ctx);
            sw.Stop();

            var method = ctx.Request.Method;
            var path   = ctx.Request.Path.Value ?? "";
            var status = ctx.Response.StatusCode;
            var ms     = sw.ElapsedMilliseconds;
            var count  = System.Threading.Interlocked.Increment(ref _totalRequests);

            // Chemins à haute fréquence → DEBUG seulement
            bool highFreq = path is "/v1/perf" or "/v1/weather" or "/v1/media";
            if (highFreq)
                Logger.Debug("HTTP", $"{method} {path} → {status} ({ms}ms)  [#{count}]");
            else
                Logger.Info("HTTP", $"{method} {path} → {status} ({ms}ms)  [#{count}]");
        });

        // ── Middleware 2 : rendu HTML pour les navigateurs ────────────────────
        // Brave/Chrome en dark mode affiche le JSON en texte sombre sur fond noir.
        app.Use(async (ctx, next) =>
        {
            bool isBrowser = ctx.Request.Headers.Accept.ToString().Contains("text/html");
            if (!isBrowser) { await next(ctx); return; }

            var origBody = ctx.Response.Body;
            using var buf = new MemoryStream();
            ctx.Response.Body = buf;

            await next(ctx);

            buf.Position = 0;
            var body = await new StreamReader(buf).ReadToEndAsync();
            ctx.Response.Body = origBody;

            if (ctx.Response.ContentType?.StartsWith("application/json") == true)
            {
                string pretty = body;
                try {
                    var el = JsonSerializer.Deserialize<JsonElement>(body);
                    pretty = JsonSerializer.Serialize(el, new JsonSerializerOptions { WriteIndented = true });
                } catch { }

                var html = BrowserHtml(ctx.Request.Path, pretty);
                ctx.Response.ContentType   = "text/html; charset=utf-8";
                ctx.Response.ContentLength = null;
                ctx.Response.Headers.Remove("Content-Length");
                await ctx.Response.WriteAsync(html);
            }
            else
            {
                await origBody.WriteAsync(buf.ToArray());
            }
        });

        // Access-Control-Allow-Private-Network (requis par Chromium/CEF — réseau local)
        // DOIT être enregistré AVANT UseCors : les preflight OPTIONS sont court-circuités
        // par UseCors (il ne rappelle pas next), donc tout middleware après est ignoré.
        // OnStarting() garantit que le header est présent sur TOUTES les réponses,
        // y compris les preflights gérés par CORS.
        app.Use(async (ctx, next) => {
            ctx.Response.OnStarting(() => {
                ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
                return Task.CompletedTask;
            });
            await next(ctx);
        });

        app.UseCors();
        app.UseRateLimiter();

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
                cpu  = new { name  = snap.CpuName,  usage = snap.CpuUsage, temp = snap.CpuTemp },
                gpu  = new { name  = snap.GpuName,  usage = snap.GpuUsage, temp = snap.GpuTemp },
                ram  = new { usage = snap.RamUsage, used_mb = snap.RamUsedMb, total_mb = snap.RamTotalMb },
                vram = new { used_mb = snap.VramUsed, total_mb = snap.VramTotal },
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

            JsonElement? forecastEl = null;
            if (w.ForecastJson is { Length: > 0 } fj)
            {
                try { forecastEl = JsonSerializer.Deserialize<JsonElement>(fj); } catch { }
            }

            return Results.Json(new {
                om_temp          = w.Temp,
                om_feels_like    = w.FeelsLike,
                om_humidity      = w.Humidity,
                om_uv            = w.Uv,
                om_precip        = w.Precip,
                om_precip_prob   = w.PrecipProb,
                om_wind          = w.Wind,
                om_wind_gusts    = w.WindGusts,
                om_wind_dir      = w.WindDir,
                om_weather_code  = w.WeatherCode,
                om_cloud_cover   = w.CloudCover,
                om_aqi           = w.Aqi,
                om_aqi_label     = w.AqiLabel,
                om_pollen        = w.Pollen,
                om_pollen_label  = w.PollenLabel,
                om_pm10          = w.Pm10,
                om_pm25          = w.Pm25,
                aether_model     = w.AetherModel,
                weather_model_id = w.WeatherModelId,
                forecast         = (object?)forecastEl,
            });
        }).RequireRateLimiting("api");

        // ─────────────────────────────────────────────────────────────────────
        // GET /v1/media
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/v1/media", (HttpContext _) =>
        {
            var m = _media.Get();
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
                title       = m.Title,
                artist      = m.Artist,
                platform    = m.Platform,
                media_type  = m.MediaType,
                source      = m.Source,
                playing     = m.Playing,
                position    = pos,
                duration    = m.Duration,
                thumb_url   = m.ThumbUrl,
                last_update = m.LastUpdate,
            });
        }).RequireRateLimiting("api");

        // ─────────────────────────────────────────────────────────────────────
        // POST /v1/media/ext  (extension navigateur Chrome/Brave/Edge)
        // Corps JSON : { type, title, artist, artwork, service, host,
        //               position, duration, playing }
        // ─────────────────────────────────────────────────────────────────────
        app.MapPost("/v1/media/ext", async (HttpContext ctx) =>
        {
            try
            {
                var json = await JsonNode.ParseAsync(ctx.Request.Body);
                if (json == null) return Results.BadRequest(new { error = "Corps JSON manquant" });

                string type = json["type"]?.GetValue<string>() ?? "";

                if (type == "no_media")
                {
                    _media.ClearExt();
                    return Results.Ok(new { ok = true });
                }

                string title    = json["title"]?.GetValue<string>()    ?? "";
                string artist   = json["artist"]?.GetValue<string>()   ?? "";
                string service  = json["service"]?.GetValue<string>()  ?? "";
                string host     = json["host"]?.GetValue<string>()     ?? "";
                bool   playing  = json["playing"]?.GetValue<bool>()    ?? false;
                int    position = json["position"]?.GetValue<int>()    ?? 0;
                int    duration = json["duration"]?.GetValue<int>()    ?? 0;
                string artwork  = json["artwork"]?.GetValue<string>()  ?? "";

                _media.UpdateFromExt(title, artist, service, host, playing, position, duration, artwork);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                Logger.Warn("Bridge", $"POST /v1/media/ext erreur : {ex.Message}");
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireRateLimiting("ext");

        // ─────────────────────────────────────────────────────────────────────
        // GET /v1/status
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/v1/status", (HttpContext _) =>
        {
            var snap = _hw.GetSnapshot();
            var w    = _weather.GetData();
            var m    = _media.Get();
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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
                total_requests  = _totalRequests,
                rate_limit_hits = _rateLimitHits,
                modules = new {
                    lhm        = snap.LhmOnline  ? "ok" : "offline",
                    weather    = w.Temp.HasValue ? "ok" : "pending",
                    model      = w.WeatherModelId ?? _cfg.WeatherModel,
                    model_name = w.AetherModel ?? "—",
                },
                endpoints = new {
                    health  = "ok",
                    perf    = "ok",
                    weather = w.Temp.HasValue ? "ok" : "pending",
                    media   = mediaState,
                },
            });
        }).RequireRateLimiting("config");

        // ─────────────────────────────────────────────────────────────────────
        // POST /v1/config  (Wallpaper Engine / extension Chrome)
        // ─────────────────────────────────────────────────────────────────────
        app.MapPost("/v1/config", async (HttpContext ctx) =>
        {
            try
            {
                var d       = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
                var changed = new List<string>();

                Logger.Info("Bridge", $"POST /v1/config reçu");

                // ── Ville → géocodage asynchrone ──────────────────────────
                if (d.TryGetProperty("city", out var cp) && cp.GetString() is { Length: > 0 } city)
                {
                    changed.Add("city");
                    Logger.Info("Bridge", $"  city=\"{city}\" → géocodage asynchrone...");
                    _cfg.Update(city: city);
                    _ = Task.Run(async () => {
                        var geo = await _weather.GeocodeAsync(city);
                        if (geo.HasValue)
                        {
                            Logger.Info("Bridge", $"  Géocodage résolu : {geo.Value.City} ({geo.Value.Lat}, {geo.Value.Lon})");
                            _cfg.Update(lat: geo.Value.Lat, lon: geo.Value.Lon, city: geo.Value.City);
                            _weather.TriggerRefresh();
                        }
                    });
                }

                if (d.TryGetProperty("weather_interval_min", out var wi))
                {
                    Logger.Info("Bridge", $"  weather_interval_min={wi.GetInt32()}");
                    _cfg.Update(intervalMin: wi.GetInt32());
                    changed.Add("weather_interval_min");
                    _weather.TriggerRefresh();
                }

                if (d.TryGetProperty("network_iface", out var ni))
                {
                    Logger.Info("Bridge", $"  network_iface={ni.GetString()}");
                    _cfg.Update(netIface: ni.GetString());
                    changed.Add("network_iface");
                }

                if (d.TryGetProperty("lhm_enabled", out var le))
                {
                    Logger.Info("Bridge", $"  lhm_enabled={le.GetBoolean()}");
                    _cfg.Update(lhmEnabled: le.GetBoolean());
                    changed.Add("lhm_enabled");
                }

                // ── Modèle météo ──────────────────────────────────────────
                if (d.TryGetProperty("weather_model", out var wm) && wm.GetString() is { } wmv)
                {
                    Logger.Info("Bridge", $"  weather_model={wmv}");
                    _cfg.Update(weatherModel: wmv);
                    changed.Add("weather_model");
                    _weather.TriggerRefresh();
                }

                if (changed.Count > 0)
                    Logger.Info("Bridge", $"  Config mise à jour : {string.Join(", ", changed)}");
                else
                    Logger.Debug("Bridge", "  POST /v1/config sans changement connu");

                return Results.Json(new { ok = true, updated = changed });
            }
            catch (Exception ex)
            {
                Logger.Error("Bridge", "POST /v1/config — erreur", ex);
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        }).RequireRateLimiting("config");

        // ─────────────────────────────────────────────────────────────────────
        // GET /v1/models  — liste les modèles météo disponibles
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/v1/models", (HttpContext _) =>
        {
            var current = _cfg.WeatherModel;
            var resolved = WeatherService.ResolveModel(current, _cfg.Lat, _cfg.Lon);
            var list = WeatherService.WEATHER_MODELS.Select(kv => new {
                id          = kv.Key,
                name        = kv.Value.Name,
                name_en     = kv.Value.NameEn,
                provider    = kv.Value.Provider,
                region      = kv.Value.Region,
                selected    = kv.Key == current,
                active      = kv.Key == resolved,
            });
            return Results.Json(new { current, resolved, models = list });
        }).RequireRateLimiting("api");

        Logger.Info("Bridge", $"Démarrage du serveur HTTP sur http://127.0.0.1:{PORT}");
        Logger.Info("Bridge", "Endpoints : GET /v1/health  /v1/perf  /v1/weather  /v1/media  /v1/status  /v1/models");
        Logger.Info("Bridge", "Endpoints : POST /v1/config");

        await app.RunAsync(ct);

        Logger.Info("Bridge", "Serveur HTTP arrêté");
    }

    // ─── Page HTML dark-theme pour les navigateurs ────────────────────────────

    private static string BrowserHtml(string path, string json)
    {
        var jsonJs = JsonSerializer.Serialize(json);

        return $$"""
            <!DOCTYPE html>
            <html lang="fr">
            <head>
            <meta charset="UTF-8">
            <title>SysView Bridge — {{path}}</title>
            <style>
            *{box-sizing:border-box;margin:0;padding:0}
            body{background:#0d1117;color:#c9d1d9;font-family:'Consolas','Monaco',monospace;padding:24px;min-height:100vh}
            h1{color:#58a6ff;font-size:13px;font-weight:400;letter-spacing:2px;text-transform:uppercase;margin-bottom:16px}
            nav{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:20px}
            nav a{color:#8b949e;text-decoration:none;font-size:11px;padding:3px 10px;border:1px solid #30363d;border-radius:12px;transition:all .15s}
            nav a:hover,nav a.cur{background:#21262d;color:#c9d1d9;border-color:#58a6ff}
            pre{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:20px;font-size:13px;line-height:1.65;overflow:auto;white-space:pre-wrap;word-break:break-all}
            .k{color:#79c0ff}.s{color:#a5d6ff}.n{color:#ffa657}.b{color:#ff7b72}.z{color:#6e7681;font-style:italic}
            </style>
            </head>
            <body>
            <h1>◈ SysView Bridge &mdash; {{path}}</h1>
            <nav>
              <a href="/v1/health">health</a>
              <a href="/v1/status">status</a>
              <a href="/v1/perf">perf</a>
              <a href="/v1/weather">weather</a>
              <a href="/v1/media">media</a>
              <a href="/v1/models">models</a>
            </nav>
            <pre id="out"></pre>
            <script>
            (function(){
              var raw={{jsonJs}};
              function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');}
              function hi(s){
                return s.replace(/("(?:\\u[0-9a-fA-F]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(?:true|false)\b|\bnull\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g,function(m){
                  if(/^"/.test(m)) return '<span class="'+(/:$/.test(m)?'k':'s')+'">'+esc(m)+'</span>';
                  if(/true|false/.test(m)) return '<span class="b">'+m+'</span>';
                  if(/null/.test(m)) return '<span class="z">'+m+'</span>';
                  return '<span class="n">'+m+'</span>';
                });
              }
              document.getElementById('out').innerHTML=hi(esc(raw));
              var cur='{{path}}';
              document.querySelectorAll('nav a').forEach(function(a){
                if(a.getAttribute('href')===cur)a.classList.add('cur');
              });
            })();
            </script>
            </body>
            </html>
            """;
    }
}
