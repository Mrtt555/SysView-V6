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

    private Dictionary<string, DiskInfo> _disks = new();
    private HashSet<string> _lastLetters = new();
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

        var d       = new Dictionary<string, DiskInfo>(snap.Disks.Count);
        var letters = new HashSet<string>(snap.Disks.Count);

        foreach (var e in snap.Disks)
        {
            d[e.Letter] = Build(e.UsedGb, e.TotalGb, e.FreeGb, e.Percent, e.Removable);
            letters.Add(e.Letter);
        }

        // Ne loguer que si l'ensemble des lecteurs a changé
        bool drivesChanged;
        lock (_mu)
        {
            drivesChanged = !letters.SetEquals(_lastLetters);
            _lastLetters  = letters;
            _disks        = d;
        }

        if (drivesChanged)
        {
            Logger.Info("Disk", $"Lecteurs mis à jour ({d.Count}) :");
            foreach (var kv in d)
                Logger.Info("Disk", $"  {kv.Key.ToUpper()}: → {kv.Value.Display}  {kv.Value.Percent:F0}% utilisé{(kv.Value.Removable ? " [amovible]" : "")}");
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static DiskInfo Build(double usedGb, double totalGb, double freeGb, float pct, bool removable)
    {
        string uU = usedGb  >= 1024 ? "To" : "Go";
        string tU = totalGb >= 1024 ? "To" : "Go";
        string fU = freeGb  >= 1024 ? "To" : "Go";
        // Valeurs déjà converties dans la bonne unité :
        //   Go → valeur en Go (ex. 447.03)
        //   To → valeur en To (ex. 1.82)  ← le HTML utilise ces valeurs directement
        double uD = uU == "To" ? Math.Round(usedGb  / 1024, 2) : Math.Round(usedGb,  2);
        double tD = tU == "To" ? Math.Round(totalGb / 1024, 2) : Math.Round(totalGb, 2);
        double fD = fU == "To" ? Math.Round(freeGb  / 1024, 2) : Math.Round(freeGb,  2);
        return new DiskInfo
        {
            UsedGb   = uD,   // valeur dans UsedUnit  (To ou Go)
            TotalGb  = tD,   // valeur dans TotalUnit (To ou Go)
            FreeGb   = fD,   // valeur dans FreeUnit  (To ou Go)
            UsedUnit = uU, TotalUnit = tU, FreeUnit = fU,
            Percent  = pct,
            Display  = $"{uD.ToString("F2", IC)}{uU}/{((long)Math.Ceiling(tD)).ToString(IC)}{tU}",
            Removable= removable,
        };
    }

    public void Dispose() { }
}
