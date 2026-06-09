// =============================================================
// DiskService — cache disques mis à jour depuis HardwareService
// Utilisé par BridgeServer pour /v1/perf  →  "disks": { ... }
// =============================================================
using System.Globalization;

namespace SysViewManager;

public sealed class DiskInfo
{
    public double UsedGb, TotalGb, FreeGb;
    public string UsedUnit = "Go", TotalUnit = "Go", FreeUnit = "Go";
    public double Percent;
    public string Display  = "";
    public bool   Removable;
}

public sealed class DiskService : IDisposable
{
    private static readonly CultureInfo IC  = CultureInfo.InvariantCulture;
    private const double TiB = 1_099_511_627_776.0;
    private const double GiB = 1_073_741_824.0;

    private Dictionary<string, DiskInfo> _disks = new();
    private readonly object _mu = new();

    // ─── Lecture du cache ─────────────────────────────────────────────────────

    public Dictionary<string, DiskInfo> GetDisks()
    {
        lock (_mu) return new Dictionary<string, DiskInfo>(_disks);
    }

    // ─── Mise à jour depuis le snapshot LHM (source primaire) ────────────────

    public void UpdateFromSnapshot(HardwareSnapshot snap)
    {
        if (snap.Disks.Count == 0) return;
        var d = new Dictionary<string, DiskInfo>(snap.Disks.Count);
        foreach (var e in snap.Disks)
            d[e.Letter] = Build(e.UsedGb, e.TotalGb, e.FreeGb, e.Percent, e.Removable);
        lock (_mu) _disks = d;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static DiskInfo Build(double usedGb, double totalGb, double freeGb, float pct, bool removable)
    {
        string uU = usedGb  >= 1024 ? "To" : "Go";
        string tU = totalGb >= 1024 ? "To" : "Go";
        string fU = freeGb  >= 1024 ? "To" : "Go";
        double uD = uU == "To" ? Math.Round(usedGb  / 1024, 2) : usedGb;
        double tD = tU == "To" ? Math.Round(totalGb / 1024, 2) : totalGb;
        return new DiskInfo
        {
            UsedGb   = Math.Round(usedGb,  2),
            TotalGb  = Math.Round(totalGb, 2),
            FreeGb   = Math.Round(freeGb,  2),
            UsedUnit = uU, TotalUnit = tU, FreeUnit = fU,
            Percent  = pct,
            Display  = $"{uD.ToString("F2", IC)}{uU}/{Math.Round(tD, 0).ToString("F0", IC)}{tU}",
            Removable= removable,
        };
    }

    public void Dispose() { }
}
