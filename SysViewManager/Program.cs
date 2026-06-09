// =============================================================
// SysView V6 — Point d'entrée
// Lance tous les services en parallèle, puis démarre le tray WinForms.
// =============================================================
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace SysViewManager;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // ── Instance unique ───────────────────────────────────────────────────
        using var mutex = new Mutex(true, "Global\\SysViewManagerMutex", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "SysView V6 est déjà en cours d'exécution.\nVérifiez l'icône dans la barre système.",
                "SysView V6", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // ── Chemins ───────────────────────────────────────────────────────────
        var baseDir   = AppContext.BaseDirectory;
        var apiDir    = Path.Combine(baseDir, "API");
        var aetherDir = Path.Combine(baseDir, "Aether");
        var logDir    = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(logDir);

        // ── Services ──────────────────────────────────────────────────────────
        var pythonW      = FindPythonW(apiDir);
        using var hwSvc  = new HardwareService();
        var diskSvc      = new DiskService();
        var rtCfg        = new RuntimeConfig(apiDir);
        using var aether = new AetherProcess(aetherDir, pythonW);
        using var weather= new WeatherService(rtCfg);
        var media        = new MediaState();

        // ── Démarrage Aether ─────────────────────────────────────────────────
        aether.Start();

        // ── Bridge HTTP (ASP.NET Core) sur thread background ─────────────────
        var cts    = new CancellationTokenSource();
        var bridge = new BridgeServer(hwSvc, diskSvc, weather, media, rtCfg, aether);
        var srv    = Task.Run(() => bridge.RunAsync(cts.Token));

        // ── Tray sur le thread STA principal ─────────────────────────────────
        using var tray = new TrayApp(hwSvc, weather, aether, logDir);
        Application.Run(tray);   // bloque jusqu'à "Quitter"

        // ── Nettoyage à la fermeture ──────────────────────────────────────────
        cts.Cancel();
        aether.Stop();
        try { srv.Wait(TimeSpan.FromSeconds(3)); } catch { }
    }

    // ─── Recherche de pythonw.exe ─────────────────────────────────────────────

    private static string FindPythonW(string apiDir)
    {
        // 1. Chemin enregistré dans runtime_config.json (par setup)
        var cfg = Path.Combine(apiDir, "runtime_config.json");
        if (File.Exists(cfg))
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfg));
                if (json.RootElement.TryGetProperty("pythonw", out var pwe))
                {
                    var p = pwe.GetString();
                    if (p != null && File.Exists(p)) return p;
                }
            }
            catch { }
        }

        // 2. PATH
        foreach (var name in new[] { "pythonw", "python" })
        {
            var found = WhereExe(name);
            if (found != null)
            {
                var pw = Path.Combine(Path.GetDirectoryName(found)!, "pythonw.exe");
                return File.Exists(pw) ? pw : found;
            }
        }

        // 3. %LOCALAPPDATA%\Programs\Python\PythonXX\
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pyRoot   = Path.Combine(localApp, "Programs", "Python");
        if (Directory.Exists(pyRoot))
        {
            foreach (var d in Directory.GetDirectories(pyRoot).OrderByDescending(x => x))
            {
                var pw = Path.Combine(d, "pythonw.exe");
                if (File.Exists(pw)) return pw;
            }
        }

        return "";
    }

    private static string? WhereExe(string name)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("cmd", $"/c where {name}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                }
            };
            p.Start();
            var first = p.StandardOutput.ReadLine()?.Trim();
            p.WaitForExit(3000);
            return first != null && File.Exists(first) ? first : null;
        }
        catch { return null; }
    }
}
