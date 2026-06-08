/* SysView — Background Service Worker v5
   Recoit les donnees du content script et les envoie au bridge.
   Le service worker n'est pas soumis aux restrictions Private Network Access. */

var BRIDGE = 'http://127.0.0.1:5001/v1/media';

chrome.runtime.onMessage.addListener(function(msg, sender, sendResponse) {
  if (msg.type !== 'MEDIA_UPDATE') return;
  var payload = {
    title:     msg.title    || '',
    artist:    msg.artist   || '',
    position:  msg.position || 0,
    duration:  msg.duration || 0,
    playing:   !!msg.playing,
    thumb_url: msg.thumb_url || '',
    source:    msg.source   || ''
  };
  fetch(BRIDGE, {
    method:  'POST',
    headers: { 'Content-Type': 'application/json' },
    body:    JSON.stringify(payload)
  })
  .then(function(){ sendResponse({ok: true}); })
  .catch(function(e){
    console.warn('[SysView] Bridge inaccessible:', e.message);
    sendResponse({ok: false});
  });
  return true; /* maintient le SW en vie jusqu'à ce que le fetch aboutisse (MV3) */
});

chrome.runtime.onInstalled.addListener(function() {
  console.log('[SysView] Extension installee v5');
});
