// ============================================================
// SysViewHardware v1.0
// Service HTTP passif — capteurs matériel via LibreHardwareMonitorLib
//
//   GET http://127.0.0.1:8086/data.json   → snapshot JSON
//   GET http://127.0.0.1:8086/health      → {"status":"ok","version":"1.0"}
//
// Droits : requireAdministrator (app.manifest)
//   → nécessaire pour temp CPU/GPU (PawnIO driver, LHM 0.9.6+)
// ============================================================

using System.Net;
using System.Text;
using System.Globalization;
using LibreHardwareMonitor.Hardware;

// ─── Snapshot ────────────────────────────────────────────────────────────────

sealed class Snap
{
    public string? cpu_name, gpu_name;
    public float?  cpu_temp, cpu_usage;
    public float?  gpu_temp, gpu_usage;
    public float?  vram_used, vram_total;   // MB
    public float?  ram_usage;               // %
    public int     ram_used_mb, ram_total_mb;
    public double  net_dl_kb,     net_ul_kb;
    public double  net_eth_dl_kb, net_eth_ul_kb;
    public List<DiskEntry> disks = new();
}

sealed class DiskEntry
{
    public string letter    = "";
    public double used_gb, total_gb, free_gb;
    public float  percent;
    public bool   removable;   // true = USB/amovible, false = disque interne
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

    static Snap           _snap   = new();
    static readonly object _mu   = new();
    static readonly Updater _vis = new();
    static volatile bool   _running = true;

    static void Main()
    {
        var hw = new Computer
        {
            IsCpuEnabled     = true,
            IsGpuEnabled     = true,
            IsMemoryEnabled  = true,
            IsNetworkEnabled = true,
        };

        try   { hw.Open(); }
        catch (Exception ex)
        {
            try { File.WriteAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hw_error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] hw.Open() failed:\n{ex}\n"); }
            catch { }
            Environment.Exit(1);
        }

        Poll(hw);

        var t = new Thread(() =>
        {
            while (_running) { Thread.Sleep(POLL_MS); if (_running) Poll(hw); }
        })
        { IsBackground = true, Name = "hw-poll" };
        t.Start();

        var lis = new HttpListener();
        lis.Prefixes.Add($"http://127.0.0.1:{PORT}/");

        try { lis.Start(); }
        catch (Exception ex)
        {
            try { File.WriteAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hw_error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HttpListener.Start() échoué (port {PORT} déjà occupé ?):\n{ex}\n"); }
            catch { }
            hw.Close();
            Environment.Exit(1);
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => { _running = false; Thread.Sleep(50); hw.Close(); lis.Stop(); };

        while (lis.IsListening)
        {
            try
            {
                var ctx = lis.GetContext();
                ThreadPool.QueueUserWorkItem(_ => Serve(ctx));
            }
            catch (HttpListenerException)  { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    // ─── Polling ─────────────────────────────────────────────────────────────

    static void Poll(Computer hw)
    {
        try
        {
            hw.Accept(_vis);
            var s = new Snap();

            foreach (var h in hw.Hardware)
            {
                switch (h.HardwareType)
                {
                    // ── CPU ───────────────────────────────────────────────────
                    case HardwareType.Cpu:
                    {
                        s.cpu_name = h.Name;
                        var sl = Flat(h).ToList();

                        foreach (var x in sl)
                        {
                            string n = x.Name.ToLowerInvariant();

                            if (x.SensorType == SensorType.Temperature && s.cpu_temp == null
                                && (x.Value ?? 0f) > 0f
                                && (n.Contains("package")   || n.Contains("tdie")
                                    || n.Contains("tctl")   || n.Contains("cpu die")
                                    || n.Contains("core (t") || n.Contains("ccd")
                                    || n.Contains("average") || n.Contains("max")))
                                s.cpu_temp = x.Value;

                            if (x.SensorType == SensorType.Load && s.cpu_usage == null
                                && n.Contains("total"))
                                s.cpu_usage = x.Value;
                        }

                        s.cpu_temp  ??= FirstPositive(sl, SensorType.Temperature);
                        s.cpu_usage ??= First(sl, SensorType.Load);
                        break;
                    }

                    // ── GPU ───────────────────────────────────────────────────
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuIntel:
                    {
                        // Préférer GPU discret (AMD/NVIDIA) à iGPU (Intel) :
                        // si un GPU est déjà enregistré ET le nouveau est discret → écraser l'iGPU
                        bool isDiscrete = h.HardwareType != HardwareType.GpuIntel;
                        if (s.gpu_name != null && !isDiscrete) break;  // iGPU ignoré si GPU déjà présent
                        // Si l'ancien était un iGPU et qu'on trouve un GPU discret → reset des capteurs
                        if (s.gpu_name != null)
                        {
                            s.gpu_temp = null; s.gpu_usage = null;
                            s.vram_used = null; s.vram_total = null;
                        }
                        s.gpu_name = h.Name;
                        var gl = Flat(h).ToList();

                        foreach (var x in gl)
                        {
                            string n = x.Name.ToLowerInvariant();

                            if (x.SensorType == SensorType.Temperature && s.gpu_temp == null
                                && (n.Contains("core") || n.Contains("gpu")))
                                s.gpu_temp = x.Value;

                            if (x.SensorType == SensorType.Load && s.gpu_usage == null
                                && (n.Contains("core") || n.Contains("gpu")))
                                s.gpu_usage = x.Value;

                            if (x.SensorType == SensorType.SmallData && s.vram_used == null
                                && n.Contains("used") && n.Contains("memory"))
                                s.vram_used = x.Value;

                            if (x.SensorType == SensorType.SmallData && s.vram_total == null
                                && n.Contains("total") && n.Contains("memory"))
                                s.vram_total = x.Value;
                        }

                        s.gpu_temp  ??= First(gl, SensorType.Temperature);
                        s.gpu_usage ??= First(gl, SensorType.Load);
                        break;
                    }

                    // ── RAM ───────────────────────────────────────────────────
                    case HardwareType.Memory:
                    {
                        var ml = Flat(h).ToList();
                        s.ram_usage = First(ml, SensorType.Load);

                        float? ramUsed = null, ramAvail = null;
                        foreach (var x in ml)
                        {
                            if (x.SensorType != SensorType.Data) continue;
                            string n = x.Name.ToLowerInvariant();
                            if (ramUsed  == null && n.Contains("memory") && n.Contains("used")
                                                 && !n.Contains("virtual")) ramUsed  = x.Value;
                            if (ramAvail == null && n.Contains("memory") && n.Contains("available")
                                                 && !n.Contains("virtual")) ramAvail = x.Value;
                        }

                        if (ramUsed != null) s.ram_used_mb = (int)MathF.Round(ramUsed.Value * 1024f);
                        if (ramUsed != null && ramAvail != null)
                            s.ram_total_mb = (int)MathF.Round((ramUsed.Value + ramAvail.Value) * 1024f);

                        // Fallback : calcule % depuis les valeurs absolues si Load manquant
                        if (s.ram_usage == null && s.ram_total_mb > 0)
                            s.ram_usage = MathF.Round((float)s.ram_used_mb / s.ram_total_mb * 100f, 1);
                        break;
                    }

                    // ── Réseau ────────────────────────────────────────────────
                    case HardwareType.Network:
                    {
                        string hn   = h.Name.ToLowerInvariant();
                        bool   wifi = hn.Contains("wi-fi") || hn.Contains("wifi")
                                   || hn.Contains("wireless") || hn.Contains("wlan");

                        foreach (var x in Flat(h))
                        {
                            if (x.SensorType != SensorType.Throughput) continue;
                            double kbps = (double)(x.Value ?? 0f) / 1000.0;
                            string sn   = x.Name.ToLowerInvariant();
                            bool   dl   = sn.Contains("download") || sn.Contains("receive");
                            bool   ul   = sn.Contains("upload")   || sn.Contains("send");

                            if (wifi)
                            {
                                if (dl)      s.net_dl_kb += kbps;
                                else if (ul) s.net_ul_kb += kbps;
                            }
                            else
                            {
                                if (dl)      s.net_eth_dl_kb += kbps;
                                else if (ul) s.net_eth_ul_kb += kbps;
                            }
                        }
                        break;
                    }
                }
            }

            // ── Disques fixes + USB amovibles (C: → Z:, ignore A: B:) ────────
            foreach (var di in DriveInfo.GetDrives())
            {
                try
                {
                    bool isFixed     = di.DriveType == DriveType.Fixed;
                    bool isRemovable = di.DriveType == DriveType.Removable;
                    if ((!isFixed && !isRemovable) || !di.IsReady) continue;
                    char lc = char.ToLowerInvariant(di.Name[0]);
                    if (lc < 'c') continue;   // ignore A: B: (lecteurs disquette)
                    double total = di.TotalSize          / 1_073_741_824.0;
                    double free  = di.AvailableFreeSpace / 1_073_741_824.0;
                    double used  = total - free;
                    s.disks.Add(new DiskEntry
                    {
                        letter    = lc.ToString(),
                        used_gb   = Math.Round(used,  2),
                        total_gb  = Math.Round(total, 2),
                        free_gb   = Math.Round(free,  2),
                        percent   = total > 0 ? MathF.Round((float)(used / total * 100.0), 1) : 0f,
                        removable = isRemovable,
                    });
                }
                catch { }
            }

            lock (_mu) _snap = s;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hw_error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Poll() error:\n{ex}\n"); }
            catch { }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    static IEnumerable<ISensor> Flat(IHardware h)
    {
        foreach (var s in h.Sensors) yield return s;
        foreach (var sub in h.SubHardware)
            foreach (var s in Flat(sub))
                yield return s;
    }

    static float? First(IEnumerable<ISensor> sensors, SensorType type)
    {
        foreach (var s in sensors)
            if (s.SensorType == type) return s.Value;
        return null;
    }

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
            ctx.Response.Headers["Access-Control-Allow-Origin"]  = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET";

            if (ctx.Request.HttpMethod == "OPTIONS")
            { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

            string path = ctx.Request.Url?.AbsolutePath ?? "/";

            if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            { WriteJson(ctx, 200, "{\"status\":\"ok\",\"version\":\"1.0\"}"); return; }

            if (!path.Equals("/data.json", StringComparison.OrdinalIgnoreCase))
            { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }

            Snap s;
            lock (_mu) s = _snap;

            string F(float? v)  => v.HasValue ? v.Value.ToString("F1", IC) : "null";
            string D(double v)  => Math.Round(v, 1).ToString("F1", IC);
            string J(string? v)
            {
                if (v == null) return "null";
                var sb = new StringBuilder("\"");
                foreach (char c in v)
                {
                    if      (c == '\\') sb.Append("\\\\");
                    else if (c == '"' ) sb.Append("\\\"");
                    else if (c == '\n') sb.Append("\\n");
                    else if (c == '\r') sb.Append("\\r");
                    else if (c == '\t') sb.Append("\\t");
                    else if (c < ' '  ) sb.Append($"\\u{(int)c:x4}");
                    else                sb.Append(c);
                }
                sb.Append('"');
                return sb.ToString();
            }

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
        catch (Exception ex)
        {
            try { ctx.Response.Abort(); } catch { }
            try { File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hw_error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Serve() error:\n{ex}\n"); }
            catch { }
        }
    }

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
              .Append($"\"percent\":{d.percent.ToString("F1", IC)},")
              .Append($"\"removable\":{(d.removable ? "true" : "false")}}}");
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
        finally { try { ctx.Response.Close(); } catch { } }
    }
}
