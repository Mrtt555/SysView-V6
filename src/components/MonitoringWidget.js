// =============================================================
// CPUWidget.js — SysView V6
// Helpers de rendu pour CPU, GPU, RAM, VRAM, réseau, disques.
// Toutes les écritures DOM passent par setTxt / setW pour éviter
// les reflows inutiles (comparaison avant écriture).
// =============================================================

// ─── DOM helpers ─────────────────────────────────────────────
function $(id) { return document.getElementById(id); }

export function setTxt(id, txt, cls) {
  var e = $(id); if (!e) return;
  if (e.textContent !== txt) e.textContent = txt;
  if (cls !== undefined && e.className !== cls) e.className = cls;
}

export function setW(id, pct) {
  var e = $(id); if (!e) return;
  var w = clamp(pct) + '%';
  if (e.style.width !== w) e.style.width = w;
}

// ─── Classificateurs couleur ─────────────────────────────────
export function clamp(v) { return Math.min(100, Math.max(0, Math.round(v))); }

export function loadCls(p) {
  if (p >= 95) return 'c-crit';
  if (p >= 85) return 'c-hot';
  if (p >= 70) return 'c-warm';
  return 'c-ok';
}

export function tempCls(v, p, hot, crit) {
  if (v === null) return loadCls(p);
  if (v >= crit)     return 'c-crit';
  if (v >= hot)      return 'c-hot';
  if (v >= hot - 15) return 'c-warm';
  return 'c-ok';
}

// ─── Format helpers ───────────────────────────────────────────
function pad(n) { return String(n).padStart(2, '0'); }

export function fmtTemp(c, unit) {
  if (unit === 'f') return '' + Math.round(c * 9 / 5 + 32);
  return '' + Math.round(c);
}
export function tempUnit(unit) { return unit === 'f' ? '°F' : '°C'; }

export function fmtNet(kb, lang, T) {
  if (kb >= 1000000) return (kb/1000000).toFixed(1) + ' <span class="netunit">' + T('gbs') + '</span>';
  if (kb >= 1000)    return (kb/1000).toFixed(1)    + ' <span class="netunit">' + T('mbs') + '</span>';
  return Math.round(kb) + ' <span class="netunit">' + T('kbs') + '</span>';
}

export function netPct(kb) {
  return kb < 0.5 ? 0 : Math.max(0, Math.min(100, Math.log10(Math.max(1, kb)) / 6 * 100));
}

export function fmtTime(sec) {
  sec = Math.floor(Math.max(0, sec) || 0);
  var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
  if (h > 0) return h + ':' + pad(m) + ':' + pad(s);
  return m + ':' + pad(s);
}

// ─── Rendu hardware (appelé depuis rAF main thread) ──────────
export function renderHW(lerp, cfg, T) {
  var rt  = lerp.ramTotal  || 0;
  var vt  = lerp.vramTotal || 0;
  var cp  = clamp(lerp.cpu);
  var gp  = clamp(lerp.gpu);
  var rp  = rt > 0 ? clamp(lerp.ram  / rt * 100) : 0;
  var vp  = vt > 0 ? clamp(lerp.vram / vt * 100) : 0;
  var tu  = tempUnit(cfg.tempUnit);
  var ct  = lerp.ct, gt = lerp.gt;
  var lang = cfg.lang;

  setW('cpu-bar', cp); setW('gpu-bar', gp);
  setW('ram-bar', rp); setW('vram-bar', vp);

  setTxt('cpu-pct',  cp + '%', 'stat-pct '  + loadCls(cp));
  setTxt('gpu-pct',  gp + '%', 'stat-pct '  + loadCls(gp));
  setTxt('cpu-temp',
    ct !== null ? fmtTemp(ct, cfg.tempUnit) + tu : '—' + tu,
    'stat-temp ' + tempCls(ct, cp, cfg.cpuHot, cfg.cpuCrit));
  setTxt('gpu-temp',
    gt !== null ? fmtTemp(gt, cfg.tempUnit) + tu : '—' + tu,
    'stat-temp ' + tempCls(gt, gp, cfg.gpuHot, cfg.gpuCrit));

  setTxt('ram-pct',   rt > 0 ? rp + '%' : '—');
  setTxt('ram-used',  (Math.max(0, lerp.ram) / 1024).toFixed(1));
  setTxt('ram-total', rt > 0
    ? '/ ' + (rt / 1024).toFixed(0) + ' ' + T('gb')
    : '/ — ' + T('gb'));

  setTxt('vram-pct',   vt > 0 ? vp + '%' : '—');
  setTxt('vram-used',  (Math.max(0, lerp.vram) / 1024).toFixed(1));
  setTxt('vram-total', vt > 0
    ? '/ ' + (vt / 1024).toFixed(0) + ' ' + T('gb')
    : '/ — ' + T('gb'));

  return { dlStr: fmtNet(Math.max(0, lerp.dl), lang, T),
           ulStr: fmtNet(Math.max(0, lerp.ul), lang, T),
           dlPct: netPct(lerp.dl),
           ulPct: netPct(lerp.ul) };
}

export function renderDisks(disks, cfg, T, diskCache) {
  var lang = cfg.lang;
  ['c','d','e','f','g','h'].forEach(function(letter) {
    var info = disks[letter];
    if (!info || !info.total_gb) return;
    var pct      = (info.percent || 0).toFixed(1);
    var usedTiB  = info.used_unit  === 'To';
    var totalTiB = info.total_unit === 'To';
    var freeTiB  = info.free_unit  === 'To';
    var used     = usedTiB ? info.used_gb.toFixed(2) : info.used_gb.toFixed(1);
    var unit     = usedTiB ? ' ' + T('tb') : ' ' + T('gb');
    var totalStr = totalTiB
      ? '/ ' + info.total_gb.toFixed(2) + ' ' + T('tb')
      : '/ ' + Math.round(info.total_gb) + ' ' + T('gb');
    var key = pct + '|' + used + unit + '|' + totalStr + '|' + (info.free_gb || 0);
    if (diskCache[letter] === key) return;
    diskCache[letter] = key;

    setW('disk-' + letter + '-bar', parseFloat(pct));
    var uEl = $('disk-' + letter + '-used');
    if (uEl) {
      uEl.textContent = used;
      var mEl = uEl.parentElement && uEl.parentElement.querySelector('.memunit');
      if (mEl) mEl.textContent = unit;
    }
    var tEl = $('disk-' + letter + '-total');
    if (tEl) {
      if (cfg.showDiskFree && info.free_gb != null) {
        var freeStr = freeTiB
          ? info.free_gb.toFixed(2) + ' ' + T('tb')
          : info.free_gb.toFixed(1) + ' ' + T('gb');
        tEl.innerHTML = totalStr + ' <span class="disk-free">· ' + freeStr + ' ' + T('disk_free') + '</span>';
      } else {
        tEl.textContent = totalStr;
      }
    }
    var blk = $('disk-' + letter + '-blk');
    if (blk) blk.style.display = '';
  });
}
