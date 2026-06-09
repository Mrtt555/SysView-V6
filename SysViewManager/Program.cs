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

        // ── En-tête de démarrage ──────────────────────────────────────────────
        Logger.Separator("Program");
        Logger.Info("Program", $"=== SysView V6 démarrage ===");
        Logger.Info("Program", $"PID      = {Environment.ProcessId}");
        Logger.Info("Program", $"Exe      = {Environment.ProcessPath}");
        Logger.Info("Program", $"OS       = {Environment.OSVersion}  ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        Logger.Info("Program", $".NET     = {Environment.Version}");
        Logger.Info("Program", $"AppData  = {AppDataDir}");
        Logger.Info("Program", $"Logs     = {logsDir}");
        Logger.Separator("Program");

        // ── Instance unique ───────────────────────────────────────────────────
        using var mutex = new Mutex(true, "Global\\SysViewManagerMutex", out bool isNew);
        if (!isNew)
        {
            Logger.Warn("Program", "Instance déjà en cours — fermeture (mutex pris)");
            MessageBox.Show(
                "SysView V6 est déjà en cours d'exécution.\nVérifiez l'icône dans la barre système.",
                "SysView V6", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // ── Démarrage automatique (tâche planifiée admin) ─────────────────────
        Logger.Info("Program", "Vérification du démarrage automatique (schtasks)...");
        bool autoStartOk = IsAutoStartRegistered();
        if (!autoStartOk)
        {
            Logger.Info("Program", "Tâche planifiée absente — création...");
            EnsureAutoStart();
            Logger.Info("Program", IsAutoStartRegistered()
                ? "Tâche planifiée créée avec succès"
                : "Tâche planifiée : création échouée (droits insuffisants ?)");
        }
        else
        {
            Logger.Info("Program", "Tâche planifiée SysViewManager : OK");
        }

        // ── Services ──────────────────────────────────────────────────────────
        Logger.Info("Program", "Initialisation des services...");

        Logger.Info("Program", "  [1/5] RuntimeConfig...");
        var rtCfg = new RuntimeConfig(AppDataDir);

        Logger.Info("Program", "  [2/5] HardwareService (LHM)...");
        using var hwSvc = new HardwareService(AppDataDir);

        Logger.Info("Program", "  [3/5] DiskService...");
        var diskSvc = new DiskService();

        Logger.Info("Program", "  [4/5] WeatherService (Open-Meteo)...");
        using var weather = new WeatherService(rtCfg, AppDataDir);

        Logger.Info("Program", "  [5/5] MediaState...");
        var media = new MediaState();

        // ── SMTC — détection native du média en cours ─────────────────────────
        var cts    = new CancellationTokenSource();
        SmtcService? smtc = null;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Logger.Info("Program", "SMTC : Windows 10 1809+ détecté — démarrage du service...");
            smtc = new SmtcService(media);
            _ = smtc.StartAsync(cts.Token);
        }
        else
        {
            Logger.Warn("Program", $"SMTC : Windows {Environment.OSVersion.Version} trop ancien (< 17763) — désactivé");
        }

        // ── Bridge HTTP (ASP.NET Core) sur thread background ─────────────────
        Logger.Info("Program", "BridgeServer : démarrage du serveur HTTP...");
        var bridge = new BridgeServer(hwSvc, diskSvc, weather, media, rtCfg);
        var srv    = Task.Run(() => bridge.RunAsync(cts.Token));

        // ── Tray sur le thread STA principal ─────────────────────────────────
        Logger.Info("Program", "TrayApp : création de l'icône système...");
        Logger.Info("Program", "=== Tous les services démarrés — en attente d'événements ===");
        using var tray = new TrayApp(hwSvc, weather, AppDataDir);
        Application.Run(tray);

        // ── Nettoyage à la fermeture ──────────────────────────────────────────
        Logger.Separator("Program");
        Logger.Info("Program", "=== SysView V6 arrêt ===");
        Logger.Info("Program", "Annulation des services...");
        cts.Cancel();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) smtc?.Dispose();
        try { srv.Wait(TimeSpan.FromSeconds(3)); } catch { }
        Logger.Info("Program", "Arrêt propre terminé.");
        Logger.Separator("Program");
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

            Logger.Info("Program", $"schtasks /create — exe={exe}");
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
            Logger.Info("Program", "schtasks /delete SysViewManager");
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
