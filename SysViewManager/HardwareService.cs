// =============================================================
// HardwareService — LHM polling en mémoire (CPU/GPU/RAM/Net)
// Exporte aussi vers %AppData%\SysViewManager\Hardware.json (2 s).
// =============================================================
using LibreHardwareMonitor.Hardware;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace SysViewManager;

// ── Snapshot partagé ──────────────────────────────────────────────────────────

public sealed class HardwareSnapshot
{
    public string? CpuName, GpuName;
    public float?  CpuTemp, CpuUsage;
    public float?  GpuTemp, GpuUsage;
    public float?  VramUsed, VramTotal;  // MB
    public float?  RamUsage;             // %
    public int     RamUsedMb, RamTotalMb;
    public double  NetDlKb,     NetUlKb;
    public double  NetEthDlKb, NetEthUlKb;
    public List<DiskEntry> Disks = new();
    public bool    LhmOnline;
}

public sealed class DiskEntry
{
    public string Letter    = "";
    public double UsedGb, TotalGb, FreeGb;
    public float  Percent;
    public bool   Removable;
}

// ── Service ───────────────────────────────────────────────────────────────────

public sealed class HardwareService : IDisposable
{
    private readonly Computer _hw;
    private readonly Visitor _vis = new();
    private bool _hwOpen;

    // Throughput réseau (fallback si LHM network absent)
    private long   _prevRec, _prevSent;
    private double _prevNetTime;

    private HardwareSnapshot _snap = new();
    private readonly object  _mu   = new();
    private readonly Thread  _thread;
    private volatile bool    _running = true;

    // Export JSON
    private readonly string _dataDir;
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public HardwareService(string dataDir = "")
    {
        _dataDir = dataDir;
        _hw = new Computer
        {
            IsCpuEnabled     = true,
            IsGpuEnabled     = true,
            IsMemoryEnabled  = true,
            IsNetworkEnabled = true,
        };
        try { _hw.Open(); _hwOpen = true; }
        catch { _hwOpen = false; }

        Poll();

        _thread = new Thread(() => {
            while (_running) { Thread.Sleep(500); if (_running) Poll(); }
        }) { IsBackground = true, Name = "hw-poll" };
        _thread.Start();
    }

    public HardwareSnapshot GetSnapshot() { lock (_mu) return _snap; }

    // ─── Polling ─────────────────────────────────────────────────────────────

    private void Poll()
    {
        try
        {
            var s = new HardwareSnapshot { LhmOnline = _hwOpen };

            if (_hwOpen)
            {
                _hw.Accept(_vis);
                foreach (var h in _hw.Hardware)
                    ReadHardware(h, s);
            }

            // Fallback réseau via NetworkInterface (si LHM ne l'a pas fourni)
            if (s.NetDlKb == 0 && s.NetEthDlKb == 0)
                UpdateNetFallback(s);

            // Disques via DriveInfo (LHM Network ne couvre pas les disques)
            ReadDisks(s);

            lock (_mu) _snap = s;

            // Export Hardware.json toutes les 500 ms (= chaque poll)
            WriteHardwareJson(s);
        }
        catch { }
    }

    // ─── Export Hardware.json ─────────────────────────────────────────────────

    private void WriteHardwareJson(HardwareSnapshot s)
    {
        if (string.IsNullOrEmpty(_dataDir)) return;
        try
        {
            var diskDict = s.Disks.ToDictionary(
                d => d.Letter,
                d => (object)new
                {
                    used_gb  = d.UsedGb,
                    total_gb = d.TotalGb,
                    free_gb  = d.FreeGb,
                    percent  = d.Percent,
                });

            var obj = new
            {
                timestamp  = DateTime.UtcNow.ToString("o"),
                lhm_online = s.LhmOnline,
                cpu = new
                {
                    name  = s.CpuName,
                    usage = s.CpuUsage.HasValue ? Math.Round(s.CpuUsage.Value, 1) : (double?)null,
                    temp  = s.CpuTemp.HasValue  ? Math.Round(s.CpuTemp.Value,  1) : (double?)null,
                },
                gpu = new
                {
                    name         = s.GpuName,
                    usage        = s.GpuUsage.HasValue ? Math.Round(s.GpuUsage.Value, 1) : (double?)null,
                    temp         = s.GpuTemp.HasValue  ? Math.Round(s.GpuTemp.Value,  1) : (double?)null,
                    vram_used_mb = s.VramUsed  != null ? (int?)Math.Round(s.VramUsed.Value)  : null,
                    vram_total_mb= s.VramTotal != null ? (int?)Math.Round(s.VramTotal.Value) : null,
                },
                ram = new
                {
                    usage    = s.RamUsage.HasValue ? Math.Round(s.RamUsage.Value, 1) : (double?)null,
                    used_mb  = s.RamUsedMb,
                    total_mb = s.RamTotalMb,
                },
                network = new
                {
                    download_kb = Math.Round(s.NetDlKb + s.NetEthDlKb, 1),
                    upload_kb   = Math.Round(s.NetUlKb + s.NetEthUlKb, 1),
                    wifi_dl_kb  = Math.Round(s.NetDlKb,    1),
                    wifi_ul_kb  = Math.Round(s.NetUlKb,    1),
                    eth_dl_kb   = Math.Round(s.NetEthDlKb, 1),
                    eth_ul_kb   = Math.Round(s.NetEthUlKb, 1),
                },
                disks = diskDict,
            };

            var json = JsonSerializer.Serialize(obj, _jsonOpts);
            File.WriteAllText(
                Path.Combine(_dataDir, "Hardware.json"),
                json, System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private void ReadHardware(IHardware h, HardwareSnapshot s)
    {
        switch (h.HardwareType)
        {
            // ── CPU ──────────────────────────────────────────────────────────
            case HardwareType.Cpu:
            {
                s.CpuName = h.Name;
                var sl = Flat(h).ToList();
                foreach (var x in sl)
                {
                    string n = x.Name.ToLowerInvariant();
                    if (x.SensorType == SensorType.Temperature && s.CpuTemp == null
                        && (x.Value ?? 0f) > 0f
                        && (n.Contains("package")   || n.Contains("tdie")
                            || n.Contains("tctl")   || n.Contains("cpu die")
                            || n.Contains("core (t") || n.Contains("ccd")
                            || n.Contains("average") || n.Contains("max")))
                        s.CpuTemp = x.Value;
                    if (x.SensorType == SensorType.Load && s.CpuUsage == null && n.Contains("total"))
                        s.CpuUsage = x.Value;
                }
                s.CpuTemp  ??= FirstPositive(sl, SensorType.Temperature);
                s.CpuUsage ??= First(sl, SensorType.Load);
                break;
            }

            // ── GPU ──────────────────────────────────────────────────────────
            case HardwareType.GpuAmd:
            case HardwareType.GpuNvidia:
            case HardwareType.GpuIntel:
            {
                bool isDiscrete = h.HardwareType != HardwareType.GpuIntel;
                if (s.GpuName != null && !isDiscrete) break;
                if (s.GpuName != null) { s.GpuTemp = null; s.GpuUsage = null; s.VramUsed = null; s.VramTotal = null; }
                s.GpuName = h.Name;
                var gl = Flat(h).ToList();
                foreach (var x in gl)
                {
                    string n = x.Name.ToLowerInvariant();
                    if (x.SensorType == SensorType.Temperature && s.GpuTemp == null
                        && (n.Contains("core") || n.Contains("gpu"))) s.GpuTemp = x.Value;
                    if (x.SensorType == SensorType.Load && s.GpuUsage == null
                        && (n.Contains("core") || n.Contains("gpu"))) s.GpuUsage = x.Value;
                    if (x.SensorType == SensorType.SmallData && s.VramUsed == null
                        && n.Contains("used") && n.Contains("memory")) s.VramUsed = x.Value;
                    if (x.SensorType == SensorType.SmallData && s.VramTotal == null
                        && n.Contains("total") && n.Contains("memory")) s.VramTotal = x.Value;
                }
                s.GpuTemp  ??= First(gl, SensorType.Temperature);
                s.GpuUsage ??= First(gl, SensorType.Load);
                break;
            }

            // ── RAM ──────────────────────────────────────────────────────────
            case HardwareType.Memory:
            {
                var ml = Flat(h).ToList();
                s.RamUsage = First(ml, SensorType.Load);
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
                if (ramUsed != null) s.RamUsedMb = (int)MathF.Round(ramUsed.Value * 1024f);
                if (ramUsed != null && ramAvail != null)
                    s.RamTotalMb = (int)MathF.Round((ramUsed.Value + ramAvail.Value) * 1024f);
                if (s.RamUsage == null && s.RamTotalMb > 0)
                    s.RamUsage = MathF.Round((float)s.RamUsedMb / s.RamTotalMb * 100f, 1);
                break;
            }

            // ── Réseau ───────────────────────────────────────────────────────
            case HardwareType.Network:
            {
                string hn  = h.Name.ToLowerInvariant();
                bool   wifi= hn.Contains("wi-fi") || hn.Contains("wifi")
                           || hn.Contains("wireless") || hn.Contains("wlan");
                foreach (var x in Flat(h))
                {
                    if (x.SensorType != SensorType.Throughput) continue;
                    double kbps = (double)(x.Value ?? 0f) / 1000.0;
                    string sn   = x.Name.ToLowerInvariant();
                    bool dl = sn.Contains("download") || sn.Contains("receive");
                    bool ul = sn.Contains("upload")   || sn.Contains("send");
                    if (wifi) { if (dl) s.NetDlKb += kbps; else if (ul) s.NetUlKb += kbps; }
                    else      { if (dl) s.NetEthDlKb += kbps; else if (ul) s.NetEthUlKb += kbps; }
                }
                break;
            }
        }
    }

    private void ReadDisks(HardwareSnapshot s)
    {
        foreach (var di in DriveInfo.GetDrives())
        {
            try
            {
                bool isFixed    = di.DriveType == DriveType.Fixed;
                bool isRemovable= di.DriveType == DriveType.Removable;
                if ((!isFixed && !isRemovable) || !di.IsReady) continue;
                char lc = char.ToLowerInvariant(di.Name[0]);
                if (lc < 'c') continue;
                double total = di.TotalSize          / 1_073_741_824.0;
                double free  = di.AvailableFreeSpace / 1_073_741_824.0;
                double used  = total - free;
                s.Disks.Add(new DiskEntry
                {
                    Letter    = lc.ToString(),
                    UsedGb    = Math.Round(used,  2),
                    TotalGb   = Math.Round(total, 2),
                    FreeGb    = Math.Round(free,  2),
                    Percent   = total > 0 ? MathF.Round((float)(used / total * 100.0), 1) : 0f,
                    Removable = isRemovable,
                });
            }
            catch { }
        }
    }

    private void UpdateNetFallback(HardwareSnapshot s)
    {
        try
        {
            long rec = 0, sent = 0;
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var st = iface.GetIPv4Statistics();
                rec  += st.BytesReceived;
                sent += st.BytesSent;
            }
            double now = Environment.TickCount64 / 1000.0;
            if (_prevNetTime > 0)
            {
                double dt = now - _prevNetTime;
                if (dt > 0)
                {
                    s.NetDlKb = Math.Max(0, (rec  - _prevRec)  / dt / 1e3);
                    s.NetUlKb = Math.Max(0, (sent - _prevSent) / dt / 1e3);
                }
            }
            _prevRec = rec; _prevSent = sent; _prevNetTime = now;
        }
        catch { }
    }

    // ─── Helpers LHM ─────────────────────────────────────────────────────────

    private static IEnumerable<ISensor> Flat(IHardware h)
    {
        foreach (var s in h.Sensors) yield return s;
        foreach (var sub in h.SubHardware)
            foreach (var s in Flat(sub)) yield return s;
    }

    private static float? First(IEnumerable<ISensor> sensors, SensorType type)
    {
        foreach (var s in sensors) if (s.SensorType == type) return s.Value;
        return null;
    }

    private static float? FirstPositive(IEnumerable<ISensor> sensors, SensorType type)
    {
        foreach (var s in sensors)
            if (s.SensorType == type && (s.Value ?? 0f) > 0f) return s.Value;
        return null;
    }

    public void Dispose()
    {
        _running = false;
        try { if (_hwOpen) _hw.Close(); } catch { }
    }

    // ── Visitor LHM (inner class) ─────────────────────────────────────────────
    private sealed class Visitor : IVisitor
    {
        public void VisitComputer(IComputer c)   => c.Traverse(this);
        public void VisitHardware(IHardware h)   { h.Update(); foreach (var sub in h.SubHardware) sub.Accept(this); }
        public void VisitSensor(ISensor s)       { }
        public void VisitParameter(IParameter p) { }
    }
}
