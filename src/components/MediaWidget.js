// =============================================================
// MediaWidget.js — SysView V6
// Rendu et logique du panneau média (SMTC / album art / progress).
// Exports : fmtArtist, fmtTime, renderProgress, setAlbumArt, showIdleAnim
// =============================================================

// ─── Services connus (ne pas décomposer le nom) ───────────────
var KNOWN_SVC = [
  'Netflix','Prime Video','Disney+','Max','Hulu','Crunchyroll',
  'Apple TV+','Plex','Emby','Jellyfin','Peacock','Paramount+',
  'MUBI','Shudder','Tubi','SkyShowtime','Funimation','HBO Max'
];

// ─── Formate le nom d'artiste SMTC ───────────────────────────
// Nettoie les suffixes parasites (VEVO, Topic) et les canaux sans
// espace (ex: "NatashaSt.PierreVEVO") en extrayant "Artiste - Titre".
export function fmtArtist(artist, title) {
  if (!artist) return '';
  if (KNOWN_SVC.indexOf(artist) >= 0) return artist;
  var noSpaces = artist.indexOf(' ') < 0;
  var isVevo   = artist.slice(-4).toUpperCase() === 'VEVO';
  var isTopic  = artist.slice(-8) === ' - Topic';
  if (noSpaces || isVevo || isTopic) {
    var dash = title ? title.indexOf(' - ') : -1;
    if (dash > 0 && dash < 60) return title.substring(0, dash);
  }
  return artist;
}

// ─── Formate une durée en secondes → "m:ss" ou "h:mm:ss" ─────
export function fmtTime(sec) {
  sec = Math.floor(Math.max(0, sec) || 0);
  var h = Math.floor(sec / 3600);
  var m = Math.floor((sec % 3600) / 60);
  var s = sec % 60;
  function pad(n) { return String(n).padStart(2, '0'); }
  if (h > 0) return h + ':' + pad(m) + ':' + pad(s);
  return m + ':' + pad(s);
}

// ─── Met à jour la barre de progression et les timestamps ─────
export function renderProgress(snap, mediaDur, mediaPos, bridgeOk) {
  var bar   = document.getElementById('prog-fill');
  var telEl = document.getElementById('time-el');
  var ttEl  = document.getElementById('time-tot');
  if (!bar) return;

  if (mediaDur <= 0) {
    if (snap) {
      bar.style.transition = 'none';
      bar.style.width = '0%';
      if (telEl) telEl.textContent = '0:00';
      if (ttEl)  ttEl.textContent  = '0:00';
    }
    return;
  }

  var pct = Math.min(100, mediaPos / mediaDur * 100);
  bar.style.transition = (snap || !bridgeOk) ? 'none' : 'width 1s linear';
  bar.style.width = pct.toFixed(2) + '%';
  if (telEl) telEl.textContent = fmtTime(mediaPos);
  if (ttEl)  ttEl.textContent  = fmtTime(mediaDur);
}

// ─── Charge l'image d'album art avec fondu ───────────────────
export function setAlbumArt(src) {
  var img = document.getElementById('media-art-img');
  var ph  = document.getElementById('media-icon-ph');
  if (!img) return;
  img.style.opacity = 0;
  img.onload  = function() { img.style.opacity = 1; if (ph) ph.style.display = 'none'; };
  img.onerror = function() { img.style.opacity = 0; if (ph) ph.style.display = 'flex'; };
  if (img.src === src) img.src = '';
  img.src = src;
}

// ─── Affiche l'animation idle (barres rebondissantes) ─────────
export function showIdleAnim() {
  var ph = document.getElementById('media-icon-ph');
  if (!ph) return;
  var bars = ph.querySelectorAll('.nm-bar');
  for (var i = 0; i < bars.length; i++) bars[i].style.animation = 'none';
  ph.style.display = 'flex';
  setTimeout(function() {
    for (var i = 0; i < bars.length; i++) bars[i].style.animation = '';
  }, 50);
}
