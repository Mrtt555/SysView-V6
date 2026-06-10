// =============================================================
// content.js — SysView Media Bridge (world: ISOLATED)
// Reçoit les métadonnées mediaSession depuis content-main.js,
// lit les éléments <video>, et pousse vers le service worker.
// =============================================================
(function () {
  'use strict';

  // ── Mapping hostname → nom du service ──────────────────────
  var SVC = {
    'netflix.com':        'Netflix',
    'disneyplus.com':     'Disney+',
    'primevideo.com':     'Prime Video',
    'amazon.com':         'Prime Video',
    'hulu.com':           'Hulu',
    'max.com':            'Max',
    'youtube.com':        'YouTube',
    'music.youtube.com':  'YouTube Music',
    'spotify.com':        'Spotify',
    'soundcloud.com':     'SoundCloud',
    'deezer.com':         'Deezer',
    'tidal.com':          'Tidal',
    'crunchyroll.com':    'Crunchyroll',
    'twitch.tv':          'Twitch',
    'vimeo.com':          'Vimeo',
    'dailymotion.com':    'Dailymotion',
    'peacocktv.com':      'Peacock',
    'paramountplus.com':  'Paramount+',
    'appletv.apple.com':  'Apple TV+',
    'skyshowtime.com':    'SkyShowtime',
    'mubi.com':           'MUBI',
    'funimation.com':     'Funimation',
    'mycanal.fr':         'Canal+',
    'tf1.fr':             'TF1+',
    'france.tv':          'France.tv',
    'arte.tv':            'ARTE',
    'app.plex.tv':        'Plex',
    'app.emby.media':     'Emby',
    'jellyfin.media':     'Jellyfin',
  };

  function detectService(host) {
    if (SVC[host]) return SVC[host];
    var parts = host.split('.');
    for (var i = 1; i < parts.length - 1; i++) {
      var k = parts.slice(i).join('.');
      if (SVC[k]) return SVC[k];
    }
    var appMeta = document.querySelector('meta[name="application-name"]');
    if (appMeta && appMeta.content === 'Jellyfin') return 'Jellyfin';
    if (appMeta && appMeta.content === 'Emby')     return 'Emby';
    return '';
  }

  function bestVideo() {
    var videos = document.querySelectorAll('video');
    var best = null;
    for (var i = 0; i < videos.length; i++) {
      var v = videos[i];
      // HAVE_NOTHING → pas encore chargé, ignorer
      if (v.readyState < 1) continue;
      if (!best) { best = v; continue; }
      // 1. Lecture active > pause
      if (!v.paused && best.paused)  { best = v; continue; }
      if (v.paused  && !best.paused) continue;
      // 2. Position la plus avancée → c'est le contenu principal
      //    (les previews/banners démarrent à 0 ; le vrai film est à N minutes)
      var vt = v.currentTime   || 0;
      var bt = best.currentTime || 0;
      if (vt > bt) { best = v; continue; }
      if (vt < bt) continue;
      // 3. Plus grande résolution (contenu HD > vignette)
      var va = (v.videoWidth    || 0) * (v.videoHeight    || 0);
      var ba = (best.videoWidth || 0) * (best.videoHeight || 0);
      if (va > ba) { best = v; continue; }
      if (va < ba) continue;
      // 4. Durée la plus longue
      if ((v.duration || 0) > (best.duration || 0)) best = v;
    }
    return best;
  }

  // Titres purement génériques (nom de la plateforme seul, sans nom d'émission)
  var _GENERIC = /^(Netflix|Disney\+|Prime Video|Amazon Prime Video|Hulu|Max|YouTube|YouTube Music|Spotify|Deezer|Tidal|SoundCloud|Crunchyroll|Twitch|Vimeo|Dailymotion|Peacock|Paramount\+|Apple TV\+|Plex|Emby|Jellyfin|Canal\+|TF1\+|ARTE|France\.tv|MUBI)([\s :,]|$)/i;

  function bestArtwork(artworkList) {
    if (!artworkList || !artworkList.length) return '';
    var best = artworkList[0];
    for (var i = 1; i < artworkList.length; i++) {
      var a = artworkList[i];
      var w  = parseInt((a.sizes    || '0x0').split('x')[0]) || 0;
      var bw = parseInt((best.sizes || '0x0').split('x')[0]) || 0;
      if (w > bw) best = a;
    }
    var src = best.src || '';
    if (src.startsWith('blob:') || src.startsWith('data:')) return '';
    return src;
  }

  // ── Données mediaSession reçues depuis content-main.js ─────
  // detail est une string JSON (les objets ne traversent pas la frontière
  // MAIN→ISOLATED de façon fiable — le primitif string passe toujours).
  var _session = { title: '', artist: '', artwork: [], state: '' };
  document.addEventListener('__sysview_session', function (e) {
    try {
      var d = JSON.parse(e.detail);
      if (d && typeof d === 'object') _session = d;
    } catch (err) {}
  });

  var _lastKey  = '';
  var _lastSent = 0;

  function poll() {
    var host    = window.location.hostname;
    var service = detectService(host);
    var video   = bestVideo();
    var title   = _session.title;
    var artist  = _session.artist;
    var artwork = bestArtwork(_session.artwork);

    // Aucun média sur cette page
    if (!video && !title) {
      if (_lastKey !== 'empty') {
        _lastKey  = 'empty';
        _lastSent = 0;
        try { chrome.runtime.sendMessage({ type: 'no_media' }); } catch (e) {}
      }
      return;
    }

    var playing = video
      ? !video.paused
      : (_session.state === 'playing');

    var pos = video ? Math.round(video.currentTime) : 0;
    var dur = (video && isFinite(video.duration) && video.duration > 0)
            ? Math.round(video.duration)
            : 0;

    // Fallback titre depuis document.title (sans le suffixe " | Netflix" etc.)
    if (!title && document.title) {
      title = document.title
        .replace(/\s*[|–—]\s*(Disney\+|Netflix|Prime Video|Amazon|Hulu|Max|YouTube|YouTube Music|Spotify|Deezer|Tidal|SoundCloud|Crunchyroll|Twitch|Vimeo|Dailymotion|Peacock|Paramount\+|Apple TV\+|Plex|Emby|Jellyfin|Canal\+|TF1\+|ARTE|France\.tv|MUBI)\s*$/i, '')
        .trim() || document.title;
    }
    // Rejeter les titres qui sont juste le nom de la plateforme
    if (title && _GENERIC.test(title)) title = '';

    var msg = {
      type:     'media',
      title:    title,
      artist:   artist,
      artwork:  artwork,
      service:  service,
      host:     host,
      position: pos,
      duration: dur,
      playing:  playing,
    };

    var key = msg.title + '|' + msg.playing + '|' + msg.position;
    var now = Date.now();
    if (key === _lastKey && (now - _lastSent) < 2000) return;
    _lastKey  = key;
    _lastSent = now;

    try { chrome.runtime.sendMessage(msg); } catch (e) {}
  }

  // Polling 500ms + événements vidéo
  setInterval(poll, 500);
  document.addEventListener('play',           poll, true);
  document.addEventListener('pause',          poll, true);
  document.addEventListener('ended',          poll, true);
  document.addEventListener('durationchange', poll, true);
  document.addEventListener('seeked',         poll, true);
})();
