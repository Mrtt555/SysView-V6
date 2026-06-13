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
    private const long MAX_BYTES   = 512 * 1024;   // 512 Ko — taille max du fichier jour
    private const long TRIM_BYTES  = 256 * 1024;   // garder les 256 Ko les plus récents

    private static readonly string[] LEVEL_TAG = { "DEBUG", "INFO ", "WARN ", "ERROR" };

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
            : "── " + label.Substring(0, Math.Min(label.Length, 54)) + " "
              + new string('─', Math.Max(0, 57 - Math.Min(label.Length, 54)));
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
                File.AppendAllText(dayFile, line, System.Text.Encoding.UTF8);
                if (new FileInfo(dayFile).Length > MAX_BYTES)
                    TrimFile(dayFile, TRIM_BYTES);
                if (level == Level.Error)
                {
                    File.AppendAllText(_errorFile, line, System.Text.Encoding.UTF8);
                    if (new FileInfo(_errorFile).Length > MAX_BYTES)
                        TrimFile(_errorFile, TRIM_BYTES);
                }
            }
        }
        catch { /* ne jamais faire planter l'app à cause des logs */ }
    }

    // Tronque le fichier en ne gardant que les `keepBytes` octets les plus récents,
    // en coupant proprement sur un saut de ligne.
    private static void TrimFile(string path, long keepBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (fs.Length <= keepBytes) return;
            fs.Seek(-keepBytes, SeekOrigin.End);
            // Avancer jusqu'au prochain \n pour ne pas couper une ligne à mi-chemin
            while (fs.ReadByte() is int b and not -1 and not '\n') { }
            long start     = fs.Position;
            long remaining = fs.Length - start;
            var  buf       = new byte[remaining];
            fs.ReadExactly(buf, 0, (int)remaining); // ReadExactly évite la lecture partielle (.NET 7+)
            fs.Seek(0, SeekOrigin.Begin);
            fs.Write(buf, 0, buf.Length);
            fs.SetLength(remaining);
        }
        catch { }
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
