// =============================================================
// MediaState — état média partagé
//
// Sources (par priorité décroissante) :
//   1. Extension Chrome  — POST /v1/media  (position exacte, artwork HD)
//   2. SMTC              — SmtcService     (natif Windows, événementiel)
//
// Règle de priorité :
//   Si l'extension a posté dans les 5 dernières secondes, les updates SMTC
//   sont ignorées. Dès que l'extension se tait (onglet fermé, navigateur
//   quitté), SMTC reprend automatiquement.
// =============================================================

namespace SysViewManager;

public sealed class MediaState
{
    public sealed class Snapshot
    {
        public string Title = "", Artist = "", Source = "", ThumbUrl = "";
        public bool   Playing;
        public double Position, Duration, LastUpdate;
    }

    private Snapshot _snap         = new();
    private double   _extLastPost;
    private readonly object _mu   = new();

    public Snapshot Get()        { lock (_mu) return _snap; }
    public double ExtLastPost    { get { lock (_mu) return _extLastPost; } }

    // ─── Source 1 : Extension Chrome ────────────────────────────────────────

    /// <summary>
    /// Met à jour l'état depuis un POST /v1/media de l'extension Chrome.
    /// Retourne false si ignoré (priorité source : titre en cours remplacé par pause).
    /// </summary>
    public bool Update(string title, string artist, bool playing,
                       double position, double duration, string thumbUrl)
    {
        lock (_mu)
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            _extLastPost = now;   // marque l'extension active même si POST ignoré

            // Priorité source : ne pas écraser un titre en cours par une source en pause
            if (title.Length > 0 && title != _snap.Title
                && _snap.Title.Length > 0 && _snap.Playing && !playing)
            {
                Logger.Debug("Media", $"  Extension ignorée (priorité source) : \"{title}\" (pause) < \"{_snap.Title}\" (lecture)");
                return false;
            }

            bool sourceChanged = _snap.Source != "extension";
            bool titleChanged  = _snap.Title  != title;

            _snap = new Snapshot
            {
                Title      = title,
                Artist     = artist,
                Source     = "extension",
                Playing    = playing,
                Position   = position,
                Duration   = duration,
                ThumbUrl   = thumbUrl,
                LastUpdate = now,
            };

            if (sourceChanged)
                Logger.Info("Media", $"Source → extension Chrome | {(playing ? "▶" : "⏸")} \"{title}\" — {artist}");
            else if (titleChanged && title.Length > 0)
                Logger.Info("Media", $"Extension : nouveau titre — {(playing ? "▶" : "⏸")} \"{title}\" — {artist}");
            else
                Logger.Debug("Media", $"Extension : position/état — {(playing ? "▶" : "⏸")} {position:F0}s / {duration:F0}s");

            return true;
        }
    }

    // ─── Source 2 : SMTC (SmtcService) ──────────────────────────────────────

    /// <summary>
    /// Met à jour l'état depuis SMTC (source native Windows).
    /// Ignoré si l'extension Chrome a posté dans les 5 dernières secondes.
    /// </summary>
    public void UpdateFromSmtc(string title, string artist, bool playing,
                                double position, double duration, string thumbUrl)
    {
        lock (_mu)
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            // L'extension Chrome a la priorité si elle est active
            if (_extLastPost > 0 && (now - _extLastPost) < 5.0)
            {
                Logger.Debug("Media", $"  SMTC ignoré (extension active depuis {(now - _extLastPost):F1}s) : \"{title}\"");
                return;
            }

            if (string.IsNullOrEmpty(title))
            {
                if (_snap.Source == "smtc")
                {
                    Logger.Info("Media", "SMTC : titre vide — état réinitialisé");
                    _snap = new Snapshot();
                }
                return;
            }

            bool sourceChanged = _snap.Source != "smtc";
            bool titleChanged  = _snap.Title  != title;
            bool playingChanged= _snap.Playing != playing;

            _snap = new Snapshot
            {
                Title      = title,
                Artist     = artist,
                Source     = "smtc",
                Playing    = playing,
                Position   = position,
                Duration   = duration,
                ThumbUrl   = thumbUrl,
                LastUpdate = now,
            };

            if (sourceChanged)
                Logger.Info("Media", $"Source → SMTC | {(playing ? "▶" : "⏸")} \"{title}\" — {artist}");
            else if (titleChanged)
                Logger.Info("Media", $"SMTC : nouveau titre — {(playing ? "▶" : "⏸")} \"{title}\" — {artist}");
            else if (playingChanged)
                Logger.Info("Media", $"SMTC : état {(playing ? "▶ lecture" : "⏸ pause")} — \"{title}\"");
            else
                Logger.Debug("Media", $"SMTC : position — {position:F0}s / {duration:F0}s");
        }
    }

    /// <summary>
    /// Efface l'état SMTC (session terminée ou plus aucun lecteur actif).
    /// Sans effet si l'extension Chrome est active.
    /// </summary>
    public void ClearSmtc()
    {
        lock (_mu)
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (_extLastPost > 0 && (now - _extLastPost) < 5.0)
            {
                Logger.Debug("Media", "ClearSmtc ignoré (extension active)");
                return;
            }
            if (_snap.Source == "smtc")
            {
                Logger.Info("Media", $"SMTC : session terminée — état réinitialisé (était : \"{_snap.Title}\")");
                _snap = new Snapshot();
            }
        }
    }
}
