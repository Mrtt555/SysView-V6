// =============================================================
// MediaWidget.js — SysView V6
// Rendu et logique du panneau média (SMTC / album art / progress).
// Exports : fmtArtist, fmtTime, renderProgress, setAlbumArt, showIdleAnim
// =============================================================
import { fmtTime } from './MonitoringWidget.js';
export { fmtTime }; // ré-exporté pour les consommateurs (app.js)

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
  var isTopic  = artist.slice(-8).toLowerCase() === ' - topic';
  if (noSpaces || isVevo || isTopic) {
    var dash = title ? title.indexOf(' - ') : -1;
    if (dash > 0 && dash < 60) return title.substring(0, dash);
  }
  return artist;
}

// ─── Met à jour la barre de progression et les timestamps ─────
export function renderProgress(snap, mediaDur, mediaPos, bridgeOk) {
  var bar   = document.getElementById('prog-fill');
  var telEl = document.getElementById('time-el');
  var ttEl  = document.getElementById('time-tot');
  if (!bar) return;

  var progRow = bar.closest('.mprog');
  if (mediaDur <= 0) {
    if (snap) {
      bar.style.width = '0%';
      if (mediaPos > 0) {
        // Durée inconnue (DRM) mais position disponible → afficher le temps écoulé
        if (telEl) telEl.textContent = fmtTime(mediaPos);
        if (ttEl)  ttEl.textContent  = '—:—';
        if (progRow) progRow.style.visibility = '';
      } else {
        if (telEl) telEl.textContent = '';
        if (ttEl)  ttEl.textContent  = '';
        if (progRow) progRow.style.visibility = 'hidden';
      }
    }
    return;
  }
  if (progRow) progRow.style.visibility = '';

  var pct = Math.min(100, mediaPos / mediaDur * 100);
  bar.style.transition = 'none';
  bar.style.width = pct.toFixed(2) + '%';
  if (telEl) telEl.textContent = fmtTime(mediaPos);
  if (ttEl)  ttEl.textContent  = fmtTime(mediaDur);
}

// ─── Charge l'image d'album art avec fondu ───────────────────
var _artGen = 0; // génération courante — évite l'effet de course si appelé deux fois rapidement

export function setAlbumArt(src) {
  var img = document.getElementById('media-art-img');
  var ph  = document.getElementById('media-icon-ph');
  if (!img) return;
  var gen = ++_artGen;
  img.style.opacity = 0;
  // Les callbacks vérifient _artGen pour ignorer les chargements devenus obsolètes
  img.onload  = function() { if (_artGen === gen) { img.style.opacity = 1; if (ph) ph.style.display = 'none'; } };
  img.onerror = function() { if (_artGen === gen) { img.style.opacity = 0; if (ph) ph.style.display = 'flex'; } };
  // Ne pas remettre src à '' (déclencherait onerror et un flash) — le navigateur utilise le cache si même URL
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
