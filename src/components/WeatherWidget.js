// =============================================================
// WeatherWidget.js — SysView V6
// Construction du HTML météo depuis les données Open-Meteo.
// Exporte : buildWeatherHtml(data, cfg)
// =============================================================

var WMO_FR = {
  0:'Ciel dégagé', 1:'Ciel dégagé', 2:'Partiellement nuageux', 3:'Nuageux',
  45:'Brouillard', 48:'Brouillard givrant',
  51:'Bruine légère', 53:'Bruine', 55:'Bruine forte',
  56:'Bruine verglaçante légère', 57:'Bruine verglaçante forte',
  61:'Pluie légère', 63:'Pluie', 65:'Pluie forte',
  66:'Pluie verglaçante légère', 67:'Pluie verglaçante forte',
  71:'Neige légère', 73:'Neige', 75:'Neige forte', 77:'Grains de neige',
  80:'Averses légères', 81:'Averses', 82:'Averses fortes',
  85:'Averses de neige légères', 86:'Averses de neige fortes',
  95:'Orage', 96:'Orage+grêle', 99:'Orage+grêle'
};
var WMO_EN = {
  0:'Clear sky', 1:'Mainly clear', 2:'Partly cloudy', 3:'Overcast',
  45:'Fog', 48:'Icy fog',
  51:'Light drizzle', 53:'Drizzle', 55:'Heavy drizzle',
  56:'Light freezing drizzle', 57:'Heavy freezing drizzle',
  61:'Light rain', 63:'Rain', 65:'Heavy rain',
  66:'Light freezing rain', 67:'Heavy freezing rain',
  71:'Light snow', 73:'Snow', 75:'Heavy snow', 77:'Snow grains',
  80:'Light showers', 81:'Showers', 82:'Heavy showers',
  85:'Light snow showers', 86:'Heavy snow showers',
  95:'Thunderstorm', 96:'Thunderstorm+hail', 99:'Thunderstorm+hail'
};

export function wmoLabel(code, lang) {
  return (lang === 'en' ? WMO_EN : WMO_FR)[code] ||
         (lang === 'en' ? 'Unknown' : 'Inconnu');
}

export function wSVG(c) {
  c = parseInt(c);
  var s = 'style="width:100%;height:100%;display:block;"';
  if (c <= 1)
    return '<svg viewBox="0 0 60 60" fill="none" ' + s + '><circle cx="30" cy="30" r="13" fill="#FFD700" opacity=".9"/><g stroke="#FFD700" stroke-width="2.5" stroke-linecap="round" opacity=".6"><line x1="30" y1="5" x2="30" y2="11"/><line x1="30" y1="49" x2="30" y2="55"/><line x1="5" y1="30" x2="11" y2="30"/><line x1="49" y1="30" x2="55" y2="30"/><line x1="11" y1="11" x2="15" y2="15"/><line x1="45" y1="45" x2="49" y2="49"/><line x1="11" y1="49" x2="15" y2="45"/><line x1="45" y1="15" x2="49" y2="11"/></g></svg>';
  if (c === 2)
    return '<svg viewBox="0 0 60 60" fill="none" ' + s + '><circle cx="21" cy="25" r="10" fill="#FFD700" opacity=".8"/><rect x="8" y="30" width="38" height="14" rx="7" fill="#7090a8" opacity=".85"/><rect x="15" y="22" width="27" height="14" rx="7" fill="#90afc0" opacity=".9"/></svg>';
  if (c === 3)
    return '<svg viewBox="0 0 60 60" fill="none" ' + s + '><rect x="7" y="28" width="42" height="16" rx="8" fill="#607888" opacity=".85"/><rect x="14" y="20" width="30" height="16" rx="8" fill="#7898a8" opacity=".9"/><rect x="21" y="12" width="20" height="13" rx="6" fill="#90aabc" opacity=".8"/></svg>';
  if (c === 45 || c === 48)
    return '<svg viewBox="0 0 60 60" fill="none" ' + s + '><g stroke="#a0b8c8" stroke-width="2.5" stroke-linecap="round" opacity=".75"><line x1="10" y1="20" x2="50" y2="20"/><line x1="6" y1="30" x2="54" y2="30"/><line x1="10" y1="40" x2="50" y2="40"/></g></svg>';
  if (c >= 51 && c <= 67)
    return '<svg viewBox="0 0 60 60" fill="none" ' + s + '><rect x="8" y="10" width="42" height="18" rx="9" fill="#607888" opacity=".85"/><g stroke="#7070ff" stroke-width="2.5" stroke-linecap="round"><line x1="18" y1="32" x2="13" y2="48"/><line x1="30" y1="32" x2="25" y2="48"/><line x1="42" y1="32" x2="37" y2="48"/></g></svg>';
  if (c >= 71 && c <= 77)
    return '<svg viewBox="0 0 60 60" fill="none" ' + s + '><rect x="8" y="10" width="42" height="16" rx="8" fill="#607888" opacity=".8"/><g fill="#c0d8ff" opacity=".85"><circle cx="18" cy="38" r="5"/><circle cx="30" cy="42" r="5"/><circle cx="42" cy="38" r="5"/></g></svg>';
  if (c >= 80 && c <= 86)
    return '<svg viewBox="0 0 60 60" fill="none" ' + s + '><rect x="7" y="8" width="42" height="18" rx="9" fill="#485860" opacity=".9"/><g stroke="#7070ff" stroke-width="2.5" stroke-linecap="round"><line x1="16" y1="30" x2="11" y2="48"/><line x1="25" y1="30" x2="20" y2="48"/><line x1="34" y1="30" x2="29" y2="48"/><line x1="43" y1="30" x2="38" y2="48"/></g></svg>';
  if (c >= 95)
    return '<svg viewBox="0 0 60 60" fill="none" ' + s + '><rect x="7" y="7" width="44" height="18" rx="9" fill="#384050" opacity=".9"/><polyline points="23,27 16,41 27,41 18,56" stroke="#FFD700" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round" fill="none"/></svg>';
  return '<svg viewBox="0 0 60 60" ' + s + '><circle cx="30" cy="30" r="20" fill="none" stroke="#a070ff" stroke-width="2" opacity=".4"/></svg>';
}

function pollenCls(v) {
  if (v === null || v === undefined || isNaN(+v) || +v === 0) return 'pp-none';
  var i = +v;
  if (i < 20)  return 'pp-low';
  if (i < 75)  return 'pp-mod';
  if (i < 150) return 'pp-high';
  return 'pp-vhigh';
}
function pollenLbl(v, lang) {
  if (v === null || v === undefined || isNaN(+v)) return '—';
  var i = +v;
  if (lang === 'en') {
    if (i===0) return 'None'; if (i<20) return 'Low';
    if (i<75)  return 'Moderate'; if (i<150) return 'High';
    return 'Very High';
  }
  if (i===0) return 'Nul'; if (i<20) return 'Faible';
  if (i<75)  return 'Modéré'; if (i<150) return 'Élevé';
  return 'Très élevé';
}
function aqiCls(v) {
  if (v === null || v === undefined || isNaN(+v)) return 'pp-none';
  var i = +v;
  if (i <= 20) return 'pp-low'; if (i <= 40) return 'pp-mod';
  if (i <= 60) return 'pp-high'; if (i <= 80) return 'pp-vhigh';
  return 'pp-crit';
}
function aqiLbl(v, lang) {
  if (v === null || v === undefined || isNaN(+v)) return '—';
  var i = +v;
  if (lang === 'en') {
    if (i<=20) return 'Good'; if (i<=40) return 'Fair';
    if (i<=60) return 'Moderate'; if (i<=80) return 'Poor';
    return 'Very Poor';
  }
  if (i<=20) return 'Bon'; if (i<=40) return 'Correct';
  if (i<=60) return 'Modéré'; if (i<=80) return 'Mauvais';
  return 'Très mauvais';
}

function T_weather(lang, key) {
  var MAP = {
    fr: { precip:'PROB. PLUIE', wind:'VENT', pollen:'Pollen', air_quality:'QAI Europe' },
    en: { precip:'RAIN PROB.',  wind:'WIND', pollen:'Pollen', air_quality:'AQI Europe' },
  };
  return (MAP[lang] || MAP.fr)[key] || key;
}

function fmtTemp(c, unit) {
  if (unit === 'f') return '' + Math.round(c * 9 / 5 + 32);
  return '' + Math.round(c);
}
function tempUnit(unit) { return unit === 'f' ? '°F' : '°C'; }

// ── Export principal ──────────────────────────────────────────
export function buildWeatherHtml(d, cfg) {
  if (d.om_temp === null || d.om_temp === undefined) return '';
  var lang = cfg.lang;
  var code = d.om_weather_code || 0;
  var temp = fmtTemp(d.om_temp, cfg.tempUnit);
  var tu   = tempUnit(cfg.tempUnit);
  var prec = (d.om_precip_prob != null) ? d.om_precip_prob : '—';
  var wind = (d.om_wind != null) ? (+d.om_wind).toFixed(1) : '—';
  var aqiV = d.om_aqi, polV = d.om_pollen;
  var aC = aqiCls(aqiV), pC = pollenCls(polV);
  var aL = aqiLbl(aqiV, lang), pL = pollenLbl(polV, lang);
  var aV = (aqiV != null) ? Math.round(aqiV) : '—';
  var pV = (polV != null && !isNaN(+polV)) ? (+polV).toFixed(1) : '—';

  return (
    '<div class="wmain">' +
      '<div class="wicon">' + wSVG(code) + '</div>' +
      '<div style="display:flex;flex-direction:column;justify-content:center;">' +
        '<div class="wtemp">' + temp + '<span class="wtemp-unit"> ' + tu + '</span></div>' +
        '<div class="wcond">' + wmoLabel(code, lang) + '</div>' +
      '</div>' +
    '</div>' +
    '<div class="wstats">' +
      '<div class="wcard"><div class="wcard-title">' + T_weather(lang,'precip') + '</div>' +
        '<div class="wcard-val" style="color:#fff;">' + prec +
          '<span class="wcard-lbl" style="font-size:.55em;opacity:.6;">' + (prec!=='—'?' %':'') + '</span>' +
        '</div></div>' +
      '<div class="wcard"><div class="wcard-title">' + T_weather(lang,'wind') + '</div>' +
        '<div class="wcard-val" style="color:#fff;">' + wind +
          '<span class="wcard-lbl" style="font-size:.55em;opacity:.6;">' + (wind!=='—'?' km/h':'') + '</span>' +
        '</div></div>' +
    '</div>' +
    '<div class="wair">' +
      '<div class="wcard"><div class="wcard-title">' + T_weather(lang,'pollen') + '</div>' +
        '<div class="wcard-val ' + pC + '">' + (pV!=='—' ? pV + '<span style="display:block;font-size:.38em;font-weight:normal;opacity:.75;letter-spacing:0;margin-top:.1em;">grains/m³</span>' : '—') + '</div>' +
        '<div class="wcard-lbl ' + pC + '">' + pL + '</div>' +
      '</div>' +
      '<div class="wcard"><div class="wcard-title">' + T_weather(lang,'air_quality') + '</div>' +
        '<div class="wcard-val ' + aC + '">' + aV + '</div>' +
        '<div class="wcard-lbl ' + aC + '">' + aL + '</div>' +
      '</div>' +
    '</div>' +
    '<div class="wsource">' + (d.aether_model || 'Open-Meteo') + '</div>'
  );
}
