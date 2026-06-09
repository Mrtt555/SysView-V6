// =============================================================
// SysView V6 — Point d'entrée
// Données utilisateur → %AppData%\SysViewManager\
//   runtime_config.json, Hardware.json, Weather.json, logs/
// =============================================================
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace SysViewManager;

static class Program
{
    /// <summary>Dossier de données utilisateur commun à tous les services.</summary>
    public static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "SysViewManager");

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // ── Dossier de données ────────────────────────────────────────────────
        Directory.CreateDirectory(AppDataDir);
        var logsDir = Path.Combine(AppDataDir, "logs");
        Directory.CreateDirectory(logsDir);
        Logger.Init(logsDir);
        Logger.Info($"=== SysView V6 démarrage (PID {Environment.ProcessId}) ===");

        // ── Instance unique ───────────────────────────────────────────────────
        using var mutex = new Mutex(true, "Global\\SysViewManagerMutex", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "SysView V6 est déjà en cours d'exécution.\nVérifiez l'icône dans la barre système.",
                "SysView V6", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // ── Démarrage automatique (tâche planifiée admin) ─────────────────────
        EnsureAutoStart();

        // ── Services ──────────────────────────────────────────────────────────
        using var hwSvc   = new HardwareService(AppDataDir);
        var diskSvc       = new DiskService();
        var rtCfg         = new RuntimeConfig(AppDataDir);
        using var weather = new WeatherService(rtCfg, AppDataDir);
        var media         = new MediaState();

        // ── SMTC — détection native du média en cours ─────────────────────────
        // Événementiel (zéro CPU si rien ne joue). Silencieux si SMTC indisponible.
        // Priorité : extension Chrome (POST /v1/media) > SMTC.
        // Requiert Windows 10 1809 (17763+) — guard explicite pour éviter CA1416.
        var cts    = new CancellationTokenSource();
        SmtcService? smtc = null;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            smtc = new SmtcService(media);
            _ = smtc.StartAsync(cts.Token);
        }

        // ── Bridge HTTP (ASP.NET Core) sur thread background ─────────────────
        var bridge = new BridgeServer(hwSvc, diskSvc, weather, media, rtCfg);
        var srv    = Task.Run(() => bridge.RunAsync(cts.Token));

        // ── Tray sur le thread STA principal ─────────────────────────────────
        using var tray = new TrayApp(hwSvc, weather, AppDataDir);
        Application.Run(tray);

        // ── Nettoyage à la fermeture ──────────────────────────────────────────
        Logger.Info("=== SysView V6 arrêt ===");
        cts.Cancel();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) smtc?.Dispose();
        try { srv.Wait(TimeSpan.FromSeconds(3)); } catch { }
    }

    // ─── Tâche planifiée — création silencieuse au 1er lancement ─────────────

    public static void EnsureAutoStart()
    {
        try
        {
            // Vérifier si la tâche existe déjà
            using var check = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/query /tn \"SysViewManager\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                }
            };
            check.Start();
            check.WaitForExit(5000);
            if (check.ExitCode == 0) return;  // déjà enregistrée

            RegisterAutoStart();
        }
        catch { }
    }

    public static void RegisterAutoStart()
    {
        try
        {
            var exe = Environment.ProcessPath
                      ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                      ?? "";
            if (string.IsNullOrEmpty(exe)) return;

            using var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    $"/create /tn \"SysViewManager\" /tr \"\\\"{exe}\\\"\"" +
                    " /sc ONLOGON /rl HIGHEST /f")
                {
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                }
            };
            p.Start();
            p.WaitForExit(10_000);
        }
        catch { }
    }

    public static void UnregisterAutoStart()
    {
        try
        {
            using var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/delete /tn \"SysViewManager\" /f")
                {
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                }
            };
            p.Start();
            p.WaitForExit(5_000);
        }
        catch { }
    }

    public static bool IsAutoStartRegistered()
    {
        try
        {
            using var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/query /tn \"SysViewManager\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                }
            };
            p.Start();
            p.WaitForExit(3_000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
