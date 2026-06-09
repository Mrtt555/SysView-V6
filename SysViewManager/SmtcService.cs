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
    private readonly MediaState _media;

    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;

    // Cache miniature — évite le ré-encodage base64 si même titre
    private string _lastThumbTitle = "";
    private string _lastThumbUrl   = "";

    // État précédent pour logs différentiels
    private string _lastLoggedTitle  = "";
    private string _lastLoggedAppId  = "";

    // Sémaphore 1-1 : empêche les FetchAsync concurrents
    private readonly SemaphoreSlim _sem = new(1, 1);

    public SmtcService(MediaState media)
    {
        _media = media;
        Logger.Info("SMTC", "Service créé");
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

            string title  = props?.Title  ?? "";
            string artist = props?.Artist ?? "";
            bool   playing = playback?.PlaybackStatus ==
                             GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            double position = timeline?.Position.TotalSeconds ?? 0;
            double duration = timeline?.EndTime.TotalSeconds  ?? 0;

            // ── Miniature ─────────────────────────────────────────────────────
            string thumbUrl       = "";
            bool   thumbFromCache = false;

            if (!string.IsNullOrEmpty(title))
            {
                if (title == _lastThumbTitle)
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
                            _lastThumbTitle = title;
                            _lastThumbUrl   = "";
                        }
                    }
                    catch (Exception ex)
                    {
                        swThumb.Stop();
                        Logger.Warn("SMTC", $"  Miniature illisible ({swThumb.ElapsedMilliseconds} ms) : {ex.Message}");
                        _lastThumbTitle = title;
                        _lastThumbUrl   = "";
                    }
                }
                else
                {
                    // Navigateur/YouTube : pas de miniature via SMTC (limitation API web)
                    Logger.Debug("SMTC", $"  Pas de miniature pour \"{title}\" (source={s.SourceAppUserModelId})");
                    _lastThumbTitle = title;
                    _lastThumbUrl   = "";
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
                    Logger.Info("SMTC", $"Média : {(playing ? "▶" : "⏸")} \"{title}\" — {artist}"
                        + (thumbFromCache ? " [miniature: cache]"
                          : !string.IsNullOrEmpty(thumbUrl) ? " [miniature: encodée]"
                          : " [sans miniature]")
                        + $"  (source={s.SourceAppUserModelId})");
                else
                    Logger.Debug("SMTC", $"Titre vide — app={s.SourceAppUserModelId}");
            }

            _media.UpdateFromSmtc(title, artist, playing, position, duration, thumbUrl);
        }
        finally
        {
            _sem.Release();
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

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
