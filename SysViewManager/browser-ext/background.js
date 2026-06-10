// =============================================================
// background.js — SysView Media Bridge (service worker MV3)
// Reçoit les données de content.js, sélectionne le meilleur
// onglet actif, et pousse vers l'API SysViewManager.
// =============================================================
'use strict';

const API = 'http://127.0.0.1:5001/v1/media/ext';

// État par onglet : { tabId, title, artist, artwork, service, host, position, duration, playing, ts }
var _tabs = {};

// ── Réception des messages des content scripts ──────────────
chrome.runtime.onMessage.addListener(function (msg, sender, sendResponse) {
  var tabId = sender.tab ? sender.tab.id : null;
  if (tabId === null) { sendResponse({}); return false; }

  if (msg.type === 'no_media') {
    delete _tabs[tabId];
  } else if (msg.type === 'media') {
    _tabs[tabId] = Object.assign({}, msg, { tabId: tabId, ts: Date.now() });
  }

  push();
  sendResponse({});
  return false; // synchrone
});

// Nettoyage quand un onglet est fermé
chrome.tabs.onRemoved.addListener(function (tabId) {
  if (_tabs[tabId]) {
    delete _tabs[tabId];
    push();
  }
});

// ── Sélection du meilleur onglet ────────────────────────────
function bestTab() {
  var best = null;
  var now = Date.now();
  for (var id in _tabs) {
    var t = _tabs[id];
    // Ignorer les données > 3 s (onglet inactif / service worker redémarré)
    if (now - t.ts > 3000) { delete _tabs[id]; continue; }
    if (!best) { best = t; continue; }
    // Préférer la lecture active
    if (t.playing && !best.playing) { best = t; continue; }
    if (!t.playing && best.playing)  continue;
    // Préférer le contenu le plus long (film > clip)
    if ((t.duration || 0) > (best.duration || 0)) best = t;
  }
  return best;
}

// ── Envoi vers SysViewManager ───────────────────────────────
function push() {
  var m = bestTab();
  fetch(API, {
    method:  'POST',
    headers: { 'Content-Type': 'application/json' },
    body:    JSON.stringify(m || { type: 'no_media' }),
  }).catch(function () {
    // SysViewManager non démarré → silencieux
  });
}
