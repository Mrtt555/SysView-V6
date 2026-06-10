// =============================================================
// MediaState — état média partagé (source unique : extension navigateur)
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

    public void Update(string title, string artist, string service, string host,
                       bool playing, int position, int duration, string artworkUrl)
    {
        lock (_mu)
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            if (string.IsNullOrEmpty(title))
            {
                if (_snap.Source == "ext")
                {
                    _snap = new Snapshot();
                    Logger.Info("Media", "Extension : aucun média actif — état réinitialisé");
                }
                return;
            }

            bool titleChanged   = _snap.Title   != title;
            bool playingChanged = _snap.Playing  != playing;
            bool positionJumped = titleChanged || Math.Abs(_snap.Position - position) > 1.5;

            string platform  = !string.IsNullOrEmpty(service) ? service : host;
            string mediaType = service is "YouTube" ? "youtube"
                             : service is "YouTube Music" or "Spotify" or "Deezer"
                                       or "Tidal" or "SoundCloud" or "Apple Music" ? "music"
                             : !string.IsNullOrEmpty(service) ? "video"
                             : "video";

            _snap = new Snapshot
            {
                Title      = title,
                Artist     = artist,
                Platform   = platform,
                MediaType  = mediaType,
                Source     = "ext",
                Playing    = playing,
                Position   = position,
                Duration   = duration,
                ThumbUrl   = artworkUrl,
                LastUpdate = positionJumped ? now : _snap.LastUpdate,
            };

            if (titleChanged)
                Logger.Info("Media", $"{(playing ? "▶" : "⏸")} \"{title}\" [{platform}]");
            else if (playingChanged)
                Logger.Info("Media", $"{(playing ? "▶ lecture" : "⏸ pause")} — \"{title}\"");
            else
                Logger.Debug("Media", $"position — {position}s / {duration}s  [{platform}]");
        }
    }

    public void Clear()
    {
        lock (_mu)
        {
            if (_snap.Source == "ext")
            {
                _snap = new Snapshot();
                Logger.Info("Media", "Extension : aucun onglet actif — état réinitialisé");
            }
        }
    }
}
