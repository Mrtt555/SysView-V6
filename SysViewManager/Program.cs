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

        // ── Dossier de données (avant les handlers pour que Logger soit prêt) ─
        Directory.CreateDirectory(AppDataDir);
        var logsDir = Path.Combine(AppDataDir, "logs");
        Directory.CreateDirectory(logsDir);
        Logger.Init(logsDir);

        // ── Handlers d'exception globaux (diagnostic crash) ───────────────────
        Application.ThreadException += (_, e) =>
            Logger.Error("Program", "Exception non gérée (thread UI) — arrêt imminent", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error("Program", $"Exception fatale CLR: {e.ExceptionObject}");

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
        EnsureAutoStart();
        Logger.Info("Program", IsAutoStartRegistered()
            ? "Tâche planifiée SysViewManager : OK"
            : "Tâche planifiée : création échouée (droits insuffisants ?)");

        // ── Services ──────────────────────────────────────────────────────────
        Logger.Info("Program", "Initialisation des services...");

        Logger.Info("Program", "  [1/4] RuntimeConfig...");
        var rtCfg = new RuntimeConfig(AppDataDir);

        Logger.Info("Program", "  [2/4] HardwareService (LHM)...");
        using var hwSvc = new HardwareService(AppDataDir);

        Logger.Info("Program", "  [3/4] DiskService...");
        using var diskSvc = new DiskService();

        Logger.Info("Program", "  [4/4] WeatherService + MediaState...");
        using var weather = new WeatherService(rtCfg, AppDataDir);
        var media = new MediaState();

        // ── Bridge HTTP (ASP.NET Core) sur thread background ─────────────────
        Logger.Info("Program", "BridgeServer : démarrage du serveur HTTP...");
        using var cts = new CancellationTokenSource();
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
        try { srv.Wait(TimeSpan.FromSeconds(3)); } catch { }
        Logger.Info("Program", "Arrêt propre terminé.");
        Logger.Separator("Program");
    }

    // ─── Tâche planifiée — création silencieuse au 1er lancement ─────────────

    public static void EnsureAutoStart()
    {
        try
        {
            var exe = Environment.ProcessPath
                      ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                      ?? "";
            if (string.IsNullOrEmpty(exe)) return;

            // Vérifier si la tâche existe avec le bon chemin
            using var check = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/query /tn \"SysViewManager\" /fo LIST /v")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                }
            };
            check.Start();
            // Lire stdout sur un thread séparé pour éviter le deadlock si le buffer se remplit
            var stdoutTask = System.Threading.Tasks.Task.Run(() => check.StandardOutput.ReadToEnd());
            check.WaitForExit(5000);
            var output = stdoutTask.IsCompleted ? stdoutTask.Result : "";

            if (check.ExitCode == 0 && output.Contains(exe, StringComparison.OrdinalIgnoreCase))
                return;  // tâche existante avec chemin correct

            // Tâche absente ou chemin obsolète → (re)créer
            if (check.ExitCode == 0)
                Logger.Info("Program", $"Tâche planifiée : chemin obsolète — mise à jour vers {exe}");

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
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                }
            };
            p.Start();
            bool exited = p.WaitForExit(3_000);
            return exited && p.ExitCode == 0;
        }
        catch { return false; }
    }
}
