// =============================================================
// MediaState — état média partagé (source unique : SMTC)
// =============================================================

namespace SysViewManager;

public sealed class MediaState
{
    public sealed class Snapshot
    {
        public string Title = "", Artist = "", Platform = "", MediaType = "", Source = "", ThumbUrl = "";
        public bool   Playing;
        public double Position, Duration, LastUpdate;
    }

    private Snapshot     _snap = new();
    private readonly object _mu = new();

    public Snapshot Get() { lock (_mu) return _snap; }

    // ─── Mise à jour depuis SMTC ─────────────────────────────────────────────

    public void UpdateFromSmtc(string title, string artist, string platform, string mediaType,
                                bool playing, double position, double duration, string thumbUrl)
    {
        lock (_mu)
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            if (string.IsNullOrEmpty(title))
            {
                if (_snap.Source == "smtc")
                {
                    Logger.Info("Media", "SMTC : titre vide — état réinitialisé");
                    _snap = new Snapshot();
                }
                return;
            }

            bool sourceChanged  = _snap.Source  != "smtc";
            bool titleChanged   = _snap.Title   != title;
            bool playingChanged = _snap.Playing  != playing;
            // Only reset the interpolation origin when position genuinely jumps
            // (seek, new track) — prevents FetchAsync thumbnail events from
            // resetting LastUpdate and making the progress bar appear frozen.
            bool positionJumped = titleChanged || Math.Abs(_snap.Position - position) > 1.5;

            _snap = new Snapshot
            {
                Title      = title,
                Artist     = artist,
                Platform   = platform,
                MediaType  = mediaType,
                Source     = "smtc",
                Playing    = playing,
                Position   = position,
                Duration   = duration,
                ThumbUrl   = thumbUrl,
                LastUpdate = positionJumped ? now : _snap.LastUpdate,
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
    /// </summary>
    public void ClearSmtc()
    {
        lock (_mu)
        {
            if (_snap.Source == "smtc")
            {
                Logger.Info("Media", $"SMTC : session terminée — état réinitialisé (était : \"{_snap.Title}\")");
                _snap = new Snapshot();
            }
        }
    }
}
