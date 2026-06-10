// =============================================================
// content.js — SysView Media Bridge (world: MAIN)
// Lit navigator.mediaSession et les éléments <video> de la page,
// puis envoie les données au service worker via chrome.runtime.
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
    // Test sous-domaines (ex: www.netflix.com → netflix.com)
    var parts = host.split('.');
    for (var i = 1; i < parts.length - 1; i++) {
      var k = parts.slice(i).join('.');
      if (SVC[k]) return SVC[k];
    }
    // Jellyfin auto-hébergé : méta application-name = "Jellyfin"
    var appMeta = document.querySelector('meta[name="application-name"]');
    if (appMeta && appMeta.content === 'Jellyfin') return 'Jellyfin';
    // Emby auto-hébergé
    if (appMeta && appMeta.content === 'Emby') return 'Emby';
    return '';
  }

  function bestVideo() {
    var videos = document.querySelectorAll('video');
    var best = null;
    for (var i = 0; i < videos.length; i++) {
      var v = videos[i];
      if (!v.src && !v.currentSrc) continue;
      if (!best) { best = v; continue; }
      // Préférer la vidéo en cours de lecture, puis la plus longue
      if (!v.paused && best.paused)               { best = v; continue; }
      if (v.paused && !best.paused)               continue;
      if ((v.duration || 0) > (best.duration || 0)) best = v;
    }
    return best;
  }

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
    // Ignorer les blob: URLs (non transférables) et les data: (trop lourds)
    if (src.startsWith('blob:') || src.startsWith('data:')) return '';
    return src;
  }

  var _lastKey = '';

  function poll() {
    var host    = window.location.hostname;
    var service = detectService(host);
    var video   = bestVideo();
    var session = navigator.mediaSession;
    var meta    = session ? session.metadata : null;

    // Aucun média sur cette page
    if (!video && !meta) {
      if (_lastKey !== 'empty') {
        _lastKey = 'empty';
        try { chrome.runtime.sendMessage({ type: 'no_media' }); } catch (e) {}
      }
      return;
    }

    var playing = video
      ? !video.paused
      : (session && session.playbackState === 'playing');

    var title   = (meta && meta.title)  || '';
    var artist  = (meta && meta.artist) || '';
    var artwork = bestArtwork(meta && meta.artwork);
    var pos     = video ? Math.round(video.currentTime) : 0;
    var dur     = (video && isFinite(video.duration) && video.duration > 0)
                ? Math.round(video.duration)
                : 0;

    // Fallback titre depuis le titre de la page
    if (!title) title = document.title || '';

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

    // N'envoyer que si quelque chose a changé (évite le spam inutile)
    var key = msg.title + '|' + msg.playing + '|' + msg.position;
    if (key === _lastKey) return;
    _lastKey = key;

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
