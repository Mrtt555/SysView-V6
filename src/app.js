// =============================================================
// app.js — SysView V6 · Orchestrateur ES module
//
// Imports :
//   ThemeManager  → variables CSS + WE property listener
//   WallpaperAPI  → wallpaperRegisterAudioListener
//   CPUWidget     → rendu DOM haute fréquence (hardware)
//   WeatherWidget → construction HTML météo
//
// Architecture :
//   DataWorker    → fetch + LERP dans thread séparé
//   Alpine.data   → état réactif (météo, média, labels, show/hide)
//   rAF loop      → rendu direct DOM (valeurs lissées du worker)
// =============================================================

import { ThemeManager }                                 from './core/ThemeManager.js';
import { WallpaperAPI }                                 from './core/WallpaperAPI.js';
import { buildWeatherHtml }                from './components/WeatherWidget.js';
import { renderHW, renderDisks, setW }    from './components/MonitoringWidget.js';
import { fmtArtist, fmtTime,
         renderProgress, setAlbumArt,
         showIdleAnim }                   from './components/MediaWidget.js';
import { startClock }                     from './components/ClockWidget.js';

// ThemeManager initialisé immédiatement (flush de la queue WE)
const theme = new ThemeManager();

// ─── Traductions ─────────────────────────────────────────────
var LANG = {
  fr: {
    monitoring:'Système · Monitoring', storage:'Stockage',
    weather:'Météo · Open-Meteo', media_label:'◈ LECTURE EN COURS',
    no_media:'NO MEDIA',
    download:'▼ DOWNLOAD', upload:'▲ UPLOAD',
    cpu:'CPU', gpu:'GPU', ram:'RAM', vram:'VRAM', network:'Réseau',
    connecting:'Connexion API…',
    gb:'Go', tb:'To', disk_free:'libre',
    kbs:'ko/s', mbs:'Mo/s', gbs:'Go/s',
    days:   ['DIMANCHE','LUNDI','MARDI','MERCREDI','JEUDI','VENDREDI','SAMEDI'],
    months: ['JANVIER','FÉVRIER','MARS','AVRIL','MAI','JUIN','JUILLET','AOÛT',
             'SEPTEMBRE','OCTOBRE','NOVEMBRE','DÉCEMBRE'],
  },
  en: {
    monitoring:'System · Monitoring', storage:'Storage',
    weather:'Weather · Open-Meteo', media_label:'◈ NOW PLAYING',
    no_media:'NO MEDIA',
    download:'▼ DOWNLOAD', upload:'▲ UPLOAD',
    cpu:'CPU', gpu:'GPU', ram:'RAM', vram:'VRAM', network:'Network',
    connecting:'Connecting…',
    gb:'GB', tb:'TB', disk_free:'free',
    kbs:'KB/s', mbs:'MB/s', gbs:'GB/s',
    days:   ['SUNDAY','MONDAY','TUESDAY','WEDNESDAY','THURSDAY','FRIDAY','SATURDAY'],
    months: ['JANUARY','FEBRUARY','MARCH','APRIL','MAY','JUNE','JULY','AUGUST',
             'SEPTEMBER','OCTOBER','NOVEMBER','DECEMBER'],
  }
};
function T(lang, key) { return (LANG[lang] || LANG.fr)[key] || key; }

// ─── Alpine component ─────────────────────────────────────────
document.addEventListener('alpine:init', function() {
  Alpine.data('sysview', function() {
    return {

      cfg: {
        lang:'fr', tempUnit:'c', timeFormat:'24h',
        cpuHot:80, cpuCrit:91, gpuHot:80, gpuCrit:95,
        showCity:true, showWeatherSource:true, showDiskFree:true,
        weatherIntervalMs:600000,
        netIface:'auto', city:'HALLUIN', taskbarOffset:48,
      },

      show: {
        monitoring:true, cpu:true, gpu:true, ram:true, vram:true, net:true,
        disks:true, diskC:true, diskD:false, diskE:false,
        diskF:false, diskG:false, diskH:false,
        media:true, meteo:true,
      },

      // Valeurs lissées reçues du Worker (LERP fait dans DataWorker)
      lerp: {
        cpu:0, gpu:0, ct:null, gt:null,
        ram:0, ramTotal:0, vram:0, vramTotal:0,
        dl:0, ul:0,
      },

      hw:          { cpuName:'Connexion API…', gpuName:'Connexion API…' },
      disks:       {},
      _diskCache:  {},
      _netCache:   { dl:null, ul:null },

      clock:'00:00:00', dateStr:'—',

      weatherHtml: '<div style="color:rgba(200,170,255,.22);font-size:13px;letter-spacing:2px;">Connexion…</div>',

      mediaTitle:'', mediaArtist:'', mediaPlatform:'', mediaType:'', mediaPlaying:false,
      mediaPos:0,    mediaDur:0,
      _lastTitle:'', _lastThumb:'', _mediaGoneAt:0, _pausedSince:0,
      _vizBars: null,   _audioSilent: 0,

      bridgeOk:    false,
      _worker:     null,
      _audioEma:   new Array(24).fill(0),
      _cfgDebounce:null, _cityDebounce:null,

      // ── Init ────────────────────────────────────────────────
      init() {
        window._sysview = this;
        theme.setInstance(this);
        document.body.classList.add('no-api');
        this.$watch('bridgeOk', function(v) {
          document.body.classList.toggle('no-api', !v);
        });
        this._startWorker();
        this._startClock();
        this._startRenderLoop();
        this._startMediaLoop();
        this._initViz();
        var self = this;
        WallpaperAPI.registerAudio(function(arr) { self._onAudio(arr); });
        this._initDemo();
      },

      // ── Web Worker ──────────────────────────────────────────
      _startWorker() {
        var self = this;
        try {
          this._worker = new Worker('src/core/DataWorker.js');
          this._worker.onmessage = function(e) { self._onMsg(e.data); };
          this._worker.postMessage({
            type:'init', perfMs:500, weatherMs:this.cfg.weatherIntervalMs
          });
        } catch(e) {
          console.warn('[SysView] Worker:', e.message);
        }
      },

      _onMsg(msg) {
        switch (msg.type) {
          case 'bridge':  this.bridgeOk = msg.ok;                              break;
          case 'lerp':    this._onLerp(msg.data);                              break;
          case 'weather': this.weatherHtml = buildWeatherHtml(msg.data, this.cfg); break;
          case 'media':   this._onMedia(msg.data);                             break;
        }
      },

      // Worker envoie des valeurs déjà lissées — on stocke et on rend
      _onLerp(d) {
        this.lerp = d;
        if (d.cpuName && d.cpuName !== this.hw.cpuName) this.hw.cpuName = d.cpuName;
        if (d.gpuName && d.gpuName !== this.hw.gpuName) this.hw.gpuName = d.gpuName;
        if (d.disks) {
          var self = this;
          ['c','d','e','f','g','h'].forEach(function(l) {
            if (d.disks[l]) self.disks[l] = d.disks[l];
          });
        }
      },

      // ── rAF — rendu DOM (les valeurs lissées viennent du Worker) ──
      _startRenderLoop() {
        var self = this;
        function loop() {
          self._renderHW();
          requestAnimationFrame(loop);
        }
        requestAnimationFrame(loop);
      },

      _renderHW() {
        var lang = this.cfg.lang;
        var net  = renderHW(this.lerp, this.cfg, function(k) { return T(lang, k); });

        if (this._netCache.dl !== net.dlStr) {
          this._netCache.dl = net.dlStr;
          var eD = document.getElementById('net-dl');
          if (eD) eD.innerHTML = net.dlStr;
        }
        if (this._netCache.ul !== net.ulStr) {
          this._netCache.ul = net.ulStr;
          var eU = document.getElementById('net-ul');
          if (eU) eU.innerHTML = net.ulStr;
        }
        setW('net-dl-bar', net.dlPct);
        setW('net-ul-bar', net.ulPct);

        renderDisks(this.disks, this.cfg, function(k) { return T(lang, k); }, this._diskCache);
      },

      // ── Horloge ─────────────────────────────────────────────
      _startClock() {
        var self = this;
        startClock(
          function()  { return self.cfg.timeFormat; },
          function()  { return T(self.cfg.lang, 'days'); },
          function()  { return T(self.cfg.lang, 'months'); },
          function(v) { self.clock = v.clock; self.dateStr = v.dateStr; }
        );
      },

      // ── Média ────────────────────────────────────────────────
      _onMedia(d) {
        if (!d.title) {
          if (this._mediaGoneAt === 0) this._mediaGoneAt = Date.now();
          if (Date.now() - this._mediaGoneAt < 30000) return;
          if (this._lastTitle !== '') this._clearMedia();
          return;
        }
        this._mediaGoneAt = 0;
        var titleChanged  = d.title !== this._lastTitle;
        this._lastTitle   = d.title;
        if (titleChanged) this._lastThumb = '';

        this.mediaTitle    = d.title;
        this.mediaPlatform = d.platform || '';
        this.mediaType     = d.media_type || '';
        // Artiste : canal YouTube / artiste Spotify / nom service → fallback plateforme
        this.mediaArtist   = fmtArtist(d.artist || '', d.title || '') || d.platform || '';
        // Ratio du cadre : carré pour musique, portrait 2:3 pour streaming vidéo
        var mart = document.querySelector('.mart');
        if (mart) mart.classList.toggle('mart--video', this.mediaType === 'video');
        this.mediaPlaying  = !!d.playing;

        if (d.thumb_url && d.thumb_url !== this._lastThumb) {
          // Nouvelle miniature valide (déjà filtrée côté C# — jamais un favicon)
          this._lastThumb = d.thumb_url;
          this._setAlbumArt(d.thumb_url);
        } else if (titleChanged && !d.thumb_url) {
          // Nouveau titre sans miniature → masquer l'ancienne image
          var img = document.getElementById('media-art-img');
          if (img) img.style.opacity = 0;
          showIdleAnim();
        }

        if (d.duration > 0) {
          this.mediaDur = d.duration; this.mediaPos = d.position;
          this._renderProgress(titleChanged);
        } else if (titleChanged) {
          this.mediaDur = 0; this.mediaPos = 0;
          this._renderProgress(true);
        }
      },

      _startMediaLoop() {
        var self = this;
        setInterval(function() {
          if (!self.bridgeOk || !self._lastTitle) return;
          if (self.mediaPlaying) { self._pausedSince = 0; return; }
          if (self._pausedSince === 0) self._pausedSince = Date.now();
          if (Date.now() - self._pausedSince > 5 * 60 * 1000) {
            self._pausedSince = 0; self._clearMedia();
          }
        }, 1000);
      },

      _clearMedia() {
        this._lastTitle = ''; this._lastThumb = '';
        this._pausedSince = 0; this._mediaGoneAt = 0;
        this.mediaTitle = ''; this.mediaArtist = ''; this.mediaType = '';
        this.mediaPlaying = false; this.mediaDur = 0; this.mediaPos = 0;
        var mart = document.querySelector('.mart');
        if (mart) mart.classList.remove('mart--video');
        var img = document.getElementById('media-art-img');
        if (img) img.style.opacity = 0;
        showIdleAnim();
        renderProgress(true, 0, 0, this.bridgeOk);
      },

      _setAlbumArt(src) { setAlbumArt(src); },

      _renderProgress(snap) {
        renderProgress(snap, this.mediaDur, this.mediaPos, this.bridgeOk);
      },

      // ── Visualiseur audio (WE) ───────────────────────────────
      _initViz() {
        var viz = document.getElementById('media-viz');
        if (!viz) return;
        for (var i = 0; i < 24; i++) {
          var b = document.createElement('div');
          b.className = 'viz-bar';
          // Délai d'animation idle échelonné (ms) pour un effet de vague
          b.style.setProperty('--vd', (i * 85) + 'ms');
          viz.appendChild(b);
        }
        this._vizBars = viz.querySelectorAll('.viz-bar');
      },

      _onAudio(audioArray) {
        var bars = this._vizBars;
        if (!bars) return;
        var anySound = false;
        for (var i = 0; i < 24; i++) {
          var s = Math.floor(i * 64 / 24), e = Math.floor((i + 1) * 64 / 24);
          if (e <= s) e = s + 1;
          var peak = 0;
          for (var j = s; j < e; j++) { if ((audioArray[j] || 0) > peak) peak = audioArray[j]; }
          var a = peak > this._audioEma[i] ? 0.80 : 0.18;
          this._audioEma[i] += a * (peak - this._audioEma[i]);
          if (this._audioEma[i] > 0.008) anySound = true;
          // Minimum 0.03 → barres toujours légèrement visibles même en silence WE
          var scale = Math.max(0.03, Math.min(0.90, this._audioEma[i] * 0.9));
          if (bars[i]) bars[i].style.transform = 'scaleY(' + scale.toFixed(3) + ')';
        }
        // Silence prolongé → laisser l'animation CSS idle reprendre
        if (anySound) {
          this._audioSilent = 0;
        } else {
          this._audioSilent++;
          if (this._audioSilent > 45) {     // ~1.5 s à 30 fps
            for (var i = 0; i < 24; i++) {
              if (bars[i]) bars[i].style.transform = '';
            }
          }
        }
      },

      // ── Mode démo (sans bridge) ──────────────────────────────
      _initDemo() {
        var self = this;
        var demo = [
          { title:'Midnight City', artist:'M83'      },
          { title:'Neon Lights',   artist:'Kraftwerk' },
          { title:'Digital Love',  artist:'Daft Punk' },
        ];
        var idx = 0;
        this.mediaTitle  = demo[0].title;
        this.mediaArtist = demo[0].artist;
        this.mediaDur    = 0;

        setInterval(function() {
          if (self.bridgeOk && self._lastTitle)  return;
          if (self.bridgeOk && !self._lastTitle) return;
          idx = (idx + 1) % demo.length;
          self.mediaTitle  = demo[idx].title;
          self.mediaArtist = demo[idx].artist;
        }, 8000);
      },

      // ── Helpers Alpine ───────────────────────────────────────
      T(key) { return T(this.cfg.lang, key); },

      weatherTitle() {
        var base = T(this.cfg.lang, 'weather');
        return this.cfg.showCity ? base + ' · ' + this.cfg.city : base;
      },

      // ── Méthodes appelées par ThemeManager ───────────────────
      setOpacity(op) { theme.applyOpacity(op); },

      setLang(val) {
        this.cfg.lang = (val === 'en' || val === 1 || val === '1') ? 'en' : 'fr';
        this._diskCache = {}; this._netCache = { dl:null, ul:null };
      },

      setWeatherInterval(min) {
        this.cfg.weatherIntervalMs = Math.max(1, Math.min(60, min)) * 60 * 1000;
        if (this._worker)
          this._worker.postMessage({ type:'weather_interval', ms:this.cfg.weatherIntervalMs });
      },

      sendWeatherConfig() {
        var self = this;
        clearTimeout(this._cfgDebounce);
        this._cfgDebounce = setTimeout(function() {
          if (self._worker) self._worker.postMessage({
            type:'config_post',
            payload:{ weather_interval_min: Math.round(self.cfg.weatherIntervalMs / 60000),
                      network_iface: self.cfg.netIface }
          });
        }, 300);
      },

      sendCityConfig() {
        var self = this;
        clearTimeout(this._cityDebounce);
        this._cityDebounce = setTimeout(function() {
          var q = (self.cfg.city || '').trim();
          if (q && self._worker)
            self._worker.postMessage({ type:'config_post', payload:{ city:q } });
        }, 500);
      },

    }; // fin return
  }); // fin Alpine.data
}); // fin alpine:init
