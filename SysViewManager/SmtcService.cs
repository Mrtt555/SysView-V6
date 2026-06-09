// =============================================================
// SmtcService — détection native du média via SMTC
//
// SMTC = System Media Transport Controls (Windows 10+)
// API : Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager
//
// Architecture :
//   ▸ Purement événementiel (SessionsChanged, MediaPropertiesChanged,
//     PlaybackInfoChanged, TimelinePropertiesChanged) → zéro CPU quand
//     aucun média ne change.
//   ▸ Sélection intelligente de session : préfère la session Playing
//     sur la session courante (GetCurrentSession). Si la session liée
//     passe en pause et qu'une autre session joue, bascule dessus.
//   ▸ La miniature base64 est ré-encodée UNIQUEMENT si le titre change.
//   ▸ Priorité : extension Chrome (POST /v1/media, active < 5 s) > SMTC
//     → si l'extension est active, les updates SMTC sont ignorées.
//   ▸ Silencieux si SMTC indisponible.
//
// Note VLC : VLC doit avoir "Utiliser les touches multimédia Windows"
//   activé (Préférences → Interface) pour s'enregistrer avec SMTC.
//
// Requis : TargetFramework net8.0-windows10.0.17763.0 dans le csproj.
// =============================================================

using System.Diagnostics;
using System.Runtime.Versioning;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SysViewManager;

[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class SmtcService : IDisposable
{
    private readonly MediaState  _media;
    private readonly TmdbService? _tmdb;

    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;

    // Cache miniature — évite le ré-encodage base64 si même titre
    private string _lastThumbTitle = "";
    private string _lastThumbUrl   = "";

    // ─── Détection des services de streaming dans le titre du navigateur ────
    // Format des titres selon le service :
    //   Préfixe  : "Prime Video: {titre}"
    //   Suffixe  : "{titre} | Netflix",  "{titre} - Crunchyroll", etc.
    private static readonly (string Pattern, bool IsPrefix, string ServiceName)[] _streamingPatterns =
    {
        // ── Préfixes ───────────────────────────────────────────────────────
        ("Prime Video: ",        true,  "Prime Video"),
        ("Amazon Prime Video: ", true,  "Prime Video"),

        // ── Suffixes ───────────────────────────────────────────────────────
        (" | Netflix",           false, "Netflix"),
        (" | Disney+",           false, "Disney+"),
        (" | Max",               false, "Max"),
        (" | Hulu",              false, "Hulu"),
        (" | Paramount+",        false, "Paramount+"),
        (" | Peacock",           false, "Peacock"),
        (" | MUBI",              false, "MUBI"),
        (" | Shudder",           false, "Shudder"),
        (" | Tubi",              false, "Tubi"),
        (" | SkyShowtime",       false, "SkyShowtime"),
        (" | Funimation",        false, "Funimation"),
        (" | Crunchyroll",       false, "Crunchyroll"),
        (" - Crunchyroll",       false, "Crunchyroll"),
        (" — Crunchyroll",       false, "Crunchyroll"),
        (" - Apple TV+",         false, "Apple TV+"),
        (" — Apple TV+",         false, "Apple TV+"),
        (" - Plex",              false, "Plex"),
        (" - Emby",              false, "Emby"),
        (" - Jellyfin",          false, "Jellyfin"),
        (" | Jellyfin",          false, "Jellyfin"),
        (" — Jellyfin",          false, "Jellyfin"),
        (" | HBO Max",           false, "HBO Max"),
    };

    /// <summary>
    /// Extrait le titre propre et le nom du service depuis le titre brut du navigateur.
    /// Ex : "Prime Video: The Boys" → ("The Boys", "Prime Video")
    ///      "Stranger Things | Netflix" → ("Stranger Things", "Netflix")
    ///      "Natasha St-Pier - Tu trouveras" → ("Natasha St-Pier - Tu trouveras", "")
    /// </summary>
    private static (string CleanTitle, string Service) ExtractStreamingService(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return (raw, "");

        foreach (var (pattern, isPrefix, service) in _streamingPatterns)
        {
            if (isPrefix)
            {
                if (raw.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return (raw[pattern.Length..].Trim(), service);
            }
            else
            {
                // Chercher le séparateur en partant de la fin (le titre lui-même peut
                // contenir " | " ou " - " mais le séparateur de service est toujours dernier)
                int idx = raw.LastIndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                    return (raw[..idx].Trim(), service);
            }
        }

        return (raw, "");
    }

    // Indique que MediaPropertiesChanged vient de se déclencher :
    // la miniature DOIT être re-lue même si le titre n'a pas changé
    // (Brave/Chrome fournit d'abord l'icône de l'app, puis la miniature réelle).
    private volatile bool _pendingThumbRefresh = false;

    // État précédent pour logs différentiels
    private string _lastLoggedTitle  = "";
    private string _lastLoggedAppId  = "";

    // Sémaphore 1-1 : empêche les FetchAsync concurrents
    private readonly SemaphoreSlim _sem = new(1, 1);

    public SmtcService(MediaState media, TmdbService? tmdb = null)
    {
        _media = media;
        _tmdb  = tmdb;
        Logger.Info("SMTC", "Service créé" + (tmdb?.IsConfigured == true ? " [TMDB actif]" : ""));
    }

    // ─── Démarrage ───────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        Logger.Info("SMTC", "Demande d'accès au gestionnaire SMTC...");
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager
                         .RequestAsync()
                         .AsTask(ct);

            _mgr.SessionsChanged += OnSessionsChanged;

            var sessions = _mgr.GetSessions();
            Logger.Info("SMTC", $"Gestionnaire SMTC obtenu — {sessions.Count} session(s) active(s)");
            foreach (var s in sessions)
                Logger.Info("SMTC", $"  · {s.SourceAppUserModelId}  état={StatusLabel(s)}");

            BindSession(PickBestSession(sessions));
            Logger.Info("SMTC", "Détection native des médias active (événementiel)");
        }
        catch (OperationCanceledException)
        {
            Logger.Info("SMTC", "Démarrage annulé (CancellationToken)");
        }
        catch (Exception ex)
        {
            Logger.Warn("SMTC", "API SMTC indisponible — fallback extension Chrome");
            Logger.Warn("SMTC", $"  Cause : {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ─── Sélection de la meilleure session ───────────────────────────────────

    /// <summary>
    /// Parmi toutes les sessions SMTC enregistrées, retourne celle qui joue.
    /// Fallback : session en pause. Fallback : GetCurrentSession().
    /// </summary>
    private GlobalSystemMediaTransportControlsSession? PickBestSession(
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        if (sessions.Count == 0) return null;

        GlobalSystemMediaTransportControlsSession? bestPlaying = null;
        GlobalSystemMediaTransportControlsSession? bestPaused  = null;

        foreach (var s in sessions)
        {
            try
            {
                var status = s.GetPlaybackInfo()?.PlaybackStatus;
                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    bestPlaying ??= s;
                else
                    bestPaused  ??= s;
            }
            catch { }
        }

        var best = bestPlaying ?? bestPaused;
        if (sessions.Count > 1)
        {
            Logger.Debug("SMTC", $"PickBestSession : {sessions.Count} sessions — choix={best?.SourceAppUserModelId ?? "aucune"}"
                + (bestPlaying != null ? $" [playing]" : " [paused-only]"));
        }
        return best;
    }

    // ─── Gestion des sessions ────────────────────────────────────────────────

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager mgr,
        SessionsChangedEventArgs _)
    {
        var sessions = mgr.GetSessions();
        Logger.Info("SMTC", $"SessionsChanged — {sessions.Count} session(s) :");
        foreach (var s in sessions)
            Logger.Info("SMTC", $"  · {s.SourceAppUserModelId}  état={StatusLabel(s)}");

        BindSession(PickBestSession(sessions));
    }

    private void BindSession(GlobalSystemMediaTransportControlsSession? s)
    {
        // Désabonnement de l'ancienne session
        if (_session is { } old)
        {
            old.MediaPropertiesChanged    -= OnMediaChanged;
            old.PlaybackInfoChanged       -= OnPlaybackChanged;
            old.TimelinePropertiesChanged -= OnTimelineChanged;
            Logger.Info("SMTC", $"Session détachée : {old.SourceAppUserModelId}");
        }

        _session = s;

        if (s == null)
        {
            Logger.Info("SMTC", "Aucune session active — état média réinitialisé");
            _media.ClearSmtc();
            return;
        }

        s.MediaPropertiesChanged    += OnMediaChanged;
        s.PlaybackInfoChanged       += OnPlaybackChanged;
        s.TimelinePropertiesChanged += OnTimelineChanged;
        Logger.Info("SMTC", $"Session attachée : {s.SourceAppUserModelId}  [{StatusLabel(s)}]");

        // Lecture initiale
        _ = Task.Run(FetchAsync);
    }

    // ─── Handlers d'événements ───────────────────────────────────────────────

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s,
                                 MediaPropertiesChangedEventArgs e)
    {
        Logger.Debug("SMTC", $"MediaPropertiesChanged — {s.SourceAppUserModelId}");
        // Forcer la relecture de la miniature même si le titre n'a pas changé :
        // les navigateurs (Brave, Chrome…) fournissent souvent l'icône de l'app
        // dans un premier événement, puis la vraie miniature dans un second.
        _pendingThumbRefresh = true;
        _ = Task.Run(FetchAsync);
    }

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s,
                                    PlaybackInfoChangedEventArgs e)
    {
        Logger.Debug("SMTC", $"PlaybackInfoChanged — {s.SourceAppUserModelId}  état={StatusLabel(s)}");

        // Si la session courante vient de passer en pause, chercher une session
        // qui joue parmi toutes les sessions disponibles.
        if (_mgr != null)
        {
            try
            {
                var status = s.GetPlaybackInfo()?.PlaybackStatus;
                bool isPaused = status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                if (isPaused)
                {
                    var all = _mgr.GetSessions();
                    if (all.Count > 1)
                    {
                        var best = PickBestSession(all);
                        if (best != null && best.SourceAppUserModelId != s.SourceAppUserModelId)
                        {
                            Logger.Info("SMTC", $"Session courante en pause → switch vers {best.SourceAppUserModelId} [{StatusLabel(best)}]");
                            BindSession(best);
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        _ = Task.Run(FetchAsync);
    }

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s,
                                    TimelinePropertiesChangedEventArgs e)
    {
        Logger.Debug("SMTC", $"TimelinePropertiesChanged — {s.SourceAppUserModelId}");
        _ = Task.Run(FetchAsync);
    }

    // ─── Lecture SMTC ────────────────────────────────────────────────────────

    private async Task FetchAsync()
    {
        if (!await _sem.WaitAsync(0))
        {
            Logger.Debug("SMTC", "FetchAsync ignoré (déjà en cours)");
            return;
        }

        // Lire et réinitialiser le flag APRÈS avoir acquis le sémaphore.
        // Si _pendingThumbRefresh repasse à true pendant notre exécution (nouveau
        // MediaPropertiesChanged), le bloc finally re-queuera un nouveau FetchAsync.
        bool forceThumb = _pendingThumbRefresh;
        _pendingThumbRefresh = false;

        try
        {
            var s = _session;
            if (s == null) return;

            GlobalSystemMediaTransportControlsSessionMediaProperties? props;
            GlobalSystemMediaTransportControlsSessionPlaybackInfo?    playback;
            GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline;
            try
            {
                props    = await s.TryGetMediaPropertiesAsync();
                playback = s.GetPlaybackInfo();
                timeline = s.GetTimelineProperties();
            }
            catch (Exception ex)
            {
                Logger.Warn("SMTC", $"FetchAsync : session fermée pendant la lecture — {ex.GetType().Name}: {ex.Message}");
                return;
            }

            string rawTitle = props?.Title  ?? "";
            string artist   = props?.Artist ?? "";
            bool   playing  = playback?.PlaybackStatus ==
                              GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            double position = timeline?.Position.TotalSeconds ?? 0;
            double duration = timeline?.EndTime.TotalSeconds  ?? 0;

            // ── Nettoyage du titre (services de streaming) ────────────────────
            // Ex: "Prime Video: The Boys" → title="The Boys", service="Prime Video"
            //     "Stranger Things | Netflix" → title="Stranger Things", service="Netflix"
            var (title, service) = ExtractStreamingService(rawTitle);

            // Artiste : si vide et service détecté, utiliser le service comme fallback
            // (Netflix, Prime Video, etc. ne fournissent pas d'artiste via SMTC)
            if (string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(service))
                artist = service;

            // ── Miniature ─────────────────────────────────────────────────────
            string thumbUrl       = "";
            bool   thumbFromCache = false;
            bool   thumbFromTmdb  = false;

            if (!string.IsNullOrEmpty(title))
            {
                // Utiliser le cache seulement si :
                //   • le titre n'a pas changé (même morceau/vidéo)
                //   • ET aucun MediaPropertiesChanged n'a demandé de relecture
                //     (forceThumb = false → cas timeline / playback uniquement)
                if (!forceThumb && title == _lastThumbTitle)
                {
                    thumbUrl       = _lastThumbUrl;
                    thumbFromCache = true;
                }
                else if (props?.Thumbnail is { } thumb)
                {
                    var swThumb = Stopwatch.StartNew();
                    try
                    {
                        using var ras = await thumb.OpenReadAsync();
                        // Respecter le content-type fourni par l'app (JPEG, PNG, …)
                        string ct = ras.ContentType;
                        if (string.IsNullOrEmpty(ct)) ct = "image/jpeg";

                        using var ms = new System.IO.MemoryStream();
                        await ras.AsStreamForRead().CopyToAsync(ms);
                        swThumb.Stop();

                        if (ms.Length > 0)
                        {
                            thumbUrl        = $"data:{ct};base64," + Convert.ToBase64String(ms.ToArray());
                            _lastThumbTitle = title;
                            _lastThumbUrl   = thumbUrl;
                            Logger.Debug("SMTC", $"  Miniature encodée : {ct}  {ms.Length / 1024} Ko  {swThumb.ElapsedMilliseconds} ms");
                        }
                        else
                        {
                            Logger.Warn("SMTC", $"  Miniature vide (0 octet) — app={s.SourceAppUserModelId}");
                            // Pas de miniature SMTC → essayer TMDB
                            thumbUrl = await TryTmdbAsync(title, service);
                            thumbFromTmdb = !string.IsNullOrEmpty(thumbUrl);
                            _lastThumbTitle = title;
                            _lastThumbUrl   = thumbUrl;
                        }
                    }
                    catch (Exception ex)
                    {
                        swThumb.Stop();
                        Logger.Warn("SMTC", $"  Miniature illisible ({swThumb.ElapsedMilliseconds} ms) : {ex.Message}");
                        thumbUrl = await TryTmdbAsync(title, service);
                        thumbFromTmdb = !string.IsNullOrEmpty(thumbUrl);
                        _lastThumbTitle = title;
                        _lastThumbUrl   = thumbUrl;
                    }
                }
                else
                {
                    // Pas de miniature fournie par l'app (Thumbnail == null)
                    // → typique pour les services DRM (Netflix, Prime Video…)
                    // → tentative de récupération du poster via TMDB
                    thumbUrl = await TryTmdbAsync(title, service);
                    thumbFromTmdb = !string.IsNullOrEmpty(thumbUrl);
                    if (!thumbFromTmdb)
                        Logger.Debug("SMTC", $"  Pas de miniature pour \"{title}\" (source={s.SourceAppUserModelId})");
                    _lastThumbTitle = title;
                    _lastThumbUrl   = thumbUrl;
                }
            }
            else
            {
                _lastThumbTitle = "";
                _lastThumbUrl   = "";
            }

            // ── Log différentiel ──────────────────────────────────────────────
            if (title != _lastLoggedTitle || s.SourceAppUserModelId != _lastLoggedAppId)
            {
                _lastLoggedTitle = title;
                _lastLoggedAppId = s.SourceAppUserModelId;
                if (!string.IsNullOrEmpty(title))
                {
                    string thumbLabel = thumbFromCache ? "[miniature: cache]"
                                      : thumbFromTmdb  ? "[miniature: TMDB]"
                                      : !string.IsNullOrEmpty(thumbUrl) ? "[miniature: encodée]"
                                      : "[sans miniature]";
                    string serviceLabel = !string.IsNullOrEmpty(service) ? $" [{service}]" : "";
                    Logger.Info("SMTC", $"Média : {(playing ? "▶" : "⏸")} \"{title}\"{serviceLabel} — {artist}  {thumbLabel}  (source={s.SourceAppUserModelId})");
                }
                else
                    Logger.Debug("SMTC", $"Titre vide — app={s.SourceAppUserModelId}");
            }

            _media.UpdateFromSmtc(title, artist, playing, position, duration, thumbUrl);
        }
        finally
        {
            _sem.Release();
            // Si un MediaPropertiesChanged est arrivé PENDANT notre exécution,
            // re-queuer immédiatement pour ne pas manquer la miniature mise à jour.
            if (_pendingThumbRefresh)
                _ = Task.Run(FetchAsync);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tente de récupérer un poster TMDB pour le titre donné.
    /// Ne fait rien si TMDB n'est pas configuré ou si ce n'est pas un service de streaming.
    /// </summary>
    private async Task<string> TryTmdbAsync(string title, string service)
    {
        if (_tmdb == null || !_tmdb.IsConfigured) return "";
        // N'interroger TMDB que pour les services de streaming identifiés
        // (pas pour les lectures locales, Spotify Web, etc.)
        if (string.IsNullOrEmpty(service)) return "";

        return await _tmdb.GetPosterAsync(title);
    }

    private static string StatusLabel(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            return s.GetPlaybackInfo()?.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing  => "playing",
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused   => "paused",
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped  => "stopped",
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => "changing",
                _ => "unknown"
            };
        }
        catch { return "error"; }
    }

    // ─── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        Logger.Info("SMTC", "Fermeture du service SMTC");
        BindSession(null);
        _sem.Dispose();
    }
}
