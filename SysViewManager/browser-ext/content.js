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

  // Cherche la plus grande <img> DOM matching un sélecteur
  function _bestDomImg(sel) {
    var imgs = document.querySelectorAll(sel);
    var best = null, bestArea = 0;
    for (var i = 0; i < imgs.length; i++) {
      var im = imgs[i];
      if (!im.src || im.src.startsWith('data:') || im.src.startsWith('blob:')) continue;
      var area = (im.naturalWidth || im.width || 0) * (im.naturalHeight || im.height || 0);
      if (area > bestArea) { bestArea = area; best = im; }
    }
    return best ? best.src : '';
  }

  // Extrait l'URL depuis background-image CSS d'un élément
  function _cssBgImg(sel) {
    var el = document.querySelector(sel);
    if (!el) return '';
    var bg = window.getComputedStyle(el).backgroundImage || '';
    var m  = bg.match(/url\(["']?([^"')]+)["']?\)/);
    var u  = m ? m[1] : '';
    return (u && !u.startsWith('data:') && !u.startsWith('blob:')) ? u : '';
  }

  // Cherche une image Netflix : img CDN → background-image player → scan global
  function _netflixImg() {
    // 1. <img> sur tous les sous-domaines nflx*
    var url = _bestDomImg('img[src*="nflximg"]') || _bestDomImg('img[src*="nflx"]');
    if (url) return url;
    // 2. background-image sur les éléments connus du player Netflix
    var nfEls = [
      '[data-uia="player-poster"]', '[data-uia="previewModal--boxArt"]',
      '[data-uia="mini-modal-image"]', '.nf-player-container',
      '.watch-video--player-view', '.NFPlayer',
    ];
    for (var i = 0; i < nfEls.length; i++) {
      url = _cssBgImg(nfEls[i]);
      if (url) return url;
    }
    // 3. Scan tous les éléments avec un background-image contenant "nflx"
    var all = document.querySelectorAll('[style*="nflx"]');
    for (var j = 0; j < all.length; j++) {
      url = _cssBgImg('#' + (all[j].id || '__no__'));
      if (!url) {
        var bg = (all[j].style && all[j].style.backgroundImage) || '';
        var m  = bg.match(/url\(["']?([^"')]+)["']?\)/);
        url = m ? m[1] : '';
      }
      if (url && !url.startsWith('data:') && !url.startsWith('blob:')) return url;
    }
    return '';
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
    // Rejeter les titres qui sont juste le nom de la plateforme (ex: "Netflix", "Prime Video : …")
    if (title && _GENERIC.test(title)) title = '';
    // Extraction DOM spécifique par service (Netflix, Prime Video…)
    if (!title) {
      if (/netflix\.com/.test(host)) {
        var ne = document.querySelector('[data-uia="video-title"]')
              || document.querySelector('[data-uia="player-title"]')
              || document.querySelector('.video-title h4');
        if (ne) {
          // L'élément peut contenir plusieurs enfants : [série][épisode]
          // Joindre avec " · " en nettoyant les espaces multiples
          var parts = [];
          var kids = ne.children;
          if (kids.length > 1) {
            for (var ki = 0; ki < kids.length; ki++) {
              var t = kids[ki].textContent.replace(/\s+/g, ' ').trim();
              if (t) parts.push(t);
            }
            title = parts.join(' · ');
          } else {
            // Pas d'enfants : nettoyer les doubles espaces → " · "
            title = ne.textContent.replace(/[ \t]{2,}/g, ' · ').replace(/\s+/g, ' ').trim();
          }
        }
      } else if (/amazon\.com|primevideo\.com/.test(host)) {
        var pe = document.querySelector('.atvwebplayersdk-title-text')
              || document.querySelector('[data-automation-id="title"]');
        if (pe) title = pe.textContent.replace(/\s+/g, ' ').trim();
      }
    }
    // Si aucun titre disponible : utiliser le nom du service comme placeholder
    // (évite un état vide → MediaState efface tout quand title === '')
    if (!title) title = service || host;

    // Fallback image : video.poster (standard HTML5, exposé par certains lecteurs)
    if (!artwork && video && video.poster &&
        !video.poster.startsWith('blob:') && !video.poster.startsWith('data:')) {
      artwork = video.poster;
    }
    // Fallback image DOM (Netflix / Prime Video n'exposent pas d'artwork via mediaSession)
    if (!artwork) {
      if (/netflix\.com/.test(host)) {
        artwork = _netflixImg();
      } else if (/amazon\.com|primevideo\.com/.test(host)) {
        artwork = _bestDomImg('img[src*="m.media-amazon.com"]');
      }
    }

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
