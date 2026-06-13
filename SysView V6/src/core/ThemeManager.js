// =============================================================
// ThemeManager.js — SysView V6
// Intercepte wallpaperPropertyListener, applique les variables
// CSS root et délègue à l'instance Alpine (_sv) si disponible.
// Supporte anciens noms WE (accent_color…) et noms CdC
// (Primary_Color, Global_Opacity…) pour rétro-compatibilité.
// =============================================================

export class ThemeManager {
  constructor() {
    this._sv = null;
    this._install();
    this._flushQueue();
    this.applyOpacity(0.55); // valeur par défaut
  }

  // Prend le relais sur le listener stub posé dans index.html
  _install() {
    var self = this;
    window.wallpaperPropertyListener = {
      applyUserProperties: function(p) { self.apply(p); }
    };
  }

  // Applique les propriétés mises en queue avant le chargement du module
  _flushQueue() {
    var queue = window._wePropsQueue || [];
    var self = this;
    queue.forEach(function(p) { self.apply(p); });
    window._wePropsQueue = [];
  }

  // Référence vers l'instance Alpine (définie dans app.js init())
  setInstance(sv) { this._sv = sv; }

  // ── Point d'entrée principal ─────────────────────────────────
  apply(p) {
    try { this._applyInner(p); } catch(e) { console.warn('[ThemeManager] apply error', e); }
  }

  _applyInner(p) {
    var r  = document.documentElement;
    var sv = this._sv;

    function rgb(prop) {
      var v = prop.value.trim().split(/\s+/);
      // Composants manquants → 0 pour éviter NaN dans les variables CSS
      var r0 = Math.round((parseFloat(v[0]) || 0) * 255);
      var g0 = Math.round((parseFloat(v[1]) || 0) * 255);
      var b0 = Math.round((parseFloat(v[2]) || 0) * 255);
      return r0 + ',' + g0 + ',' + b0;
    }

    // ── Couleurs ──────────────────────────────────────────────
    if (p.Primary_Color   !== undefined) r.style.setProperty('--p',  rgb(p.Primary_Color));
    if (p.accent_color    !== undefined) r.style.setProperty('--p',  rgb(p.accent_color));
    if (p.Secondary_Color !== undefined) r.style.setProperty('--s',  rgb(p.Secondary_Color));
    if (p.accent2_color   !== undefined) r.style.setProperty('--s',  rgb(p.accent2_color));
    if (p.bg_color        !== undefined) r.style.setProperty('--bg', rgb(p.bg_color));
    if (p.text_color      !== undefined) r.style.setProperty('--tx', rgb(p.text_color));
    if (p.bar_cpu_gpu !== undefined) r.style.setProperty('--bar-cpugpu', rgb(p.bar_cpu_gpu));
    if (p.bar_ram     !== undefined) r.style.setProperty('--bar-ram',    rgb(p.bar_ram));
    if (p.bar_vram    !== undefined) r.style.setProperty('--bar-vram',   rgb(p.bar_vram));
    if (p.bar_dl      !== undefined) r.style.setProperty('--bar-dl',     rgb(p.bar_dl));
    if (p.bar_ul      !== undefined) r.style.setProperty('--bar-ul',     rgb(p.bar_ul));
    if (p.bar_disk    !== undefined) r.style.setProperty('--bar-disk',   rgb(p.bar_disk));

    if (p.font_family !== undefined) {
      var googleFonts = {
        roboto:        'Roboto:wght@400;500;700',
        dmsans:        'DM+Sans:wght@400;500;700',
        barlow:        'Barlow:wght@400;500;600',
        nunito:        'Nunito:wght@400;600;700',
        rajdhani:      'Rajdhani:wght@400;600;700',
        exo2:          'Exo+2:wght@400;600;700',
        oxanium:       'Oxanium:wght@400;600;700',
        orbitron:      'Orbitron:wght@400;600;700',
        titillium:     'Titillium+Web:wght@400;600;700',
        spacegrotesk:  'Space+Grotesk:wght@400;500;700',
        quantico:      'Quantico:wght@400;700',
        chakra:        'Chakra+Petch:wght@400;600;700',
        jetbrains:     'JetBrains+Mono:wght@400;500;700',
        robotomono:    'Roboto+Mono:wght@400;500;700',
        sourcecodepro: 'Source+Code+Pro:wght@400;500;700',
        spacemono:     'Space+Mono:wght@400;700',
        sharetech:     'Share+Tech+Mono',
      };
      var fontStack = {
        inter:         "'Inter', 'Segoe UI', system-ui, sans-serif",
        segoe:         "'Segoe UI', system-ui, sans-serif",
        roboto:        "'Roboto', 'Segoe UI', sans-serif",
        dmsans:        "'DM Sans', 'Segoe UI', sans-serif",
        barlow:        "'Barlow', 'Segoe UI', sans-serif",
        nunito:        "'Nunito', 'Segoe UI', sans-serif",
        rajdhani:      "'Rajdhani', 'Segoe UI', sans-serif",
        exo2:          "'Exo 2', 'Segoe UI', sans-serif",
        oxanium:       "'Oxanium', 'Segoe UI', sans-serif",
        orbitron:      "'Orbitron', 'Segoe UI', sans-serif",
        titillium:     "'Titillium Web', 'Segoe UI', sans-serif",
        spacegrotesk:  "'Space Grotesk', 'Segoe UI', sans-serif",
        quantico:      "'Quantico', 'Rajdhani', sans-serif",
        chakra:        "'Chakra Petch', 'Rajdhani', sans-serif",
        jetbrains:     "'JetBrains Mono', 'Consolas', monospace",
        robotomono:    "'Roboto Mono', 'Consolas', monospace",
        sourcecodepro: "'Source Code Pro', 'Consolas', monospace",
        spacemono:     "'Space Mono', 'Consolas', monospace",
        sharetech:     "'Share Tech Mono', 'Consolas', monospace",
        consolas:      "'Consolas', 'Courier New', monospace",
      };
      var fk = p.font_family.value;
      if (googleFonts[fk]) {
        var linkId = 'gfont-' + fk;
        if (!document.getElementById(linkId)) {
          var lk = document.createElement('link');
          lk.id = linkId; lk.rel = 'stylesheet';
          lk.href = 'https://fonts.googleapis.com/css2?family=' + googleFonts[fk] + '&display=swap';
          document.head.appendChild(lk);
        }
      }
      var ff = fontStack[fk] || fontStack.inter;
      r.style.setProperty('--ff', ff);
      document.documentElement.style.fontFamily = ff;
    }

    // ── Opacité globale ───────────────────────────────────────
    var opProp = p.Global_Opacity !== undefined ? p.Global_Opacity : p.panel_opacity;
    if (opProp !== undefined) {
      var op = parseFloat(opProp.value) / 100;
      if (isNaN(op)) op = 0.55;
      r.style.setProperty('--op', op.toFixed(2));
      if (sv) sv.setOpacity(op);
      else    this.applyOpacity(op);
    }

    // ── Échelle UI ────────────────────────────────────────────
    if (p.ui_scale !== undefined)
      r.style.setProperty('--sz', (p.ui_scale.value / 100).toFixed(3));

    // ── Offset barre des tâches ───────────────────────────────
    if (p.taskbar_offset !== undefined) {
      var mp = document.getElementById('media-panel');
      if (mp) mp.style.bottom = p.taskbar_offset.value + 'px';
      if (sv) sv.cfg.taskbarOffset = p.taskbar_offset.value;
    }

    // ── Image / vidéo de fond ─────────────────────────────────
    if (p.bg_image !== undefined) this._applyBgImage(p.bg_image.value || '');

    if (!sv) return;

    // ── Propriétés déléguées à Alpine ────────────────────────
    if (p.city_name !== undefined)  { sv.cfg.city = p.city_name.value; sv.sendCityConfig(); }
    if (p.language  !== undefined)  sv.setLang(p.language.value);
    if (p.show_city !== undefined)  sv.cfg.showCity = !!p.show_city.value;
    if (p.show_weather_source !== undefined) {
      sv.cfg.showWeatherSource = !!p.show_weather_source.value;
      var bm = document.getElementById('blk-meteo');
      if (bm) bm.classList.toggle('hide-source', !sv.cfg.showWeatherSource);
    }
    if (p.temp_unit !== undefined) {
      sv.cfg.tempUnit = p.temp_unit.value;
      if (sv._rebuildWeather) sv._rebuildWeather();
    }
    if (p.temp_decimal  !== undefined) {
      sv.cfg.tempDecimal = !!p.temp_decimal.value;
      if (sv._rebuildWeather) sv._rebuildWeather();
    }
    if (p.time_format   !== undefined) sv.cfg.timeFormat  = p.time_format.value;
    if (p.network_iface !== undefined) { sv.cfg.netIface = p.network_iface.value; sv.sendWeatherConfig(); }

    if (p.cpu_temp_hot  !== undefined) { var v1 = parseInt(p.cpu_temp_hot.value);  if (!isNaN(v1)) sv.cfg.cpuHot  = v1; }
    if (p.cpu_temp_crit !== undefined) { var v2 = parseInt(p.cpu_temp_crit.value); if (!isNaN(v2)) sv.cfg.cpuCrit = v2; }
    if (p.gpu_temp_hot  !== undefined) { var v3 = parseInt(p.gpu_temp_hot.value);  if (!isNaN(v3)) sv.cfg.gpuHot  = v3; }
    if (p.gpu_temp_crit !== undefined) { var v4 = parseInt(p.gpu_temp_crit.value); if (!isNaN(v4)) sv.cfg.gpuCrit = v4; }

    if (p.show_disk_free !== undefined) {
      sv.cfg.showDiskFree = !!p.show_disk_free.value;
      sv._diskCache = {};
    }
    if (p.weather_manual !== undefined) sv.sendWeatherConfig();

    var wiProp = p.Weather_Interval !== undefined ? p.Weather_Interval : p.weather_interval;
    if (wiProp !== undefined) sv.setWeatherInterval(wiProp.value);

    var showMap = {
      show_monitoring:'monitoring', show_cpu:'cpu',  show_gpu:'gpu',
      show_ram:'ram',   show_vram:'vram',   show_net:'net',
      show_disks:'disks',   show_disk_c:'diskC', show_disk_d:'diskD',
      show_disk_e:'diskE',  show_disk_f:'diskF', show_disk_g:'diskG',
      show_disk_h:'diskH',  show_media:'media',  show_meteo:'meteo',
    };
    for (var key in showMap) {
      if (p[key] !== undefined) sv.show[showMap[key]] = !!p[key].value;
    }
  }

  // ── Variables CSS dérivées de l'opacité ──────────────────────
  applyOpacity(op) {
    var r = document.documentElement;
    r.style.setProperty('--op-hi',     (op * 0.40 ).toFixed(3));
    r.style.setProperty('--op-lo',     (op * 0.18 ).toFixed(3));
    r.style.setProperty('--op-media',  (op * 0.36 ).toFixed(3));
    r.style.setProperty('--op-card',   (op * 0.20 ).toFixed(3));
    r.style.setProperty('--op-glow',   (op * 0.545).toFixed(3));
    r.style.setProperty('--op-shadow', (op * 0.364).toFixed(3));
  }

  // ── Fond image / vidéo ────────────────────────────────────────
  _applyBgImage(bgVal) {
    var bgEl = document.getElementById('bg-image');
    if (!bgEl) return;
    if (bgVal) {
      var decoded;
      try { decoded = decodeURIComponent(bgVal); } catch(_) { decoded = bgVal; }
      var bgPath = decoded.replace(/\\/g, '/');
      if (bgPath.match(/^[A-Za-z]:\//)) bgPath = 'file:///' + bgPath;
      if (/\.(mp4|webm|mkv|mov)$/i.test(bgPath)) {
        bgEl.style.backgroundImage = 'none';
        var bv = bgEl.querySelector('video') || document.createElement('video');
        bv.src = bgPath; bv.autoplay = true; bv.loop = true;
        bv.muted = true; bv.playsInline = true;
        bv.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;object-fit:cover;pointer-events:none;';
        if (!bgEl.querySelector('video')) bgEl.appendChild(bv);
        bv.load(); bv.play().catch(function() {});
      } else {
        var ov = bgEl.querySelector('video');
        if (ov) bgEl.removeChild(ov);
        bgEl.style.backgroundImage = "url('" + bgPath.replace(/'/g, '%27').replace(/ /g, '%20') + "')";
      }
    } else {
      var ov2 = bgEl.querySelector('video');
      if (ov2) bgEl.removeChild(ov2);
      bgEl.style.backgroundImage = 'none';
    }
  }
}
