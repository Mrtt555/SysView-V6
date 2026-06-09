// =============================================================
// MediaState — état média partagé (extension Chrome → bridge)
// Équivalent du dict MEDIA + logique priorité source du bridge Python.
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
                return false;

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
            return true;
        }
    }
}
