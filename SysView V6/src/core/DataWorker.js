// =============================================================
// DataWorker.js — SysView V6 · Web Worker
// Thread séparé : fetch API + lissage LERP.
//
// Messages entrants :
//   { type:'init',             perfMs, weatherMs }
//   { type:'weather_interval', ms }
//   { type:'config_post',      payload }
//
// Messages sortants :
//   { type:'bridge',  ok:bool }
//   { type:'lerp',    data }     ← valeurs lissées ~30 fps
//   { type:'weather', data }
//   { type:'media',   data }
// =============================================================
'use strict';

var API        = 'http://127.0.0.1:5001/v1';
var PERF_MS    = 500;
var MEDIA_MS   = 500;
var WEATHER_MS = 180000;
var LERP_MS    = 33;   // ~30 fps

// ─── État brut (mis à jour par fetchPerf) ────────────────────
var _raw = {
  cpu: 0, gpu: 0, ct: null, gt: null,
  ram: 0, ramTotal: 0, vram: 0, vramTotal: 0,
  dl: 0, ul: 0,
  cpuName: '', gpuName: '',
  disks: {}
};

// ─── État lissé LERP (envoyé au thread principal) ────────────
var _lerp = {
  cpu: 0, gpu: 0, ct: null, gt: null,
  ram: 0, ramTotal: 0, vram: 0, vramTotal: 0,
  dl: 0, ul: 0
};

var _bridgeOk       = null;
var _weatherRetries = 0;
var _lastLerpT      = 0;
var _perfTid, _mediaTid, _weatherTid, _lerpTid;

// ─── Interface messages ───────────────────────────────────────
self.onmessage = function(e) {
  var m = e.data;
  switch (m.type) {
    case 'init':
      if (m.perfMs)    PERF_MS    = m.perfMs;
      if (m.weatherMs) WEATHER_MS = m.weatherMs;
      _lastLerpT = Date.now();
      fetchPerf();
      fetchMedia();
      fetchWeather();
      startLerpLoop();
      break;
    case 'weather_interval':
      WEATHER_MS = m.ms;
      clearTimeout(_weatherTid);
      _weatherRetries = 0;
      fetchWeather();
      break;
    case 'config_post':
      fetchWithTimeout(API + '/config', 5000, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(m.payload)
      }).catch(function() {});
      break;
  }
};

// ─── Helpers ─────────────────────────────────────────────────
// Annule le timer si la requête se termine avant le délai (évite jusqu'à 6 timers simultanés)
// opts optionnel : fusionné avec { signal } pour les requêtes POST (config_post, etc.)
function fetchWithTimeout(url, ms, opts) {
  var ac = new AbortController();
  var tid = setTimeout(function() { ac.abort(); }, ms);
  var fetchOpts = Object.assign({}, opts || {}, { signal: ac.signal });
  return fetch(url, fetchOpts).finally(function() { clearTimeout(tid); });
}

function notifyBridge(ok) {
  if (_bridgeOk !== ok) {
    _bridgeOk = ok;
    self.postMessage({ type: 'bridge', ok: ok });
  }
}

// ─── Boucle LERP (~30 fps via setInterval) ───────────────────
// k=5 → τ≈0.2s, atteint 95% en ~0.6s, indépendant du framerate
function startLerpLoop() {
  _lerpTid = setInterval(function() {
    var now = Date.now();
    var dt  = Math.min((now - _lastLerpT) / 1000, 0.1);
    _lastLerpT = now;
    var k = 5, f = 1 - Math.exp(-k * dt);

    _lerp.cpu  += (_raw.cpu  - _lerp.cpu)  * f;
    _lerp.gpu  += (_raw.gpu  - _lerp.gpu)  * f;
    _lerp.ram  += (_raw.ram  - _lerp.ram)  * f;
    _lerp.vram += (_raw.vram - _lerp.vram) * f;
    _lerp.dl   += (_raw.dl   - _lerp.dl)   * f;
    _lerp.ul   += (_raw.ul   - _lerp.ul)   * f;
    _lerp.ramTotal  = _raw.ramTotal;
    _lerp.vramTotal = _raw.vramTotal;

    if (_raw.ct !== null)
      _lerp.ct = (_lerp.ct !== null) ? _lerp.ct + (_raw.ct - _lerp.ct) * f : _raw.ct;
    else _lerp.ct = null;

    if (_raw.gt !== null)
      _lerp.gt = (_lerp.gt !== null) ? _lerp.gt + (_raw.gt - _lerp.gt) * f : _raw.gt;
    else _lerp.gt = null;

    self.postMessage({ type: 'lerp', data: {
      cpu: _lerp.cpu, gpu: _lerp.gpu, ct: _lerp.ct, gt: _lerp.gt,
      ram: _lerp.ram, ramTotal: _lerp.ramTotal,
      vram: _lerp.vram, vramTotal: _lerp.vramTotal,
      dl: _lerp.dl, ul: _lerp.ul,
      cpuName: _raw.cpuName, gpuName: _raw.gpuName,
      disks: _raw.disks
    }});
  }, LERP_MS);
}

// ─── Fetch loops ─────────────────────────────────────────────
async function fetchPerf() {
  try {
    var r = await fetchWithTimeout(API + '/perf', 3000);
    if (r.ok) {
      notifyBridge(true);
      var d = await r.json();
      if (d.cpu) {
        _raw.cpu = d.cpu.usage || 0;
        _raw.ct  = d.cpu.temp != null ? d.cpu.temp : null;
        if (d.cpu.name) _raw.cpuName = d.cpu.name;
      }
      if (d.gpu) {
        _raw.gpu = d.gpu.usage || 0;
        _raw.gt  = d.gpu.temp != null ? d.gpu.temp : null;
        if (d.gpu.name) _raw.gpuName = d.gpu.name;
      }
      if (d.ram)  { _raw.ram  = d.ram.used_mb   || 0; _raw.ramTotal  = d.ram.total_mb   || 0; }
      if (d.vram) { _raw.vram = d.vram.used_mb  || 0; _raw.vramTotal = d.vram.total_mb  || 0; }
      if (d.network) { _raw.dl = d.network.download_kb || 0; _raw.ul = d.network.upload_kb || 0; }
      if (d.disks) {
        // Reconstruire depuis zéro pour retirer les disques éjectés
        var nd = {};
        ['c','d','e','f','g','h'].forEach(function(l) {
          if (d.disks[l]) nd[l] = d.disks[l];
        });
        _raw.disks = nd;
      }
    } else { notifyBridge(false); }
  } catch (_) { notifyBridge(false); }
  _perfTid = setTimeout(fetchPerf, PERF_MS);
}

async function fetchMedia() {
  try {
    var r = await fetchWithTimeout(API + '/media', 3000);
    if (r.ok) self.postMessage({ type: 'media', data: await r.json() });
    else notifyBridge(false);
  } catch (_) { notifyBridge(false); }
  _mediaTid = setTimeout(fetchMedia, MEDIA_MS);
}

async function fetchWeather() {
  try {
    var r = await fetchWithTimeout(API + '/weather', 5000);
    if (!r.ok) throw new Error('HTTP ' + r.status);
    self.postMessage({ type: 'weather', data: await r.json() });
    _weatherRetries = 0;
    _weatherTid = setTimeout(fetchWeather, WEATHER_MS);
  } catch (_) {
    _weatherRetries++;
    _weatherTid = setTimeout(fetchWeather, _weatherRetries <= 5 ? 3000 : WEATHER_MS);
  }
}
