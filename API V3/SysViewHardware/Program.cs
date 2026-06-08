// ============================================================
// SysViewHardware v1.0
// Service HTTP passif — capteurs matériel via LibreHardwareMonitorLib
//
//   GET http://127.0.0.1:8086/data.json   → snapshot JSON
//   GET http://127.0.0.1:8086/health      → {"status":"ok","version":"1.0"}
//
// Clés JSON identiques à la sortie de _lhm_parse() dans SysViewBridge
// → aucune modification de hardware_loop() nécessaire.
//
// Droits : requireAdministrator (app.manifest)
//   → nécessaire pour temp CPU (MSR), temp GPU (ADL / NVML)
// ============================================================

using System.Net;
using System.Text;
using System.Globalization;
using LibreHardwareMonitor.Hardware;

// ─── Snapshot immuable ────────────────────────────────────────────────────────

sealed class Snap
{
    public string? cpu_name, gpu_name;
    public float?  cpu_temp, cpu_usage;
    public float?  gpu_temp, gpu_usage;
    public float?  vram_used, vram_total;   // MB
    public float?  ram_usage;               // %
    public int     ram_used_mb, ram_total_mb; // MiB (depuis LHM Data sensors)
    // Clés réseau identiques aux attentes du bridge (WiFi = net_dl_kb / net_ul_kb)
    public double  net_dl_kb,     net_ul_kb;      // Wi-Fi  KB/s
    public double  net_eth_dl_kb, net_eth_ul_kb;  // Ethernet KB/s
    // Disques (DriveInfo — espace système de fichiers, valeurs en GiB)
    public List<DiskEntry> disks = new();
}

// ─── Entrée disque ────────────────────────────────────────────────────────────

sealed class DiskEntry
{
    public string letter  = "";
    public double used_gb, total_gb, free_gb;
    public float  percent;
}

// ─── Visitor LHM ─────────────────────────────────────────────────────────────

sealed class Updater : IVisitor
{
    public void VisitComputer(IComputer c)   => c.Traverse(this);
    public void VisitHardware(IHardware h)
    {
        h.Update();
        foreach (var sub in h.SubHardware) sub.Accept(this);
    }
    public void VisitSensor(ISensor s)       { }
    public void VisitParameter(IParameter p) { }
}

// ─── Programme principal ─────────────────────────────────────────────────────

class Program
{
    const int    PORT    = 8086;
    const int    POLL_MS = 1000;
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    static Snap          _snap    = new();
    static readonly object _mu   = new();
    static readonly Updater _vis = new();

    static void Main()
    {
        // ── Ouverture du matériel ─────────────────────────────────────────────
        var hw = new Computer
        {
            IsCpuEnabled     = true,
            IsGpuEnabled     = true,
            IsMemoryEnabled  = true,
            IsNetworkEnabled = true,
        };

        try   { hw.Open(); }
        catch { return; }   // droits insuffisants — le bridge détectera l'absence

        // ── Premier poll synchrone (évite réponse vide au démarrage) ─────────
        Poll(hw);

        // ── Thread de polling en arrière-plan ─────────────────────────────────
        var t = new Thread(() =>
        {
            for (;;) { Thread.Sleep(POLL_MS); Poll(hw); }
        })
        { IsBackground = true, Name = "hw-poll" };
        t.Start();

        // ── Serveur HTTP ──────────────────────────────────────────────────────
        var lis = new HttpListener();
        lis.Prefixes.Add($"http://127.0.0.1:{PORT}/");

        try { lis.Start(); }
        catch { hw.Close(); Environment.Exit(1); }   // port déjà occupé

        AppDomain.CurrentDomain.ProcessExit += (_, _) => { hw.Close(); lis.Stop(); };

        while (lis.IsListening)
        {
            try
            {
                var ctx = lis.GetContext();
                ThreadPool.QueueUserWorkItem(_ => Serve(ctx));
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    // ─── Lecture des capteurs ─────────────────────────────────────────────────

    static void Poll(Computer hw)
    {
        try
        {
            hw.Accept(_vis);
            var s = new Snap();

            foreach (var h in hw.Hardware)
            {
                var sens = Flat(h);  // itère capteurs + sous-hardware

                switch (h.HardwareType)
                {
                    // ── CPU ───────────────────────────────────────────────────
                    case HardwareType.Cpu:
                        s.cpu_name = h.Name;
                        foreach (var x in sens)
                        {
                            string n = x.Name.ToLowerInvariant();

                            // Température : priorité Package/Tdie/Tctl/Die, valeur > 0 obligatoire
                            // (LHM peut retourner 0.0 si le driver MSR/SMU n'est pas encore prêt)
                            if (x.SensorType == SensorType.Temperature && s.cpu_temp == null
                                && (x.Value ?? 0f) > 0f
                                && (n.Contains("package") || n.Contains("tdie")
                                    || n.Contains("tctl")  || n.Contains("cpu die")
                                    || n.Contains("core (t")))
                                s.cpu_temp = x.Value;

                            // Charge CPU globale
                            if (x.SensorType == SensorType.Load && s.cpu_usage == null
                                && n.Contains("total"))
                                s.cpu_usage = x.Value;
                        }
                        // Fallbacks : premier capteur > 0 du bon type
                        s.cpu_temp  ??= FirstPositive(sens, SensorType.Temperature);
                        s.cpu_usage ??= First(sens, SensorType.Load);
                        break;

                    // ── GPU (AMD / Nvidia / Intel Arc) ────────────────────────
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuIntel:
                        if (s.gpu_name != null) break;   // premier GPU seulement
                        s.gpu_name = h.Name;
                        foreach (var x in sens)
                        {
                            string n = x.Name.ToLowerInvariant();

                            if (x.SensorType == SensorType.Temperature && s.gpu_temp == null
                                && (n.Contains("core") || n.Contains("gpu")))
                                s.gpu_temp = x.Value;

                            if (x.SensorType == SensorType.Load && s.gpu_usage == null
                                && (n.Contains("core") || n.Contains("gpu")))
                                s.gpu_usage = x.Value;

                            // VRAM : SensorType.SmallData = MB
                            if (x.SensorType == SensorType.SmallData && s.vram_used == null
                                && n.Contains("used") && n.Contains("memory"))
                                s.vram_used = x.Value;

                            if (x.SensorType == SensorType.SmallData && s.vram_total == null
                                && n.Contains("total") && n.Contains("memory"))
                                s.vram_total = x.Value;
                        }
                        s.gpu_temp  ??= First(sens, SensorType.Temperature);
                        s.gpu_usage ??= First(sens, SensorType.Load);
                        break;

                    // ── RAM ───────────────────────────────────────────────────
                    case HardwareType.Memory:
                        s.ram_usage = First(sens, SensorType.Load);
                        // RAM absolue via SensorType.Data : "Memory Used" / "Memory Available" (GiB)
                        float? ramUsed = null, ramAvail = null;
                        foreach (var x in sens)
                        {
                            if (x.SensorType != SensorType.Data) continue;
                            string n = x.Name.ToLowerInvariant();
                            if (ramUsed  == null && n.Contains("memory") && n.Contains("used")
                                                 && !n.Contains("virtual")) ramUsed  = x.Value;
                            if (ramAvail == null && n.Contains("memory") && n.Contains("available")
                                                 && !n.Contains("virtual")) ramAvail = x.Value;
                        }
                        if (ramUsed  != null) s.ram_used_mb  = (int)(ramUsed.Value  * 1024f);
                        if (ramUsed  != null && ramAvail != null)
                            s.ram_total_mb = (int)((ramUsed.Value + ramAvail.Value) * 1024f);
                        break;

                    // ── Réseau ────────────────────────────────────────────────
                    case HardwareType.Network:
                    {
                        // Détection Wi-Fi vs Ethernet par le nom du matériel
                        string hn   = h.Name.ToLowerInvariant();
                        bool   wifi = hn.Contains("wi-fi") || hn.Contains("wifi")
                                   || hn.Contains("wireless") || hn.Contains("wlan");

                        foreach (var x in sens)
                        {
                            if (x.SensorType != SensorType.Throughput) continue;

                            // LHM Throughput = B/s → on divise par 1000 pour KB/s
                            // (cohérent avec le bridge qui attend des KB/s)
                            double kbps = (double)(x.Value ?? 0f) / 1000.0;
                            string sn   = x.Name.ToLowerInvariant();
                            bool   dl   = sn.Contains("download") || sn.Contains("receive");
                            bool   ul   = sn.Contains("upload")   || sn.Contains("send");

                            if (wifi)
                            {
                                if (dl) s.net_dl_kb     += kbps;
                                else if (ul) s.net_ul_kb += kbps;
                            }
                            else
                            {
                                if (dl) s.net_eth_dl_kb     += kbps;
                                else if (ul) s.net_eth_ul_kb += kbps;
                            }
                        }
                        break;
                    }
                }
            }

            // ── Disques C: à H: (espace système de fichiers via DriveInfo) ──────────
            foreach (var di in DriveInfo.GetDrives())
            {
                try
                {
                    if (di.DriveType != DriveType.Fixed || !di.IsReady) continue;
                    char lc = char.ToLowerInvariant(di.Name[0]);
                    if (lc < 'c' || lc > 'h') continue;   // C: → H: uniquement
                    double total = di.TotalSize          / 1_073_741_824.0;  // GiB → Go
                    double free  = di.AvailableFreeSpace / 1_073_741_824.0;
                    double used  = total - free;
                    s.disks.Add(new DiskEntry
                    {
                        letter   = lc.ToString(),
                        used_gb  = Math.Round(used,  2),
                        total_gb = Math.Round(total, 2),
                        free_gb  = Math.Round(free,  2),
                        percent  = total > 0 ? MathF.Round((float)(used / total * 100.0), 1) : 0f,
                    });
                }
                catch { /* lecteur inaccessible ou non prêt — ignoré */ }
            }

            // Publication atomique du snapshot
            lock (_mu) _snap = s;
        }
        catch { /* erreur capteur — ignorée, bridge détectera l'absence */ }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Itère à plat sur tous les capteurs d'un hardware et de ses sous-hardware.</summary>
    static IEnumerable<ISensor> Flat(IHardware h)
    {
        foreach (var s in h.Sensors) yield return s;
        foreach (var sub in h.SubHardware)
            foreach (var s in Flat(sub))
                yield return s;
    }

    /// <summary>Valeur du premier capteur du type demandé, ou null.</summary>
    static float? First(IEnumerable<ISensor> sensors, SensorType type)
    {
        foreach (var s in sensors)
            if (s.SensorType == type) return s.Value;
        return null;
    }

    /// <summary>Valeur du premier capteur du type demandé dont la valeur est > 0, ou null.</summary>
    static float? FirstPositive(IEnumerable<ISensor> sensors, SensorType type)
    {
        foreach (var s in sensors)
            if (s.SensorType == type && (s.Value ?? 0f) > 0f) return s.Value;
        return null;
    }

    // ─── Serveur HTTP ─────────────────────────────────────────────────────────

    static void Serve(HttpListenerContext ctx)
    {
        try
        {
            // CORS — SysViewBridge (localhost) et Wallpaper Engine (null origin)
            ctx.Response.Headers["Access-Control-Allow-Origin"]  = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET";

            if (ctx.Request.HttpMethod == "OPTIONS")
            { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

            string path = ctx.Request.Url?.AbsolutePath ?? "/";

            // ── GET /health ───────────────────────────────────────────────────
            if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            { WriteJson(ctx, 200, "{\"status\":\"ok\",\"version\":\"1.0\"}"); return; }

            // ── GET /data.json ────────────────────────────────────────────────
            if (!path.Equals("/data.json", StringComparison.OrdinalIgnoreCase))
            { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }

            Snap s;
            lock (_mu) s = _snap;

            // Helpers de formatage avec culture invariante (point décimal)
            string F(float? v)  => v.HasValue ? v.Value.ToString("F1", IC) : "null";
            string D(double v)  => Math.Round(v, 1).ToString("F1", IC);
            string J(string? v) => v == null ? "null"
                : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"")
                          .Replace("\r", "").Replace("\n", "") + "\"";

            // JSON avec les clés exactement attendues par SysViewBridge
            string json =
                "{\n" +
                $"  \"cpu_name\":      {J(s.cpu_name)},\n" +
                $"  \"cpu_temp\":      {F(s.cpu_temp)},\n" +
                $"  \"cpu_usage\":     {F(s.cpu_usage)},\n" +
                $"  \"gpu_name\":      {J(s.gpu_name)},\n" +
                $"  \"gpu_temp\":      {F(s.gpu_temp)},\n" +
                $"  \"gpu_usage\":     {F(s.gpu_usage)},\n" +
                $"  \"vram_used\":     {F(s.vram_used)},\n" +
                $"  \"vram_total\":    {F(s.vram_total)},\n" +
                $"  \"ram_usage\":     {F(s.ram_usage)},\n" +
                $"  \"ram_used_mb\":   {s.ram_used_mb},\n" +
                $"  \"ram_total_mb\":  {s.ram_total_mb},\n" +
                $"  \"net_dl_kb\":     {D(s.net_dl_kb)},\n" +
                $"  \"net_ul_kb\":     {D(s.net_ul_kb)},\n" +
                $"  \"net_eth_dl_kb\": {D(s.net_eth_dl_kb)},\n" +
                $"  \"net_eth_ul_kb\": {D(s.net_eth_ul_kb)},\n" +
                $"  \"disks\":         {DiskJson(s.disks)},\n" +
                "  \"online\":        true\n" +
                "}";

            WriteJson(ctx, 200, json);
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { /* ignoré */ }
        }
    }

    // ─── Sérialisation des disques ────────────────────────────────────────────

    static string DiskJson(List<DiskEntry> disks)
    {
        if (disks.Count == 0) return "{}";
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < disks.Count; i++)
        {
            var d = disks[i];
            sb.Append($"    \"{d.letter}\": {{")
              .Append($"\"used_gb\":{d.used_gb.ToString("F2", IC)},")
              .Append($"\"total_gb\":{d.total_gb.ToString("F2", IC)},")
              .Append($"\"free_gb\":{d.free_gb.ToString("F2", IC)},")
              .Append($"\"percent\":{d.percent.ToString("F1", IC)}}}");
            if (i < disks.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        return sb.Append("  }").ToString();
    }

    static void WriteJson(HttpListenerContext ctx, int status, string body)
    {
        byte[] buf = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode      = status;
        ctx.Response.ContentType     = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = buf.Length;
        try   { ctx.Response.OutputStream.Write(buf, 0, buf.Length); }
        finally { try { ctx.Response.Close(); } catch { /* ignoré */ } }
    }
}
