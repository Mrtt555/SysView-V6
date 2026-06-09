// =============================================================
// Logger — journalisation fichier minimaliste
//
// Fichiers : %AppData%\SysViewManager\logs\sysview-YYYY-MM-DD.log
// Rotation : 1 fichier par jour, purge automatique après 7 jours.
// Thread-safe : lock sur l'écriture disque.
// =============================================================

namespace SysViewManager;

public static class Logger
{
    private static string _logDir   = "";
    private static readonly object _mu = new();
    private const int KEEP_DAYS = 7;

    // ─── Initialisation ──────────────────────────────────────────────────────

    public static void Init(string logDir)
    {
        _logDir = logDir;
        // Purge des fichiers plus vieux que KEEP_DAYS
        try
        {
            foreach (var f in Directory.GetFiles(logDir, "sysview-*.log"))
                if ((DateTime.Now - File.GetLastWriteTime(f)).TotalDays > KEEP_DAYS)
                    File.Delete(f);
        }
        catch { }
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    public static void Info (string msg)               => Write("INFO ", msg);
    public static void Warn (string msg)               => Write("WARN ", msg);
    public static void Error(string msg)               => Write("ERROR", msg);
    public static void Error(string msg, Exception ex) => Write("ERROR",
        $"{msg} — {ex.GetType().Name}: {ex.Message}");

    // ─── Écriture ─────────────────────────────────────────────────────────────

    private static void Write(string level, string msg)
    {
        if (string.IsNullOrEmpty(_logDir)) return;
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}{Environment.NewLine}";
            var path = Path.Combine(_logDir, $"sysview-{DateTime.Now:yyyy-MM-dd}.log");
            lock (_mu) File.AppendAllText(path, line, System.Text.Encoding.UTF8);
        }
        catch { /* ne jamais faire planter l'app à cause des logs */ }
    }
}
