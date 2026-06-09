/* SysView Media Bridge — Content Script v5
   Utilise document.hidden pour detecter l'onglet actif.
   Compatible tous sites avec balise <video> HTML5. */

var _ticker  = null;
var _lastKey = '';
var _dead    = false;   /* true apres invalidation du contexte — bloque tout restart */

var BRIDGE_URL = 'http://127.0.0.1:5001/v1/media';

/* ── Envoi direct au bridge (fallback sans service worker) ───────────────── */
function _fetchBridge(payload) {
  fetch(BRIDGE_URL, {
    method: 'POST',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({
      title:     payload.title     || '',
      artist:    payload.artist    || '',
      position:  payload.position  || 0,
      duration:  payload.duration  || 0,
      playing:   !!payload.playing,
      thumb_url: payload.thumb_url || '',
      source:    payload.source    || ''
    })
  }).catch(function(){});
}

/* ── Envoi securise (gere Extension context invalidated et SW endormi) ───── */
function safeSend(msg) {
  try {
    if (!chrome.runtime || !chrome.runtime.id) {
      /* Contexte extension entierement invalide → arret definitif */
      _dead = true;
      if (_ticker) { clearInterval(_ticker); _ticker = null; }
      return;
    }
    chrome.runtime.sendMessage(msg);
  } catch(e) {
    /* sendMessage a echoue — deux cas possibles :
       1. SW endormi par Chrome (chrome.runtime.id encore present) → fallback fetch direct,
          le ticker continue (le SW se reveillera au prochain message entrant).
       2. Contexte invalide (extension desactivee, onglet en fermeture) → arret definitif. */
    if (chrome.runtime && chrome.runtime.id) {
      _fetchBridge(msg);   /* SW endormi : on envoie directement */
    } else {
      _dead = true;
      if (_ticker) { clearInterval(_ticker); _ticker = null; }
    }
  }
}

/* ── Onglet actif : uniquement document.hidden (fiable en content script) ── */
function isActive() {
  return !document.hidden;
}

/* ── Meilleure video de la page ──────────────────────────────────────────── */
function getBestVideo() {
  if (location.hostname.includes('youtube.com') && !location.hostname.includes('music.youtube.com')) {
    var ytVid = document.querySelector('#movie_player video');
    if (ytVid && isFinite(ytVid.duration) && ytVid.duration > 0) return ytVid;
  }
  var videos = Array.from(document.querySelectorAll('video, audio'));
  if (!videos.length) return null;
  var playing = videos.filter(function(v) {
    return !v.paused && !v.ended && v.readyState >= 2
        && isFinite(v.duration) && v.duration > 0;
  });
  if (playing.length)
    return playing.sort(function(a,b){ return b.duration - a.duration; })[0];
  return videos.filter(function(v){ return isFinite(v.duration) && v.duration > 0; })
               .sort(function(a,b){ return b.duration - a.duration; })[0] || null;
}

/* ── Conversion mm:ss ou h:mm:ss en secondes (fallback DOM YouTube) ───── */
function parseTime(s) {
  var parts = (s || '').trim().split(':').map(Number);
  if (parts.length === 3) return parts[0] * 3600 + parts[1] * 60 + parts[2];
  if (parts.length === 2) return parts[0] * 60  + parts[1];
  return 0;
}

/* ── Titre ───────────────────────────────────────────────────────────────── */
function getTitle() {
  var ms = navigator.mediaSession;
  if (ms && ms.metadata && ms.metadata.title) return ms.metadata.title;
  var host = location.hostname;
  var el;
  if (host.includes('youtube.com') && !host.includes('music.youtube.com')) {
    el = document.querySelector('h1.ytd-watch-metadata yt-formatted-string')
      || document.querySelector('#title h1 yt-formatted-string');
    if (el && el.textContent.trim()) return el.textContent.trim();
  }
  if (host.includes('music.youtube.com')) {
    el = document.querySelector('.ytmusic-player-bar .title');
    if (el) return el.textContent.trim();
  }
  if (host.includes('twitch.tv')) {
    el = document.querySelector('[data-a-target="stream-title"]');
    if (el) return el.textContent.trim();
  }
  if (host.includes('netflix.com')) {
    el = document.querySelector('[data-uia="video-title"]')
      || document.querySelector('.watch-title');
    if (el) return el.textContent.trim();
  }
  if (host.includes('vimeo.com')) {
    el = document.querySelector('.clip_info--title');
    if (el) return el.textContent.trim();
  }
  var t = document.title || '';
  t = t.replace(/ [-|]+ (YouTube|Twitch|Netflix|Vimeo|Dailymotion|Plex|Emby|Jellyfin)$/i, '');
  t = t.replace(/^\(\d+\) /, '').replace(/\s+/g, ' ').trim();
  return t || location.hostname;
}

/* ── Artiste ─────────────────────────────────────────────────────────────── */
function getArtist() {
  var ms = navigator.mediaSession;
  if (ms && ms.metadata && ms.metadata.artist) return ms.metadata.artist;
  var host = location.hostname;
  var el;
  if (host.includes('youtube.com') && !host.includes('music.youtube.com')) {
    el = document.querySelector('#top-row ytd-channel-name a')
      || document.querySelector('#channel-name a');
    if (el) return el.textContent.trim();
  }
  if (host.includes('music.youtube.com')) {
    el = document.querySelector('.ytmusic-player-bar .byline a');
    if (el) return el.textContent.trim();
  }
  if (host.includes('twitch.tv')) {
    el = document.querySelector('a[data-a-target="stripped-layout-header-channel-link"] h1');
    if (el) return el.textContent.trim();
  }
  if (host.includes('vimeo.com')) {
    el = document.querySelector('.clip_info--author a');
    if (el) return el.textContent.trim();
  }
  return location.hostname.replace(/^www\./, '');
}

/* ── Miniature ───────────────────────────────────────────────────────────── */
function getThumbUrl(video) {
  var ms = navigator.mediaSession;
  if (ms && ms.metadata && ms.metadata.artwork && ms.metadata.artwork.length) {
    var artSrc = ms.metadata.artwork[ms.metadata.artwork.length - 1].src;
    if (artSrc && !artSrc.startsWith('blob:') && !artSrc.startsWith('data:'))
      return artSrc;
  }
  var host = location.hostname;
  var match, el;
  if ((host.includes('youtube.com') && !host.includes('music.youtube.com')) || host.includes('youtu.be')) {
    match = location.href.match(/[?&]v=([A-Za-z0-9_-]{11})/)
         || location.href.match(/youtu\.be\/([A-Za-z0-9_-]{11})/)
         || location.href.match(/\/shorts\/([A-Za-z0-9_-]{11})/);  /* YouTube Shorts */
    if (match) return 'https://img.youtube.com/vi/' + match[1] + '/mqdefault.jpg';
  }
  if (host.includes('music.youtube.com')) {
    el = document.querySelector('.ytmusic-player-bar img.thumbnail');
    if (el && el.src) return el.src;
  }
  if (host.includes('twitch.tv')) {
    el = document.querySelector('[data-a-target="user-avatar"]');
    if (el && el.src) return el.src;
  }
  /* Netflix : video.poster ou twitter:image — plus précis que og:image */
  if (host.includes('netflix.com')) {
    if (video && video.poster) return video.poster;
    el = document.querySelector('meta[name="twitter:image"]');
    if (el && el.content) return el.content;
    el = document.querySelector('meta[property="og:image"]');
    if (el && el.content) return el.content;
    return '';
  }
  /* Prime Video : poster de la balise video, puis sélecteurs spécifiques */
  if (host.includes('primevideo.com') || host.includes('amazon.com')) {
    if (video && video.poster) return video.poster;
    el = document.querySelector('[data-testid="artwork-image"] img')
      || document.querySelector('img[class*="packshot"]')
      || document.querySelector('img[class*="Packshot"]')
      || document.querySelector('.scalable-image img');
    if (el && el.src && !el.src.endsWith('transparent-pixel.png')) return el.src;
    el = document.querySelector('meta[name="twitter:image"]');
    if (el && el.content) return el.content;
    el = document.querySelector('meta[property="og:image"]');
    if (el && el.content) return el.content;
    return '';
  }
  /* Autres sites : video.poster d'abord (plus spécifique que og:image) */
  if (video && video.poster) return video.poster;
  var meta = document.querySelector('meta[property="og:image"]')
          || document.querySelector('meta[name="twitter:image"]');
  if (meta && meta.content) return meta.content;
  return '';
}

/* ── Envoi ───────────────────────────────────────────────────────────────── */
function sendData() {
  /* Tout le corps est dans un try-catch : quand Chrome invalide le contexte
     d'extension, n'importe quelle ligne peut lever "Extension context
     invalidated" — pas seulement sendMessage. On stoppe le ticker proprement. */
  try {
    if (!chrome.runtime || !chrome.runtime.id) {
      if (_ticker) { clearInterval(_ticker); _ticker = null; }
      return;
    }

    var video  = getBestVideo();
    var ms     = navigator.mediaSession;
    var msMeta = ms && ms.metadata;
    if (!video && !(msMeta && msMeta.title)) return;

    var title = getTitle();
    if (!title) return;

    var playing  = video ? (!video.paused && !video.ended)
                         : (ms ? ms.playbackState === 'playing' : false);
    /* Onglet en arriere-plan : envoyer uniquement si lecture active
       (permet les mises a jour de piste/miniature quand le navigateur est minimise).
       Si rien ne joue et que l'onglet est cache, ignorer pour ne pas
       ecraser un onglet actif avec des donnees perimees. */
    if (!isActive() && !playing) return;
    /* Position en float pour interpolation cote bridge */
    var position = video ? video.currentTime : 0;
    var duration = video ? (isFinite(video.duration) ? video.duration : 0) : 0;

    /* YouTube : fallback DOM si video non encore initialisee */
    if (location.hostname.includes('youtube.com') && !location.hostname.includes('music.youtube.com') && !duration) {
      var ytCur = document.querySelector('.ytp-time-current');
      var ytDur = document.querySelector('.ytp-time-duration');
      if (ytDur && ytDur.textContent.trim()) duration = parseTime(ytDur.textContent);
      if (ytCur && ytCur.textContent.trim()) position = parseTime(ytCur.textContent);
      if (!playing && ms) playing = ms.playbackState === 'playing';
    }

    /* Deduplication sur position arrondie a la seconde pour envoyer ~1/s */
    var key = title + '|' + Math.floor(position) + '|' + playing;
    if (key === _lastKey) return;
    _lastKey = key;

    safeSend({
      type:      'MEDIA_UPDATE',
      title:      title,
      artist:     getArtist(),
      position:   position,
      duration:   duration,
      playing:    playing,
      thumb_url:  getThumbUrl(video),
      source:     location.hostname
    });

  } catch(e) {
    /* Contexte invalide ou autre erreur -> arret propre du ticker */
    _dead = true;
    if (_ticker) { clearInterval(_ticker); _ticker = null; }
  }
}

/* ── Reset quand onglet passe en arriere-plan ────────────────────────────── */
document.addEventListener('visibilitychange', function() {
  if (document.hidden) _lastKey = '';
});

/* ── Demarrage ───────────────────────────────────────────────────────────── */
function start() {
  if (_dead) return;
  if (_ticker) clearInterval(_ticker);
  _ticker = setInterval(sendData, 1000);
}

function stop() {
  if (_ticker) { clearInterval(_ticker); _ticker = null; }
  var clearPayload = {
    title:'', artist:'', position:0, duration:0,
    playing:false, thumb_url:'', source:''
  };
  if (!_dead) {
    /* safeSend peut mettre _dead=true si le contexte s'invalide pendant l'appel */
    safeSend(Object.assign({type:'MEDIA_UPDATE'}, clearPayload));
  }
  /* Si _dead est vrai (avant OU apres l'appel safeSend), on envoie directement
     au bridge pour s'assurer qu'il efface l'etat media. */
  if (_dead) {
    _fetchBridge(clearPayload);
  }
}

if (getBestVideo() || (navigator.mediaSession && navigator.mediaSession.metadata)) {
  start();
} else {
  var obs = new MutationObserver(function() {
    /* Démarrer dès qu'une vidéo apparaît OU qu'une métadonnée audio est disponible
       (ex. Spotify Web, podcasts) — évite que les pages audio-only restent silencieuses */
    if (getBestVideo() || (navigator.mediaSession && navigator.mediaSession.metadata)) {
      start(); obs.disconnect();
    }
  });
  obs.observe(document.body || document.documentElement, { childList: true, subtree: true });
}

window.addEventListener('yt-navigate-finish', function() { _lastKey = ''; start(); });
/* Netflix, Prime, Jellyfin, etc. : navigation SPA via History API */
window.addEventListener('popstate',    function() { _lastKey = ''; start(); });
window.addEventListener('hashchange',  function() { _lastKey = ''; });
window.addEventListener('beforeunload', stop);
