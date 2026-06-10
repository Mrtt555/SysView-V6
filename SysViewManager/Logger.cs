// =============================================================
// Logger — journalisation fichier complète
//
// Format : [yyyy-MM-dd HH:mm:ss.fff] [LVL] [Source    ] Message
//
// Fichiers :
//   logs/sysview-YYYY-MM-DD.log   — tout (≥ MinLevel)
//   logs/sysview-errors.log       — erreurs seulement (toujours écrit)
//
// Rotation : 1 fichier par jour, purge auto après KEEP_DAYS jours.
// Thread-safe : lock sur l'écriture disque.
// =============================================================

namespace SysViewManager;

public static class Logger
{
    // ─── Niveaux ──────────────────────────────────────────────────────────────

    public enum Level { Debug = 0, Info = 1, Warn = 2, Error = 3 }

    /// <summary>Niveau minimum écrit dans les logs (défaut : Info).</summary>
    public static Level MinLevel { get; set; } = Level.Info;

    // ─── Constantes ───────────────────────────────────────────────────────────

    private const int  KEEP_DAYS   = 14;
    private const int  SRC_WIDTH   = 11;   // largeur du champ Source (pour l'alignement)

    private static readonly string[] LEVEL_TAG = { "DBG", "INF", "WRN", "ERR" };

    // ─── État interne ─────────────────────────────────────────────────────────

    private static string       _logDir    = "";
    private static string       _errorFile = "";
    private static readonly object _mu     = new();

    // ─── Initialisation ──────────────────────────────────────────────────────

    public static void Init(string logDir)
    {
        _logDir    = logDir;
        _errorFile = Path.Combine(logDir, "sysview-errors.log");

        // Purge des fichiers journaliers trop anciens
        try
        {
            foreach (var f in Directory.GetFiles(logDir, "sysview-20*.log"))
                if ((DateTime.Now - File.GetLastWriteTime(f)).TotalDays > KEEP_DAYS)
                    File.Delete(f);
        }
        catch { }
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    public static void Debug(string src, string msg)
        => Write(Level.Debug, src, msg);

    public static void Info(string src, string msg)
        => Write(Level.Info, src, msg);

    public static void Warn(string src, string msg)
        => Write(Level.Warn, src, msg);

    public static void Error(string src, string msg)
        => Write(Level.Error, src, msg);

    public static void Error(string src, string msg, Exception ex)
        => Write(Level.Error, src, $"{msg}\n{FormatException(ex)}");

    // Surcharge sans source (rétrocompat) — utilise "App"
    public static void Info (string msg) => Info ("App", msg);
    public static void Warn (string msg) => Warn ("App", msg);
    public static void Error(string msg) => Error("App", msg);
    public static void Error(string msg, Exception ex) => Error("App", msg, ex);

    // ─── Séparateur visuel ───────────────────────────────────────────────────

    public static void Separator(string src, string label = "")
    {
        var line = string.IsNullOrEmpty(label)
            ? new string('─', 60)
            : $"── {label} " + new string('─', Math.Max(0, 57 - label.Length));
        Write(Level.Info, src, line);
    }

    // ─── Implémentation ───────────────────────────────────────────────────────

    private static void Write(Level level, string src, string msg)
    {
        if (level < MinLevel) return;
        if (string.IsNullOrEmpty(_logDir)) return;

        try
        {
            var now  = DateTime.Now;
            var tag  = LEVEL_TAG[(int)level];
            var pad  = src.PadRight(SRC_WIDTH).Substring(0, SRC_WIDTH);
            var ts   = now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"[{ts}] [{tag}] [{pad}] {msg}{Environment.NewLine}";

            var dayFile = Path.Combine(_logDir, $"sysview-{now:yyyy-MM-dd}.log");

            lock (_mu)
            {
                File.AppendAllText(dayFile,    line, System.Text.Encoding.UTF8);
                if (level == Level.Error)
                    File.AppendAllText(_errorFile, line, System.Text.Encoding.UTF8);
            }
        }
        catch { /* ne jamais faire planter l'app à cause des logs */ }
    }

    private static string FormatException(Exception? ex, int depth = 0)
    {
        if (ex == null) return "";
        var indent = new string(' ', depth * 4 + 4);
        var sb     = new System.Text.StringBuilder();

        sb.AppendLine($"{indent}{ex.GetType().FullName}: {ex.Message}");

        // Stack trace (tronquée aux 8 premières frames pour rester lisible)
        if (ex.StackTrace is { } st)
        {
            var frames = st.Split('\n').Take(8);
            foreach (var f in frames)
                sb.AppendLine($"{indent}  {f.TrimStart()}");
        }

        if (ex.InnerException != null)
        {
            sb.AppendLine($"{indent}  --- InnerException ---");
            sb.Append(FormatException(ex.InnerException, depth + 1));
        }

        return sb.ToString().TrimEnd();
    }
}
