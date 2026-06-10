// =============================================================
// content-main.js — world: MAIN
// Lit navigator.mediaSession (contexte page) et le transmet
// à content.js (monde ISOLATED) via un CustomEvent DOM.
// =============================================================
(function () {
  'use strict';

  function dispatch() {
    var s    = navigator.mediaSession;
    var meta = s ? s.metadata : null;
    var art  = [];
    if (meta && meta.artwork) {
      for (var i = 0; i < meta.artwork.length; i++) {
        var a = meta.artwork[i];
        art.push({ src: a.src || '', sizes: a.sizes || '' });
      }
    }
    // Passer une string JSON (primitif) — les objets ne traversent pas
    // la frontière MAIN→ISOLATED de façon fiable via CustomEvent.detail.
    document.dispatchEvent(new CustomEvent('__sysview_session', {
      detail: JSON.stringify({
        title:  (meta && meta.title)  || '',
        artist: (meta && meta.artist) || '',
        artwork: art,
        state:  (s && s.playbackState) || '',
      })
    }));
  }

  setInterval(dispatch, 500);
})();
