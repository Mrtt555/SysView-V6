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
//   ▸ La miniature base64 est ré-encodée UNIQUEMENT si le titre change
//     (évite le goulot d'étranglement identifié avec le bridge Python).
//   ▸ Priorité : extension Chrome (POST /v1/media, active < 5 s) > SMTC
//     → si l'extension est active, les updates SMTC sont ignorées.
//   ▸ Silencieux si SMTC indisponible (processus élevé < Windows 1903,
//     ou build sans API) — l'extension Chrome prend alors le relais.
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

    // État précédent pour détecter les changements (logs)
    private string _lastLoggedTitle  = "";
    private string _lastLoggedAppId  = "";

    // Sémaphore 1-1 : empêche les FetchAsync concurrents
    // (plusieurs events peuvent arriver simultanément lors d'un changement de piste)
    private readonly SemaphoreSlim _sem = new(1, 1);

    public SmtcService(MediaState media)
    {
        _media = media;
        Logger.Info("SMTC", "Service créé");
    }

    // ─── Démarrage ───────────────────────────────────────────────────────────

    /// <summary>
    /// Lance le service SMTC. N'échoue jamais — silencieux si l'API est indisponible.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        Logger.Info("SMTC", "Demande d'accès au gestionnaire SMTC...");
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager
                         .RequestAsync()
                         .AsTask(ct);

            _mgr.SessionsChanged += OnSessionsChanged;
            var current = _mgr.GetCurrentSession();
            Logger.Info("SMTC", $"Gestionnaire SMTC obtenu — session courante : {(current != null ? current.SourceAppUserModelId : "aucune")}");
            BindSession(current);
            Logger.Info("SMTC", "Détection native des médias active (événementiel)");
        }
        catch (OperationCanceledException)
        {
            Logger.Info("SMTC", "Démarrage annulé (CancellationToken)");
        }
        catch (Exception ex)
        {
            // SMTC non disponible sur ce build Windows ou contexte élevé non supporté.
            // L'extension Chrome reste la source médias — aucun impact fonctionnel.
            Logger.Warn("SMTC", $"API SMTC indisponible — fallback extension Chrome");
            Logger.Warn("SMTC", $"  Cause : {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ─── Gestion des sessions ────────────────────────────────────────────────

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager mgr,
        SessionsChangedEventArgs _)
    {
        var s = mgr.GetCurrentSession();
        Logger.Info("SMTC", $"SessionsChanged — nouvelle session : {(s != null ? s.SourceAppUserModelId : "aucune")}");
        BindSession(s);
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
        Logger.Info("SMTC", $"Session attachée : {s.SourceAppUserModelId}");

        // Lecture initiale de la session fraîchement attachée
        _ = Task.Run(FetchAsync);
    }

    // ─── Handlers d'événements ───────────────────────────────────────────────

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s,
                                 MediaPropertiesChangedEventArgs e)
    {
        Logger.Debug("SMTC", $"MediaPropertiesChanged — app={s.SourceAppUserModelId}");
        _ = Task.Run(FetchAsync);
    }

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s,
                                    PlaybackInfoChangedEventArgs e)
    {
        Logger.Debug("SMTC", $"PlaybackInfoChanged — app={s.SourceAppUserModelId}");
        _ = Task.Run(FetchAsync);
    }

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s,
                                    TimelinePropertiesChangedEventArgs e)
    {
        Logger.Debug("SMTC", $"TimelinePropertiesChanged — app={s.SourceAppUserModelId}");
        _ = Task.Run(FetchAsync);
    }

    // ─── Lecture SMTC ────────────────────────────────────────────────────────

    private async Task FetchAsync()
    {
        // Si un FetchAsync est déjà en cours, on abandonne ce déclenchement
        // (il lira l'état le plus récent de toute façon)
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
            // Ré-encodage base64 uniquement lors d'un changement de titre.
            string thumbUrl = "";
            bool   thumbFromCache = false;

            if (!string.IsNullOrEmpty(title))
            {
                if (title == _lastThumbTitle)
                {
                    thumbUrl      = _lastThumbUrl;   // cache chaud — zéro travail
                    thumbFromCache = true;
                }
                else if (props?.Thumbnail is { } thumb)
                {
                    var swThumb = Stopwatch.StartNew();
                    try
                    {
                        using var ras = await thumb.OpenReadAsync();
                        using var ms  = new System.IO.MemoryStream();
                        await ras.AsStreamForRead().CopyToAsync(ms);
                        thumbUrl        = "data:image/jpeg;base64," +
                                          Convert.ToBase64String(ms.ToArray());
                        _lastThumbTitle = title;
                        _lastThumbUrl   = thumbUrl;
                        swThumb.Stop();
                        Logger.Debug("SMTC", $"  Miniature encodée en {swThumb.ElapsedMilliseconds} ms ({ms.Length / 1024} Ko)");
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
                    // Nouveau titre sans miniature SMTC
                    Logger.Debug("SMTC", $"  Pas de miniature pour \"{title}\"");
                    _lastThumbTitle = title;
                    _lastThumbUrl   = "";
                }
            }
            else
            {
                _lastThumbTitle = "";
                _lastThumbUrl   = "";
            }

            // ── Log si le titre a changé ──────────────────────────────────────
            if (title != _lastLoggedTitle || s.SourceAppUserModelId != _lastLoggedAppId)
            {
                _lastLoggedTitle = title;
                _lastLoggedAppId = s.SourceAppUserModelId;
                if (!string.IsNullOrEmpty(title))
                    Logger.Info("SMTC", $"Média détecté : {(playing ? "▶" : "⏸")} \"{title}\" — {artist}"
                        + (thumbFromCache ? " [miniature: cache]"
                          : !string.IsNullOrEmpty(thumbUrl) ? " [miniature: encodée]"
                          : " [sans miniature]"));
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

    // ─── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        Logger.Info("SMTC", "Fermeture du service SMTC");
        BindSession(null);   // désabonne les events
        _sem.Dispose();
    }
}
