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

    // Sémaphore 1-1 : empêche les FetchAsync concurrents
    // (plusieurs events peuvent arriver simultanément lors d'un changement de piste)
    private readonly SemaphoreSlim _sem = new(1, 1);

    public SmtcService(MediaState media) { _media = media; }

    // ─── Démarrage ───────────────────────────────────────────────────────────

    /// <summary>
    /// Lance le service SMTC. N'échoue jamais — silencieux si l'API est indisponible.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager
                         .RequestAsync()
                         .AsTask(ct);

            _mgr.SessionsChanged += OnSessionsChanged;
            BindSession(_mgr.GetCurrentSession());
            Logger.Info("SMTC initialisé — détection native des médias active");
        }
        catch (OperationCanceledException) { /* fermeture propre */ }
        catch (Exception ex)
        {
            // SMTC non disponible sur ce build Windows ou contexte élevé non supporté.
            // L'extension Chrome reste la source médias — aucun impact fonctionnel.
            Logger.Warn($"SMTC indisponible — fallback extension Chrome ({ex.GetType().Name}: {ex.Message})");
        }
    }

    // ─── Gestion des sessions ────────────────────────────────────────────────

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager mgr,
        SessionsChangedEventArgs _)
        => BindSession(mgr.GetCurrentSession());

    private void BindSession(GlobalSystemMediaTransportControlsSession? s)
    {
        // Désabonnement de l'ancienne session
        if (_session is { } old)
        {
            old.MediaPropertiesChanged    -= OnMediaChanged;
            old.PlaybackInfoChanged       -= OnPlaybackChanged;
            old.TimelinePropertiesChanged -= OnTimelineChanged;
        }

        _session = s;

        if (s == null)
        {
            _media.ClearSmtc();
            return;
        }

        s.MediaPropertiesChanged    += OnMediaChanged;
        s.PlaybackInfoChanged       += OnPlaybackChanged;
        s.TimelinePropertiesChanged += OnTimelineChanged;

        // Lecture initiale de la session fraîchement attachée
        _ = Task.Run(FetchAsync);
    }

    // ─── Handlers d'événements ───────────────────────────────────────────────

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s,
                                 MediaPropertiesChangedEventArgs e)
    { _ = Task.Run(FetchAsync); }

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s,
                                    PlaybackInfoChangedEventArgs e)
    { _ = Task.Run(FetchAsync); }

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s,
                                    TimelinePropertiesChangedEventArgs e)
    { _ = Task.Run(FetchAsync); }

    // ─── Lecture SMTC ────────────────────────────────────────────────────────

    private async Task FetchAsync()
    {
        // Si un FetchAsync est déjà en cours, on abandonne ce déclenchement
        // (il lira l'état le plus récent de toute façon)
        if (!await _sem.WaitAsync(0)) return;
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
            catch { return; /* session fermée entre-temps */ }

            string title  = props?.Title  ?? "";
            string artist = props?.Artist ?? "";
            bool   playing = playback?.PlaybackStatus ==
                             GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            double position = timeline?.Position.TotalSeconds ?? 0;
            double duration = timeline?.EndTime.TotalSeconds  ?? 0;

            // ── Miniature ─────────────────────────────────────────────────────
            // Ré-encodage base64 uniquement lors d'un changement de titre.
            // C'est l'opération coûteuse — 30–80 ms selon la taille JPEG.
            string thumbUrl = "";

            if (!string.IsNullOrEmpty(title))
            {
                if (title == _lastThumbTitle)
                {
                    thumbUrl = _lastThumbUrl;   // cache chaud — zéro travail
                }
                else if (props?.Thumbnail is { } thumb)
                {
                    try
                    {
                        using var ras = await thumb.OpenReadAsync();
                        using var ms  = new System.IO.MemoryStream();
                        await ras.AsStreamForRead().CopyToAsync(ms);
                        thumbUrl        = "data:image/jpeg;base64," +
                                          Convert.ToBase64String(ms.ToArray());
                        _lastThumbTitle = title;
                        _lastThumbUrl   = thumbUrl;
                    }
                    catch
                    {
                        // Miniature illisible — affichage sans image
                        _lastThumbTitle = title;
                        _lastThumbUrl   = "";
                    }
                }
                else
                {
                    // Nouveau titre sans miniature SMTC
                    _lastThumbTitle = title;
                    _lastThumbUrl   = "";
                }
            }
            else
            {
                _lastThumbTitle = "";
                _lastThumbUrl   = "";
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
        BindSession(null);   // désabonne les events
        _sem.Dispose();
    }
}
