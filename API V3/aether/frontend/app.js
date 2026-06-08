'use strict';

// ═══════════════════════════════════════
//  CONSTANTS
// ═══════════════════════════════════════

const WMO = {
  0:['☀️','Ciel dégagé','Clear sky'], 1:['🌤️','Principalement dégagé','Mainly clear'],
  2:['⛅','Partiellement nuageux','Partly cloudy'], 3:['☁️','Couvert','Overcast'],
  45:['🌫️','Brouillard','Fog'], 48:['🌫️','Brouillard givrant','Icing fog'],
  51:['🌦️','Bruine légère','Light drizzle'], 53:['🌦️','Bruine modérée','Moderate drizzle'],
  55:['🌧️','Bruine dense','Dense drizzle'], 61:['🌧️','Pluie légère','Slight rain'],
  63:['🌧️','Pluie modérée','Moderate rain'], 65:['🌧️','Pluie forte','Heavy rain'],
  71:['❄️','Neige légère','Slight snow'], 73:['❄️','Neige modérée','Moderate snow'],
  75:['❄️','Neige forte','Heavy snow'], 77:['🌨️','Grains de neige','Snow grains'],
  80:['🌦️','Averses légères','Slight showers'], 81:['🌧️','Averses modérées','Moderate showers'],
  82:['⛈️','Averses violentes','Violent showers'], 85:['🌨️','Averses de neige','Snow showers'],
  86:['🌨️','Fortes averses neige','Heavy snow showers'],
  95:['⛈️','Orage','Thunderstorm'], 96:['⛈️','Orage + grêle','Thunderstorm w/ hail'],
  99:['⛈️','Orage, grêle forte','Thunderstorm, heavy hail'],
};

const WEATHER_PARAMS = [
  {k:'temperature_2m',       i:'🌡️', fr:'Température',          en:'Temperature',        u:'°C'},
  {k:'relative_humidity_2m', i:'💧', fr:'Humidité relative',     en:'Relative Humidity',  u:'%'},
  {k:'apparent_temperature', i:'🤔', fr:'Température ressentie', en:'Feels Like',         u:'°C'},
  {k:'precipitation',        i:'🌧️', fr:'Précipitations',        en:'Precipitation',      u:'mm'},
  {k:'weather_code',         i:'🌤️', fr:'Code météo (WMO)',      en:'Weather Code',       u:''},
  {k:'cloud_cover',          i:'☁️', fr:'Couverture nuageuse',   en:'Cloud Cover',        u:'%'},
  {k:'surface_pressure',     i:'📊', fr:'Pression surface',      en:'Surface Pressure',   u:'hPa'},
  {k:'pressure_msl',         i:'📊', fr:'Pression niveau mer',   en:'Sea Level Pressure', u:'hPa'},
  {k:'wind_speed_10m',       i:'💨', fr:'Vitesse du vent',       en:'Wind Speed',         u:'km/h'},
  {k:'wind_direction_10m',   i:'🧭', fr:'Direction du vent',     en:'Wind Direction',     u:'°'},
  {k:'wind_gusts_10m',       i:'🌪️', fr:'Rafales',              en:'Wind Gusts',         u:'km/h'},
  {k:'uv_index',             i:'☀️', fr:'Indice UV',             en:'UV Index',           u:''},
  {k:'visibility',           i:'👁️', fr:'Visibilité',           en:'Visibility',         u:'m'},
  {k:'is_day',               i:'🌅', fr:'Jour / Nuit',           en:'Day / Night',        u:''},
  {k:'shortwave_radiation',  i:'🔆', fr:'Rayonnement solaire',   en:'Solar Radiation',    u:'W/m²'},
];

const AIR_PARAMS = [
  {k:'european_aqi',    i:'🇪🇺', fr:'IQA Européen',              en:'European AQI',     u:'(0-100+)', g:'air'},
  {k:'us_aqi',          i:'🇺🇸', fr:'IQA Américain',             en:'US AQI',           u:'(0-500)',  g:'air'},
  {k:'pm10',            i:'🔬', fr:'PM10',                       en:'PM10',             u:'μg/m³',   g:'air'},
  {k:'pm2_5',           i:'🔬', fr:'PM2.5',                      en:'PM2.5',            u:'μg/m³',   g:'air'},
  {k:'carbon_monoxide', i:'💨', fr:'Monoxyde de carbone',        en:'Carbon Monoxide',  u:'μg/m³',   g:'air'},
  {k:'nitrogen_dioxide',i:'💨', fr:"Dioxyde d'azote (NO₂)",      en:'Nitrogen Dioxide', u:'μg/m³',   g:'air'},
  {k:'sulphur_dioxide', i:'💨', fr:"Dioxyde de soufre (SO₂)",    en:'Sulphur Dioxide',  u:'μg/m³',   g:'air'},
  {k:'ozone',           i:'🌀', fr:'Ozone (O₃)',                  en:'Ozone (O₃)',        u:'μg/m³',   g:'air'},
  {k:'dust',            i:'🌾', fr:'Poussières',                 en:'Dust',             u:'μg/m³',   g:'air'},
  {k:'alder_pollen',    i:'🌿', fr:"Pollen d'aulne",             en:'Alder Pollen',     u:'g/m³',    g:'pollen'},
  {k:'birch_pollen',    i:'🌿', fr:'Pollen de bouleau',          en:'Birch Pollen',     u:'g/m³',    g:'pollen'},
  {k:'grass_pollen',    i:'🌿', fr:'Pollen de graminées',        en:'Grass Pollen',     u:'g/m³',    g:'pollen'},
  {k:'mugwort_pollen',  i:'🌿', fr:"Pollen d'armoise",           en:'Mugwort Pollen',   u:'g/m³',    g:'pollen'},
  {k:'olive_pollen',    i:'🌿', fr:"Pollen d'olivier",           en:'Olive Pollen',     u:'g/m³',    g:'pollen'},
  {k:'ragweed_pollen',  i:'🌿', fr:"Pollen d'ambroisie",         en:'Ragweed Pollen',   u:'g/m³',    g:'pollen'},
];

// ═══════════════════════════════════════
//  MODEL RECOMMENDATIONS BY COUNTRY
// ═══════════════════════════════════════

// Keys = ISO 3166-1 alpha-2 country codes
const MODEL_RECOMMENDATIONS = {
  // France & DOM-TOM
  FR:{m:'meteofrance_seamless', fr:'Météo-France — modèle officiel français, excellent pour la France et l\'Europe.', en:'Météo-France — official French model, excellent for France and Europe.'},
  GP:{m:'meteofrance_seamless', fr:'Météo-France — couvre les Antilles françaises.', en:'Météo-France — covers the French Antilles.'},
  MQ:{m:'meteofrance_seamless', fr:'Météo-France — couvre les Antilles françaises.', en:'Météo-France — covers the French Antilles.'},
  GF:{m:'meteofrance_seamless', fr:'Météo-France — couvre la Guyane française.', en:'Météo-France — covers French Guiana.'},
  RE:{m:'meteofrance_seamless', fr:'Météo-France — couvre La Réunion.', en:'Météo-France — covers Réunion island.'},
  PM:{m:'meteofrance_seamless', fr:'Météo-France — couvre Saint-Pierre-et-Miquelon.', en:'Météo-France — covers Saint-Pierre-et-Miquelon.'},
  YT:{m:'meteofrance_seamless', fr:'Météo-France — couvre Mayotte.', en:'Météo-France — covers Mayotte.'},
  // Germany + Central Europe
  DE:{m:'dwd_icon_seamless', fr:'DWD ICON — modèle officiel allemand, très précis en Europe centrale.', en:'DWD ICON — official German model, very accurate in Central Europe.'},
  AT:{m:'dwd_icon_seamless', fr:'DWD ICON — excellent pour l\'Autriche et les Alpes.', en:'DWD ICON — excellent for Austria and the Alps.'},
  CH:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour la Suisse.', en:'DWD ICON — recommended for Switzerland.'},
  LI:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour le Liechtenstein.', en:'DWD ICON — recommended for Liechtenstein.'},
  CZ:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour la République tchèque.', en:'DWD ICON — recommended for Czechia.'},
  PL:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour la Pologne.', en:'DWD ICON — recommended for Poland.'},
  SK:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour la Slovaquie.', en:'DWD ICON — recommended for Slovakia.'},
  HU:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour la Hongrie.', en:'DWD ICON — recommended for Hungary.'},
  NL:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour les Pays-Bas.', en:'DWD ICON — recommended for the Netherlands.'},
  BE:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour la Belgique.', en:'DWD ICON — recommended for Belgium.'},
  LU:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour le Luxembourg.', en:'DWD ICON — recommended for Luxembourg.'},
  DK:{m:'dwd_icon_seamless', fr:'DWD ICON — recommandé pour le Danemark.', en:'DWD ICON — recommended for Denmark.'},
  NO:{m:'dwd_icon_seamless', fr:'DWD ICON — bonne couverture Scandinavie.', en:'DWD ICON — good Scandinavia coverage.'},
  SE:{m:'dwd_icon_seamless', fr:'DWD ICON — bonne couverture Scandinavie.', en:'DWD ICON — good Scandinavia coverage.'},
  FI:{m:'dwd_icon_seamless', fr:'DWD ICON — bonne couverture Scandinavie.', en:'DWD ICON — good Scandinavia coverage.'},
  // UK & Ireland
  GB:{m:'ukmo_seamless', fr:'UK Met Office — modèle officiel britannique, très précis sur les îles britanniques.', en:'UK Met Office — official British model, very accurate over the British Isles.'},
  IE:{m:'ukmo_seamless', fr:'UK Met Office — recommandé pour l\'Irlande.', en:'UK Met Office — recommended for Ireland.'},
  IM:{m:'ukmo_seamless', fr:'UK Met Office — recommandé pour l\'île de Man.', en:'UK Met Office — recommended for the Isle of Man.'},
  JE:{m:'ukmo_seamless', fr:'UK Met Office — recommandé pour Jersey.', en:'UK Met Office — recommended for Jersey.'},
  GG:{m:'ukmo_seamless', fr:'UK Met Office — recommandé pour Guernesey.', en:'UK Met Office — recommended for Guernsey.'},
  // Americas
  US:{m:'gfs_seamless', fr:'NOAA GFS — modèle officiel américain, idéal pour les États-Unis.', en:'NOAA GFS — official US model, ideal for the United States.'},
  CA:{m:'gfs_seamless', fr:'NOAA GFS — recommandé pour le Canada.', en:'NOAA GFS — recommended for Canada.'},
  MX:{m:'gfs_seamless', fr:'NOAA GFS — recommandé pour le Mexique.', en:'NOAA GFS — recommended for Mexico.'},
  BR:{m:'gfs_seamless', fr:'NOAA GFS — recommandé pour le Brésil.', en:'NOAA GFS — recommended for Brazil.'},
  AR:{m:'gfs_seamless', fr:'NOAA GFS — recommandé pour l\'Argentine.', en:'NOAA GFS — recommended for Argentina.'},
  CL:{m:'gfs_seamless', fr:'NOAA GFS — recommandé pour le Chili.', en:'NOAA GFS — recommended for Chile.'},
  // Default (rest of world) → best_match
};

// ═══════════════════════════════════════
//  I18N
// ═══════════════════════════════════════

const I18N = {
  fr: {
    hdr_sub:'Tableau de bord météo V1.1 — Propulsé par Astralcodes',
    tl_cfg:'Configuration', tl_live:'Résultats Live', tl_docs:'Documentation',
    lbl_loc:'Localisation',
    lbl_wp:'Paramètres Météo',
    lbl_wd:"Sélectionnez les paramètres météorologiques à exposer via l'API.",
    lbl_ep:'Paramètres Environnement',
    lbl_ed:"Sélectionnez les paramètres de qualité de l'air et pollens à exposer via l'API.",
    lbl_model:'Modèle de prévision',
    lbl_model_d:'Choisissez la source de données météorologiques.',
    lbl_src:'Source', lbl_provider:'Fournisseur', lbl_region:'Région',
    lbl_dark:'Thème sombre', lbl_light:'Thème clair',
    lbl_rec:'💡 Recommandé pour cette localisation',
    lbl_apply:'Appliquer',
    lbl_fc:'Prévisions 24 h',
    chart_temp:'Température (°C)', chart_precip:'Précipitations (mm)',
    lbl_wh:'Webhook',
    lbl_wh_d:'Envoyez les données vers une URL externe (Discord, Slack, n8n, Make…).',
    lbl_wh_hint:'Discord : format embed automatique. Autres : JSON brut.',
    lbl_wh_btn:'Envoyer maintenant',
    lbl_wh_ok:'✓ Envoyé', lbl_wh_err:'✗ Erreur', lbl_wh_nourl:'⚠️ Entrez une URL',
    lbl_rl_cfg:'Limitation de débit',
    lbl_rl_d:'Nombre maximum de requêtes par minute acceptées par le serveur (max absolu : 7 000 req/min).',
    lbl_rl_s:'Recherche ville (req/min)', lbl_rl_l:'Données live (req/min)',
    lbl_rl_max:'Maximum absolu : 7 000 req/min',
    city_ph:'Rechercher une ville...', city_searching:'Recherche...', city_none:'Aucun résultat',
    city_err:'Serveur de géocodage inaccessible',
    d_rec_t:'Recommandations de modèles par région',
    d_tos_t:'Conditions d\'utilisation — Open-Meteo',
    ss_ok:'✓ Sauvegardé', ss_wait:'⏳ Sauvegarde...', ss_err:'✗ Erreur',
    lbl_vfmt:'Mise en page', lbl_vraw:'JSON brut', lbl_ref:'Actualiser',
    lbl_load:'Chargement des données...', lbl_err_t:'Erreur de chargement',
    lbl_upd:'Mis à jour à',
    sec_w:'Météo', sec_a:"Qualité de l'air", sec_p:'Pollens',
    no_data:'Aucun paramètre actif. Configurez-les sur la page Configuration.',
    day:'Jour', night:'Nuit',
    wN:'Nord',wNE:'Nord-Est',wE:'Est',wSE:'Sud-Est',wS:'Sud',wSW:'Sud-Ouest',wW:'Ouest',wNW:'Nord-Ouest',
    aq:['Bon','Moyen','Médiocre','Mauvais','Très mauvais','Extrêmement mauvais'],
    pl:['Très faible','Faible','Modéré','Élevé','Très élevé'],
    // Docs page
    d_ov_t:"Vue d'ensemble",
    d_ov:`Cette application est une <strong>API proxy</strong> vers les services gratuits
      <a href="https://open-meteo.com" target="_blank" class="text-blue-600 hover:underline">Open-Meteo</a>.
      Elle permet de configurer dynamiquement les paramètres exposés et d'y accéder via un endpoint unifié.<br><br>
      <strong>⚙️ <a href="/" class="text-blue-600 hover:underline">Configuration</a></strong>
        — Choisissez votre ville et les paramètres à collecter. Les modifications sont sauvegardées
        automatiquement dans <code class="bg-slate-100 px-1 rounded text-xs">config.json</code>.<br>
      <strong>📡 <a href="/live" class="text-blue-600 hover:underline">Résultats Live</a></strong>
        — Visualisez les données en temps réel, vue formatée ou JSON brut.<br>
      <strong>📖 Documentation</strong> — Endpoints, rate limiting, et sources de données.`,
    d_ep_t:"Endpoints de l'API",
    d_sw_t:'Documentation Interactive (Swagger)',
    d_sw:"Swagger UI permet de tester tous les endpoints directement depuis le navigateur.",
    lbl_sw:'Ouvrir Swagger UI',
    d_rl_t:'Limitation de débit (Rate Limiting)',
    d_rl:[
      '🔍 <strong>/api/search_city</strong> — configurable (défaut : 10 req/min par IP)',
      '📡 <strong>/api/live_data</strong> — configurable (défaut : 20 req/min par IP)',
      'Au-delà, le serveur répond avec <code class="bg-amber-100 px-1 rounded text-xs">HTTP 429 Too Many Requests</code>.',
      '⚙️ Modifiable depuis la page <a href="/" style="color:#92400e;font-weight:600">Configuration</a> · Maximum absolu : <strong>7 000 req/min</strong>.',
    ],
    d_src_t:'Sources de données (Open-Meteo)',
    ep:[
      "Retourne la configuration actuelle (ville, coordonnées, paramètres actifs).",
      "Met à jour partiellement la configuration. Champs omis inchangés.",
      "Recherche géographique de villes (max 8 résultats).",
      "Données météo et qualité de l'air agrégées selon la configuration active.",
      "Liste des modèles météo disponibles avec leurs métadonnées (fournisseur, région…).",
      "Envoie les données live vers l'URL webhook configurée (Discord embed ou JSON brut).",
    ],
    src:[
      ['Geocoding API','Recherche de villes et coordonnées géographiques.','https://open-meteo.com/en/docs/geocoding-api'],
      ['Weather API','Prévisions météo et données actuelles (1000+ variables).','https://open-meteo.com/en/docs'],
      ['Air Quality API',"Qualité de l'air, particules fines et pollens.","https://open-meteo.com/en/docs/air-quality-api"],
    ],
  },
  en: {
    hdr_sub:'Weather dashboard — by Astralcodes',
    tl_cfg:'Configuration', tl_live:'Live Results', tl_docs:'Documentation',
    lbl_loc:'Location',
    lbl_wp:'Weather Parameters',
    lbl_wd:'Select the weather parameters to expose via the API.',
    lbl_ep:'Environment Parameters',
    lbl_ed:'Select the air quality and pollen parameters to expose via the API.',
    lbl_model:'Forecast model',
    lbl_model_d:'Choose the meteorological data source.',
    lbl_src:'Source', lbl_provider:'Provider', lbl_region:'Region',
    lbl_dark:'Dark mode', lbl_light:'Light mode',
    lbl_rec:'💡 Recommended for this location',
    lbl_apply:'Apply',
    lbl_fc:'24 h Forecast',
    chart_temp:'Temperature (°C)', chart_precip:'Precipitation (mm)',
    lbl_wh:'Webhook',
    lbl_wh_d:'Push data to an external URL (Discord, Slack, n8n, Make…).',
    lbl_wh_hint:'Discord: automatic embed format. Others: raw JSON.',
    lbl_wh_btn:'Send now',
    lbl_wh_ok:'✓ Sent', lbl_wh_err:'✗ Error', lbl_wh_nourl:'⚠️ Enter a URL first',
    lbl_rl_cfg:'Rate Limiting',
    lbl_rl_d:'Maximum requests per minute accepted by the server (absolute max: 7,000 req/min).',
    lbl_rl_s:'City search (req/min)', lbl_rl_l:'Live data (req/min)',
    lbl_rl_max:'Absolute maximum: 7,000 req/min',
    city_ph:'Search for a city...', city_searching:'Searching...', city_none:'No results',
    city_err:'Geocoding server unreachable',
    d_rec_t:'Model recommendations by region',
    d_tos_t:'Terms of Use — Open-Meteo',
    ss_ok:'✓ Saved', ss_wait:'⏳ Saving...', ss_err:'✗ Error',
    lbl_vfmt:'Formatted', lbl_vraw:'Raw JSON', lbl_ref:'Refresh',
    lbl_load:'Loading data...', lbl_err_t:'Load error',
    lbl_upd:'Updated at',
    sec_w:'Weather', sec_a:'Air Quality', sec_p:'Pollen',
    no_data:'No active parameters. Configure them on the Configuration page.',
    day:'Day', night:'Night',
    wN:'North',wNE:'Northeast',wE:'East',wSE:'Southeast',wS:'South',wSW:'Southwest',wW:'West',wNW:'Northwest',
    aq:['Good','Fair','Moderate','Poor','Very Poor','Extremely Poor'],
    pl:['Very Low','Low','Moderate','High','Very High'],
    d_ov_t:'Overview',
    d_ov:`This application is a <strong>proxy API</strong> towards the free
      <a href="https://open-meteo.com" target="_blank" class="text-blue-600 hover:underline">Open-Meteo</a> services.
      It lets you dynamically configure which data is exposed and access it via a unified endpoint.<br><br>
      <strong>⚙️ <a href="/" class="text-blue-600 hover:underline">Configuration</a></strong>
        — Choose your city and parameters. Changes are auto-saved to
        <code class="bg-slate-100 px-1 rounded text-xs">config.json</code>.<br>
      <strong>📡 <a href="/live" class="text-blue-600 hover:underline">Live Results</a></strong>
        — View real-time data in formatted or raw JSON view.<br>
      <strong>📖 Documentation</strong> — Endpoints, rate limiting, and data sources.`,
    d_ep_t:'API Endpoints',
    d_sw_t:'Interactive Documentation (Swagger)',
    d_sw:'Swagger UI lets you test all endpoints directly from the browser.',
    lbl_sw:'Open Swagger UI',
    d_rl_t:'Rate Limiting',
    d_rl:[
      '🔍 <strong>/api/search_city</strong> — configurable (default: 10 req/min per IP)',
      '📡 <strong>/api/live_data</strong> — configurable (default: 20 req/min per IP)',
      'Beyond that, the server responds with <code class="bg-amber-100 px-1 rounded text-xs">HTTP 429 Too Many Requests</code>.',
      '⚙️ Configurable from the <a href="/" style="color:#92400e;font-weight:600">Configuration</a> page · Absolute maximum: <strong>7,000 req/min</strong>.',
    ],
    d_src_t:'Data Sources (Open-Meteo)',
    ep:[
      'Returns the current configuration (city, coordinates, active parameters).',
      'Partially updates the configuration. Omitted fields are unchanged.',
      'Geographic city search (max 8 results).',
      'Aggregated weather and air quality data based on the active configuration.',
      'List of available weather models with metadata (provider, region…).',
      'Pushes live data to the configured webhook URL (Discord embed or raw JSON).',
    ],
    src:[
      ['Geocoding API','City search and geographic coordinates.','https://open-meteo.com/en/docs/geocoding-api'],
      ['Weather API','Weather forecasts and current conditions (1000+ variables).','https://open-meteo.com/en/docs'],
      ['Air Quality API','Air quality, fine particles and pollen.','https://open-meteo.com/en/docs/air-quality-api'],
    ],
  },
};

// ═══════════════════════════════════════
//  STATE
// ═══════════════════════════════════════

let lang = localStorage.getItem('meteo-lang') || 'fr';

const DEFAULT_CONFIG = {
  city:'Paris', latitude:48.8566, longitude:2.3522,
  weather_model:'best_match',
  weather_params:['temperature_2m','relative_humidity_2m','apparent_temperature',
    'precipitation','weather_code','cloud_cover','wind_speed_10m',
    'wind_direction_10m','wind_gusts_10m','uv_index'],
  air_quality_params:['european_aqi','pm10','pm2_5','grass_pollen','birch_pollen'],
};

let config = JSON.parse(JSON.stringify(DEFAULT_CONFIG));
let saveTimer = null;

// ═══════════════════════════════════════
//  UTILS
// ═══════════════════════════════════════

const $ = id => document.getElementById(id);
const t = k => I18N[lang]?.[k] ?? k;
const esc = s => String(s == null ? '' : s)
  .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const isDark = () => document.documentElement.classList.contains('dark');
const p = (light, dark) => isDark() ? dark : light;

function setText(id, text) { const e = $(id); if (e) e.textContent = text; }
function setHtml(id, html)  { const e = $(id); if (e) e.innerHTML  = html;  }
function show(id)  { const e = $(id); if (e) e.style.display = ''; }
function hide(id)  { const e = $(id); if (e) e.style.display = 'none'; }

function debounce(fn, ms) {
  let tid;
  return (...a) => { clearTimeout(tid); tid = setTimeout(() => fn(...a), ms); };
}

function windDir(deg) {
  return t(['wN','wNE','wE','wSE','wS','wSW','wW','wNW'][Math.round(deg/45)%8]);
}
function windArrow(deg) {
  return ['↑','↗','→','↘','↓','↙','←','↖'][Math.round(deg/45)%8];
}

function aqiEu(v) {
  const i = v<=20?0:v<=40?1:v<=60?2:v<=80?3:v<=100?4:5;
  return {i, label:t('aq')[i]};
}
function aqiUs(v) {
  const i = v<=50?0:v<=100?1:v<=150?2:v<=200?3:v<=300?4:5;
  return {i, label:t('aq')[i]};
}
function pollenLvl(v) {
  const i = v<=10?0:v<=30?1:v<=80?2:v<=200?3:4;
  return {i, label:t('pl')[i]};
}

// ═══════════════════════════════════════
//  API
// ═══════════════════════════════════════

async function apiFetch(url, opts={}) {
  const r = await fetch(url, opts);
  if (!r.ok) {
    const e = await r.json().catch(() => ({detail:r.statusText}));
    throw new Error(e.detail || r.statusText);
  }
  return r.json();
}

async function loadConfig() {
  config = await apiFetch('/api/config');
}

async function pushConfig(partial) {
  setSaveStatus('wait');
  try {
    const res = await apiFetch('/api/config', {
      method:'POST',
      headers:{'Content-Type':'application/json'},
      body:JSON.stringify(partial),
    });
    config = res.config;
    setSaveStatus('ok');
  } catch { setSaveStatus('err'); }
}

// ═══════════════════════════════════════
//  COMMON UI
// ═══════════════════════════════════════

function applyI18n() {
  setText('hdr-sub', t('hdr_sub'));
  setText('tl-cfg',  t('tl_cfg'));
  setText('tl-live', t('tl_live'));
  setText('tl-docs', t('tl_docs'));
  // Config page labels
  setText('lbl-loc',     t('lbl_loc'));
  setText('lbl-model',   t('lbl_model'));
  setText('lbl-model-d', t('lbl_model_d'));
  setText('lbl-rl-cfg',  t('lbl_rl_cfg'));
  setText('lbl-rl-d',    t('lbl_rl_d'));
  setText('lbl-rl-s',    t('lbl_rl_s'));
  setText('lbl-rl-l',    t('lbl_rl_l'));
  setText('lbl-rl-max',  t('lbl_rl_max'));
  setText('lbl-wh',      t('lbl_wh'));
  setText('lbl-wh-d',    t('lbl_wh_d'));
  setText('lbl-wh-hint', t('lbl_wh_hint'));
  setText('lbl-wh-btn',  t('lbl_wh_btn'));
  setText('lbl-fc',      t('lbl_fc'));
  const dtBtn = $('dark-toggle');
  if (dtBtn) dtBtn.title = isDark() ? t('lbl_light') : t('lbl_dark');
  setText('lbl-wp',  t('lbl_wp'));
  setText('lbl-wd',  t('lbl_wd'));
  setText('lbl-ep',  t('lbl_ep'));
  setText('lbl-ed',  t('lbl_ed'));
  const ci = $('city-input'); if (ci) ci.placeholder = t('city_ph');
  // Live page labels
  setText('lbl-vfmt', t('lbl_vfmt'));
  setText('lbl-vraw', t('lbl_vraw'));
  setText('lbl-ref',  t('lbl_ref'));
  setText('lbl-load', t('lbl_load'));
  setText('lbl-err-t',t('lbl_err_t'));
  // Docs page labels
  setText('d-ov-t',   t('d_ov_t'));
  setHtml('d-ov',     t('d_ov'));
  setText('d-ep-t',   t('d_ep_t'));
  setText('d-sw-t',   t('d_sw_t'));
  setText('d-sw',     t('d_sw'));
  setText('lbl-sw',   t('lbl_sw'));
  setText('d-rl-t',   t('d_rl_t'));
  setText('d-src-t',  t('d_src_t'));
  setText('d-rec-t',  t('d_rec_t'));
  setText('d-tos-t',  t('d_tos_t'));
}

function initLangSwitcher() {
  const sw = $('lang-sw');
  if (!sw) return;
  sw.value = lang;
  sw.addEventListener('change', e => {
    lang = e.target.value;
    localStorage.setItem('meteo-lang', lang);
    applyI18n();
    // Re-render page-specific content
    if ($('weather-grid')) renderCheckboxes();
    if ($('model-sel')) renderModelSelector();
    if ($('live-fmt') && window._liveData) renderFormatted(window._liveData);
    if (_charts && Object.keys(_charts).length) renderForecast(window._liveData);
    if ($('d-ep')) renderDocs();
  });
}

let _saveTimer2 = null;
function setSaveStatus(state) {
  const el = $('save-status');
  if (!el) return;
  clearTimeout(_saveTimer2);
  const map = {ok:`<span style="color:#059669">${t('ss_ok')}</span>`,
               wait:`<span style="color:#d97706">${t('ss_wait')}</span>`,
               err:`<span style="color:#dc2626">${t('ss_err')}</span>`};
  el.innerHTML = map[state] || '';
  if (state === 'ok') _saveTimer2 = setTimeout(() => el.innerHTML='', 2500);
}

// ═══════════════════════════════════════
//  CONFIG PAGE
// ═══════════════════════════════════════

function renderCheckboxes() {
  buildGrid('weather-grid', WEATHER_PARAMS, config.weather_params || []);
  buildGrid('air-grid',     AIR_PARAMS,     config.air_quality_params || []);
}

function buildGrid(id, params, activeKeys) {
  const el = $(id);
  if (!el) return;
  el.innerHTML = params.map(param => {
    const label   = lang === 'fr' ? param.fr : param.en;
    const checked = activeKeys.includes(param.k);
    const border  = checked ? p('#2563eb','#3b82f6') : p('#d1d5db','#4b5563');
    const bg      = checked ? p('#eff6ff','#1e3a5f') : p('#ffffff','#1f2937');
    const clr     = checked ? p('#1e40af','#93c5fd') : p('#374151','#e5e7eb');
    const fw      = checked ? '600' : '500';
    const chk     = checked ? p('#2563eb','#60a5fa') : p('#d1d5db','#4b5563');
    return `<label style="display:flex;align-items:center;gap:10px;padding:11px 14px;
        border-radius:10px;border:2px solid ${border};background:${bg};
        cursor:pointer;user-select:none;transition:border-color .15s,background .15s;
        box-shadow:0 1px 3px rgba(0,0,0,.04)">
      <input type="checkbox" data-k="${esc(param.k)}"${checked?' checked':''}
        style="width:16px;height:16px;accent-color:#2563eb;flex-shrink:0;cursor:pointer">
      <span style="font-size:19px;flex-shrink:0;line-height:1">${param.i}</span>
      <span style="flex:1;min-width:0;overflow:hidden">
        <div style="font-size:13px;font-weight:${fw};color:${clr};
            white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${esc(label)}</div>
        ${param.u?`<div style="font-size:11px;color:${p('#9ca3af','#64748b')};margin-top:1px">${esc(param.u)}</div>`:''}
      </span>
      <span style="font-size:13px;color:${chk};flex-shrink:0;font-weight:700">${checked?'✓':''}</span>
    </label>`;
  }).join('');
  el.querySelectorAll('input[type="checkbox"]').forEach(cb => cb.addEventListener('change', onCheck));
}

function onCheck(e) {
  const label = e.target.closest('label');
  const on = e.target.checked;
  label.style.borderColor = on ? p('#2563eb','#3b82f6') : p('#d1d5db','#4b5563');
  label.style.background  = on ? p('#eff6ff','#1e3a5f') : p('#ffffff','#1f2937');
  const divs = label.querySelectorAll('div');
  if (divs[0]) { divs[0].style.color = on ? p('#1e40af','#93c5fd') : p('#374151','#e5e7eb'); divs[0].style.fontWeight = on?'600':'500'; }
  const chkSpan = label.lastElementChild;
  if (chkSpan) { chkSpan.textContent = on?'✓':''; chkSpan.style.color = on ? p('#2563eb','#60a5fa') : p('#d1d5db','#4b5563'); }
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    const wKeys = [...document.querySelectorAll('#weather-grid input:checked')].map(i=>i.dataset.k);
    const aKeys = [...document.querySelectorAll('#air-grid input:checked')].map(i=>i.dataset.k);
    config.weather_params = wKeys;
    config.air_quality_params = aKeys;
    pushConfig({weather_params:wKeys, air_quality_params:aKeys});
  }, 400);
}

function toggleAll(gridId, state) {
  $(gridId).querySelectorAll('input[type="checkbox"]').forEach(cb => {
    cb.checked = state;
    cb.dispatchEvent(new Event('change'));
  });
}

function initCitySearch() {
  const input = $('city-input');
  const dropdown = $('city-dropdown');
  const list = $('city-list');
  const spin = $('search-spin');
  if (!input) return;

  const closeDD = () => { dropdown.style.display = 'none'; };
  document.addEventListener('click', e => {
    if (!input.contains(e.target) && !dropdown.contains(e.target)) closeDD();
  });
  input.addEventListener('keydown', e => { if (e.key === 'Escape') { closeDD(); input.blur(); } });

  const doSearch = debounce(async q => {
    if (q.length < 2) { closeDD(); return; }
    spin.style.display = '';
    list.innerHTML = `<div style="padding:12px 16px;color:#94a3b8;text-align:center;font-size:13px">
      ${esc(t('city_searching'))}</div>`;
    dropdown.style.display = '';
    try {
      const data = await apiFetch('/api/search_city?q=' + encodeURIComponent(q));
      const results = data.results || [];
      if (!results.length) {
        list.innerHTML = `<div style="padding:12px 16px;color:#94a3b8;text-align:center;font-size:13px">
          🔍 ${esc(t('city_none'))}</div>`;
      } else {
        list.innerHTML = results.map(r => `
          <div class="city-result"
            data-name="${esc(r.name)}" data-country="${esc(r.country||'')}"
            data-cc="${esc(r.country_code||'')}"
            data-admin="${esc(r.admin1||'')}" data-lat="${r.latitude}" data-lon="${r.longitude}">
            <div>
              <div class="cr-name">${esc(r.name)}${r.admin1?', '+esc(r.admin1):''}</div>
              <div class="cr-sub">${esc(r.country||'')}</div>
            </div>
            <div class="cr-coords">${r.latitude.toFixed(2)}, ${r.longitude.toFixed(2)}</div>
          </div>`).join('');
        list.querySelectorAll('.city-result').forEach(row =>
          row.addEventListener('click', () => selectCity(row)));
      }
    } catch {
      list.innerHTML = `<div style="padding:12px 16px;color:#ef4444;text-align:center;font-size:13px">
        ⚠️ ${esc(t('city_err'))}</div>`;
    } finally { spin.style.display = 'none'; }
  }, 320);

  input.addEventListener('input', e => doSearch(e.target.value.trim()));
}

function selectCity(row) {
  const name    = row.dataset.name;
  const country = row.dataset.country;
  const cc      = row.dataset.cc || '';
  const admin   = row.dataset.admin;
  const lat     = parseFloat(row.dataset.lat);
  const lon     = parseFloat(row.dataset.lon);
  const display = [name, admin, country].filter(Boolean).join(', ');
  $('city-input').value = display;
  $('city-dropdown').style.display = 'none';
  showCityInfo(display, lat, lon);
  pushConfig({city:display, latitude:lat, longitude:lon});
  if (cc) showModelRecommendation(cc);
}

function showModelRecommendation(countryCode) {
  const el = $('model-rec');
  if (!el) return;
  const rec = MODEL_RECOMMENDATIONS[countryCode.toUpperCase()];
  const sel = $('model-sel');
  if (!rec) { el.style.display = 'none'; return; }
  const curModel = sel ? sel.value : (config.weather_model || 'best_match');
  if (curModel === rec.m) { el.style.display = 'none'; return; }
  const reason = lang === 'fr' ? rec.fr : rec.en;
  const modelName = _cachedModels
    ? (lang === 'fr' ? (_cachedModels[rec.m]?.name || rec.m) : (_cachedModels[rec.m]?.name_en || rec.m))
    : rec.m;
  el.innerHTML = `
    <div style="display:flex;align-items:flex-start;gap:10px;padding:10px 14px;
        background:${p('#f0fdf4','rgba(5,46,22,.35)')};border:1.5px solid ${p('#86efac','#166534')};
        border-radius:10px;font-size:13px">
      <span style="font-size:16px;flex-shrink:0;margin-top:1px">💡</span>
      <div style="flex:1;min-width:0">
        <div style="font-weight:600;color:${p('#166534','#4ade80')};margin-bottom:2px">${esc(t('lbl_rec'))}</div>
        <div style="color:${p('#15803d','#86efac')}"><strong>${esc(modelName)}</strong> — ${esc(reason)}</div>
      </div>
      <button onclick="applyRecommendedModel('${esc(rec.m)}')"
        style="flex-shrink:0;background:#16a34a;color:white;border:none;border-radius:8px;
            padding:5px 12px;font-size:12px;font-weight:600;cursor:pointer;white-space:nowrap">
        ${esc(t('lbl_apply'))}
      </button>
    </div>`;
  el.style.display = '';
}

function applyRecommendedModel(modelId) {
  const sel = $('model-sel');
  if (sel) sel.value = modelId;
  config.weather_model = modelId;
  pushConfig({weather_model: modelId});
  if (_cachedModels) updateModelInfo(_cachedModels[modelId], $('model-info'));
  const el = $('model-rec'); if (el) el.style.display = 'none';
}

function showCityInfo(name, lat, lon) {
  if (!name || !$('city-info')) return;
  $('city-disp').textContent = name;
  $('lat-d').textContent = (typeof lat === 'number' ? lat : parseFloat(lat)).toFixed(4);
  $('lon-d').textContent = (typeof lon === 'number' ? lon : parseFloat(lon)).toFixed(4);
  $('city-info').style.display = '';
}

// ═══════════════════════════════════════
//  RATE LIMIT INPUTS (Config page)
// ═══════════════════════════════════════

function syncRateLimitInputs() {
  const rs = $('rl-search'); if (rs) rs.value = config.rate_limit_search ?? 10;
  const rl = $('rl-live');   if (rl) rl.value = config.rate_limit_live   ?? 20;
}

function initRateLimitInputs() {
  const makeHandler = (field) => debounce((e) => {
    let v = parseInt(e.target.value, 10);
    if (isNaN(v) || v < 1) v = 1;
    if (v > 7000) { v = 7000; e.target.value = 7000; }
    config[field] = v;
    pushConfig({[field]: v});
  }, 600);
  const rs = $('rl-search'); if (rs) rs.addEventListener('input', makeHandler('rate_limit_search'));
  const rl = $('rl-live');   if (rl) rl.addEventListener('input', makeHandler('rate_limit_live'));
}

// ═══════════════════════════════════════
//  MODEL SELECTOR (Config page)
// ═══════════════════════════════════════

let _cachedModels = null;

async function renderModelSelector() {
  const sel  = $('model-sel');
  const info = $('model-info');
  if (!sel) return;
  try {
    if (!_cachedModels) {
      const data = await apiFetch('/api/weather_models');
      _cachedModels = data.models || {};
    }
    const models = _cachedModels;
    const curId  = config.weather_model || 'best_match';
    sel.innerHTML = Object.entries(models).map(([id, m]) => {
      const name   = lang === 'fr' ? m.name : (m.name_en || m.name);
      const region = m.region ? ` — ${m.region}` : '';
      return `<option value="${esc(id)}"${id===curId?' selected':''}>${esc(name)}${esc(region)}</option>`;
    }).join('');
    if (info) updateModelInfo(models[curId], info);
    sel.onchange = null;
    sel.addEventListener('change', () => {
      const id = sel.value;
      config.weather_model = id;
      pushConfig({weather_model: id});
      if (info) updateModelInfo(models[id], info);
    });
  } catch {
    sel.innerHTML = '<option value="best_match">Sélection automatique (best_match)</option>';
  }
}

function updateModelInfo(model, el) {
  if (!model || !el) return;
  const desc = lang === 'fr'
    ? (model.description || '')
    : (model.description_en || model.description || '');
  el.innerHTML =
    `<span style="background:${p('#eff6ff','#1e3a5f')};color:${p('#1d4ed8','#93c5fd')};border-radius:9999px;
        padding:2px 10px;font-size:11px;font-weight:700">${esc(model.region || '')}</span>` +
    `<span style="font-size:12px;color:${p('#64748b','#d1d5db')}">${esc(model.provider || '')}</span>` +
    (desc ? `<span style="font-size:12px;color:${p('#94a3b8','#9ca3af')};font-style:italic"> — ${esc(desc)}</span>` : '');
  el.style.display = 'flex';
}

// ═══════════════════════════════════════
//  LIVE PAGE
// ═══════════════════════════════════════

let liveView = 'fmt';

async function refreshLive() {
  hide('live-err'); hide('live-fmt'); hide('raw-json');
  show('live-load');
  const cnt = $('auto-refresh-cnt'); if (cnt) cnt.textContent = '';
  try {
    const data = await apiFetch('/api/live_data');
    window._liveData = data;
    const now = new Date().toLocaleTimeString(lang==='fr'?'fr-FR':'en-GB',
      {hour:'2-digit',minute:'2-digit',second:'2-digit'});
    setText('last-upd', `${t('lbl_upd')} ${now}`);
    renderFormatted(data);
    renderForecast(data);
    $('raw-json').textContent = JSON.stringify(data, null, 2);
    if (liveView === 'raw') { hide('live-fmt'); show('raw-json'); }
    else show('live-fmt');
  } catch(e) {
    $('err-msg').textContent = e.message;
    show('live-err');
  } finally { hide('live-load'); }
}

function renderFormatted(data) {
  const el = $('live-fmt');
  if (!el) return;
  const w   = data.weather?.current || {};
  const a   = data.air_quality?.current || {};
  const wu  = data.weather?.current_units || {};
  const city = data.config?.city || '';

  const wKeys = Object.keys(w).filter(k => k!=='time' && k!=='interval');
  const aKeys = Object.keys(a).filter(k => k!=='time' && k!=='interval');

  if (!wKeys.length && !aKeys.length) {
    el.innerHTML = `<div style="text-align:center;padding:64px;color:${p('#94a3b8','#6b7280')}">
      <div style="font-size:48px;margin-bottom:12px">📭</div>
      <p style="font-size:14px">${esc(t('no_data'))}</p>
    </div>`;
    return;
  }

  const srcW = data.sources?.weather || null;
  const srcA = data.sources?.air_quality || null;

  let html = '';
  if (city) html += `<div style="font-size:18px;font-weight:700;color:${p('#0f172a','#f9fafb')};margin-bottom:20px">📍 ${esc(city)}</div>`;

  if (wKeys.length) {
    html += `<div class="live-sep">🌤️ ${esc(t('sec_w'))}</div>`;
    if (srcW) html += sourceBanner(srcW, lang);
    html += `<div class="metric-grid">`;
    wKeys.forEach(k => { html += buildCard(k, w[k], wu[k], WEATHER_PARAMS.find(p=>p.k===k)); });
    html += '</div>';
  }

  const airOnly = aKeys.filter(k => AIR_PARAMS.find(p=>p.k===k)?.g==='air');
  if (airOnly.length) {
    html += `<div class="live-sep">🌍 ${esc(t('sec_a'))}</div>`;
    if (srcA) html += sourceBanner(srcA, lang);
    html += `<div class="metric-grid">`;
    airOnly.forEach(k => { html += buildCard(k, a[k], null, AIR_PARAMS.find(p=>p.k===k)); });
    html += '</div>';
  }

  const pollens = aKeys.filter(k => AIR_PARAMS.find(p=>p.k===k)?.g==='pollen');
  if (pollens.length) {
    html += `<div class="live-sep">🌿 ${esc(t('sec_p'))}</div>`;
    if (srcA) html += sourceBanner(srcA, lang);
    html += `<div class="metric-grid">`;
    pollens.forEach(k => { html += buildCard(k, a[k], null, AIR_PARAMS.find(p=>p.k===k)); });
    html += '</div>';
  }

  el.innerHTML = html;
}

function sourceBanner(src, langCode) {
  const name     = langCode === 'en' ? (src.name_en || src.name) : src.name;
  const desc     = langCode === 'en' ? (src.description_en || src.description || '') : (src.description || '');
  const provider = src.provider || '';
  const region   = src.region   || '';
  return `<div style="display:flex;flex-wrap:wrap;align-items:center;gap:8px;
      margin:-6px 0 14px;font-size:12px;color:${p('#64748b','#9ca3af')}">
    <span style="background:${p('#f1f5f9','#374151')};border-radius:9999px;padding:2px 10px;font-weight:700;color:${p('#334155','#f3f4f6')}">
      🛰️ ${esc(name)}
    </span>
    ${region ? `<span style="background:${p('#eff6ff','#1e3a5f')};color:${p('#1d4ed8','#93c5fd')};border-radius:9999px;padding:2px 8px;font-weight:600;font-size:11px">${esc(region)}</span>` : ''}
    <span style="color:${p('#94a3b8','#9ca3af')}">${esc(provider)}</span>
    ${desc ? `<span style="color:${p('#c4c9d4','#6b7280')};font-style:italic">${esc(desc)}</span>` : ''}
  </div>`;
}

function buildCard(key, val, unitOverride, meta) {
  const icon  = meta?.i || '📌';
  const label = meta ? (lang==='fr' ? meta.fr : meta.en) : key;
  const unit  = unitOverride || meta?.u || '';
  let valHtml = '', extraHtml = '';

  const vClr = p('#0f172a','#f9fafb');
  const uClr = p('#64748b','#9ca3af');
  const xClr = p('#475569','#d1d5db');

  if (val == null) {
    valHtml = `<span style="font-size:24px;font-weight:800;color:${p('#cbd5e1','#475569')}">—</span>`;
  } else if (key === 'weather_code') {
    const wmo = WMO[val] || ['❓','Inconnu','Unknown'];
    valHtml   = `<span style="font-size:38px;line-height:1">${wmo[0]}</span>`;
    extraHtml = `<div style="font-size:13px;font-weight:500;color:${xClr};margin-top:4px">
      ${esc(lang==='fr'?wmo[1]:wmo[2])}</div>
      <div style="font-size:11px;color:${p('#cbd5e1','#475569')}">code ${val}</div>`;
  } else if (key === 'is_day') {
    valHtml   = `<span style="font-size:34px">${val?'🌅':'🌙'}</span>`;
    extraHtml = `<div style="font-size:13px;font-weight:500;color:${xClr};margin-top:4px">
      ${esc(val?t('day'):t('night'))}</div>`;
  } else if (key === 'wind_direction_10m') {
    valHtml   = `<span style="font-size:24px;font-weight:800;color:${vClr}">
      ${windArrow(val)} ${Math.round(val)}<span style="font-size:14px;color:${uClr}">°</span></span>`;
    extraHtml = `<div style="font-size:12px;font-weight:600;color:${xClr};margin-top:4px">${esc(windDir(val))}</div>`;
  } else if (key === 'visibility') {
    const [v,u] = val>=1000?[(val/1000).toFixed(1),'km']:[Math.round(val),'m'];
    valHtml = `<span style="font-size:24px;font-weight:800;color:${vClr}">${v}
      <span style="font-size:14px;color:${uClr}"> ${u}</span></span>`;
  } else if (key === 'european_aqi') {
    const l = aqiEu(val);
    valHtml   = `<span style="font-size:24px;font-weight:800;color:${vClr}">${Math.round(val)}</span>`;
    extraHtml = `<div style="margin-top:6px"><span class="badge aqi-${l.i}">${esc(l.label)}</span></div>`;
  } else if (key === 'us_aqi') {
    const l = aqiUs(val);
    valHtml   = `<span style="font-size:24px;font-weight:800;color:${vClr}">${Math.round(val)}</span>`;
    extraHtml = `<div style="margin-top:6px"><span class="badge aqi-${l.i}">${esc(l.label)}</span></div>`;
  } else if (key.endsWith('_pollen')) {
    const l = pollenLvl(val||0);
    valHtml   = `<span style="font-size:24px;font-weight:800;color:${vClr}">${val!=null?val.toFixed(1):'—'}
      <span style="font-size:14px;color:${uClr}"> g/m³</span></span>`;
    extraHtml = `<div style="margin-top:6px"><span class="badge pol-${l.i}">${esc(l.label)}</span></div>`;
  } else {
    const v = typeof val==='number' ? (Number.isInteger(val)?val:val.toFixed(1)) : val;
    valHtml = `<span style="font-size:24px;font-weight:800;color:${vClr}">${v}
      ${unit?`<span style="font-size:14px;color:${uClr}"> ${esc(unit)}</span>`:''}
    </span>`;
  }

  return `<div class="metric-card">
    <span style="font-size:26px;display:block;margin-bottom:8px">${icon}</span>
    ${valHtml}
    <div style="font-size:12px;color:${p('#94a3b8','#9ca3af')};margin-top:5px">${esc(label)}</div>
    ${extraHtml}
  </div>`;
}

// ═══════════════════════════════════════
//  FORECAST CHARTS
// ═══════════════════════════════════════

const _charts = {};

function renderForecast(data) {
  const fc = data?.forecast?.hourly;
  const el = $('forecast-section');
  if (!fc || !el || typeof Chart === 'undefined') return;

  const now   = new Date();
  const times = fc.time || [];
  const temps = fc.temperature_2m || [];
  const precs = fc.precipitation   || [];

  const labels = [], tData = [], pData = [];
  for (let i = 0; i < times.length && labels.length < 25; i++) {
    if (new Date(times[i]) >= now) {
      const h = new Date(times[i]).getHours();
      labels.push(h + 'h');
      tData.push(temps[i]);
      pData.push(precs[i] ?? 0);
    }
  }
  if (!labels.length) return;
  el.style.display = '';

  const grid = p('#f1f5f9', '#334155');
  const tick = p('#94a3b8', '#64748b');

  _buildChart('chart-temp', 'line', labels, tData, {
    label:   t('chart_temp'),
    color:   '#ef4444',
    fill:    true,
    tension: 0.4,
  }, grid, tick);

  _buildChart('chart-precip', 'bar', labels, pData, {
    label: t('chart_precip'),
    color: '#3b82f6',
  }, grid, tick);
}

function _buildChart(id, type, labels, data, opts, gridColor, tickColor) {
  if (_charts[id]) { _charts[id].destroy(); delete _charts[id]; }
  const canvas = $(id);
  if (!canvas) return;
  _charts[id] = new Chart(canvas, {
    type,
    data: {
      labels,
      datasets: [{
        label:           opts.label,
        data,
        borderColor:     opts.color,
        backgroundColor: type === 'line' ? opts.color + '28' : opts.color + 'aa',
        borderWidth:     type === 'line' ? 2.5 : 1,
        fill:            opts.fill || false,
        tension:         opts.tension || 0,
        pointRadius:     type === 'line' ? 3 : 0,
        pointHoverRadius:5,
        borderRadius:    type === 'bar' ? 4 : 0,
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { labels: { color: tickColor, font: { size: 12, family: 'system-ui' } } },
        tooltip: { mode: 'index', intersect: false },
      },
      scales: {
        x: { grid: { color: gridColor }, ticks: { color: tickColor, font: { size: 11 }, maxTicksLimit: 13 } },
        y: { grid: { color: gridColor }, ticks: { color: tickColor, font: { size: 11 } } },
      },
    },
  });
}

// ═══════════════════════════════════════
//  WEBHOOK (Config page)
// ═══════════════════════════════════════

function initWebhook() {
  const input  = $('wh-url');
  const btn    = $('wh-test');
  const status = $('wh-status');
  if (!input || !btn) return;

  // Load saved URL from config
  if (config.webhook_url) input.value = config.webhook_url;

  // Auto-save URL on change
  const saveUrl = debounce(async () => {
    const url = input.value.trim();
    await pushConfig({ webhook_url: url }).catch(() => {});
  }, 600);
  input.addEventListener('input', saveUrl);

  // Test / send
  btn.addEventListener('click', async () => {
    const url = input.value.trim();
    if (!url) {
      status.innerHTML = `<span style="color:${p('#d97706','#f59e0b')}">${esc(t('lbl_wh_nourl'))}</span>`;
      return;
    }
    await pushConfig({ webhook_url: url }).catch(() => {});
    status.innerHTML = `<span style="color:${p('#6b7280','#94a3b8')}">⏳ …</span>`;
    btn.disabled = true;
    try {
      const r = await apiFetch('/api/webhook/trigger', { method: 'POST' });
      status.innerHTML = `<span style="color:${p('#059669','#34d399')}">${esc(t('lbl_wh_ok'))} (HTTP ${r.http_status})</span>`;
    } catch(e) {
      status.innerHTML = `<span style="color:${p('#dc2626','#f87171')}">${esc(t('lbl_wh_err'))} — ${esc(e.message)}</span>`;
    } finally { btn.disabled = false; }
    setTimeout(() => { if (status) status.innerHTML = ''; }, 4000);
  });
}

// ═══════════════════════════════════════
//  DOCS PAGE
// ═══════════════════════════════════════

function renderDocs() {
  const s = I18N[lang];
  const ENDPOINTS = [
    {m:'GET', p:'/api/config',            d:s.ep[0]},
    {m:'POST',p:'/api/config',            d:s.ep[1]},
    {m:'GET', p:'/api/search_city?q=...', d:s.ep[2]},
    {m:'GET', p:'/api/live_data',         d:s.ep[3]},
    {m:'GET',  p:'/api/weather_models',    d:s.ep[4]},
    {m:'POST', p:'/api/webhook/trigger',   d:s.ep[5]},
  ];
  setHtml('d-ep', ENDPOINTS.map(e=>`
    <div class="ep-row">
      <span class="method m-${e.m.toLowerCase()}">${e.m}</span>
      <span class="ep-path">${esc(e.p)}</span>
      <span class="ep-desc">— ${e.d}</span>
    </div>`).join(''));

  setHtml('d-rl', (s.d_rl||[]).map(line=>
    `<div style="display:flex;align-items:baseline;gap:8px;padding:6px 0;
      border-bottom:1px solid #fef3c7;font-size:13.5px">${line}</div>`).join(''));

  const ICONS = ['🌍','🌤️','🌿'];
  setHtml('d-src', (s.src||[]).map(([name,desc,url],i)=>`
    <div class="src-row">
      <span style="font-size:22px;flex-shrink:0">${ICONS[i]}</span>
      <div>
        <a href="${esc(url)}" target="_blank"
          style="font-weight:600;color:#2563eb;text-decoration:none;font-size:14px">${esc(name)}</a><br>
        <span style="font-size:12px;color:#64748b">${esc(desc)}</span>
      </div>
    </div>`).join(''));

  // Recommendations table
  const REC_GROUPS = lang === 'fr' ? [
    {icon:'🇫🇷', region:'France & DOM-TOM',               model:'Météo-France (ARPEGE + AROME)',    id:'meteofrance_seamless',  tip:'Modèle officiel français, excellent en Europe de l\'Ouest.'},
    {icon:'🇩🇪', region:'Allemagne, Belgique, Pays-Bas, Scandinavie, Europe centrale', model:'DWD ICON', id:'dwd_icon_seamless', tip:'Modèle officiel allemand, très précis sur l\'Europe centrale.'},
    {icon:'🇬🇧', region:'Royaume-Uni, Irlande',            model:'UK Met Office',                   id:'ukmo_seamless',         tip:'Modèle officiel britannique, idéal pour les îles britanniques.'},
    {icon:'🇺🇸', region:'États-Unis, Canada, Amériques',   model:'NOAA GFS',                        id:'gfs_seamless',          tip:'Modèle américain global, optimal pour le continent américain.'},
    {icon:'🌍', region:'Reste du monde / inconnu',          model:'Sélection automatique',           id:'best_match',            tip:'Open-Meteo choisit automatiquement le meilleur modèle disponible.'},
  ] : [
    {icon:'🇫🇷', region:'France & overseas territories',   model:'Météo-France (ARPEGE + AROME)',    id:'meteofrance_seamless',  tip:'Official French model, excellent over Western Europe.'},
    {icon:'🇩🇪', region:'Germany, Belgium, Netherlands, Scandinavia, Central Europe', model:'DWD ICON', id:'dwd_icon_seamless', tip:'Official German model, very accurate over Central Europe.'},
    {icon:'🇬🇧', region:'United Kingdom, Ireland',         model:'UK Met Office',                   id:'ukmo_seamless',         tip:'Official British model, ideal for the British Isles.'},
    {icon:'🇺🇸', region:'USA, Canada, Americas',           model:'NOAA GFS',                        id:'gfs_seamless',          tip:'US global model, optimal for the American continent.'},
    {icon:'🌍', region:'Rest of world / unknown',           model:'Automatic selection',             id:'best_match',            tip:'Open-Meteo automatically picks the best available model.'},
  ];
  setHtml('d-rec', REC_GROUPS.map(r=>`
    <div style="display:flex;align-items:center;gap:14px;padding:12px 0;
        border-bottom:1px solid ${p('#f1f5f9','#374151')};font-size:13px">
      <span style="font-size:24px;flex-shrink:0;width:32px;text-align:center;line-height:1">${r.icon}</span>
      <div style="flex:1;min-width:0">
        <div style="font-weight:600;color:${p('#1e293b','#f3f4f6')}">${esc(r.region)}</div>
        <div style="color:${p('#64748b','#9ca3af')};margin-top:2px;font-size:12px">${esc(r.tip)}</div>
      </div>
      <span style="flex-shrink:0;background:${p('#eff6ff','#1e3a5f')};color:${p('#1d4ed8','#93c5fd')};
          border-radius:9999px;padding:3px 12px;font-size:11px;font-weight:700;white-space:nowrap">${esc(r.model)}</span>
    </div>`).join(''));

  // Open-Meteo Terms of Service
  const tos = lang === 'fr' ? [
    '✅ <strong>API gratuite</strong> — Aucune clé API, aucune inscription, aucune carte bancaire requise.',
    '📊 <strong>Usage non-commercial gratuit</strong> — Jusqu\'à <strong>10 000 appels API par jour</strong>.',
    '📜 <strong>Licence CC BY 4.0</strong> — Attribution requise : citer <em>Open-Meteo.com</em> comme source.',
    '🏢 <strong>Usage commercial</strong> — Des plans payants sont disponibles avec des limites de débit augmentées et un support prioritaire.',
    '📡 <strong>Sources de données</strong> — Données issues des services météo nationaux (ECMWF, DWD, Météo-France, NOAA, Met Office…), toutes sous licence ouverte.',
  ] : [
    '✅ <strong>Free API</strong> — No API key, no sign-up, no credit card required.',
    '📊 <strong>Free non-commercial use</strong> — Up to <strong>10,000 daily API calls</strong>.',
    '📜 <strong>CC BY 4.0 licence</strong> — Attribution required: cite <em>Open-Meteo.com</em> as the source.',
    '🏢 <strong>Commercial use</strong> — Subscription plans available with higher rate limits and priority support.',
    '📡 <strong>Data sources</strong> — Data from national weather services (ECMWF, DWD, Météo-France, NOAA, Met Office…), all under open licences.',
  ];
  setHtml('d-tos', tos.map(line=>
    `<div style="display:flex;align-items:baseline;gap:8px;padding:6px 0;
      border-bottom:1px solid #d1fae5;font-size:13.5px">${line}</div>`).join(''));
}

// ═══════════════════════════════════════
//  PAGE INIT FUNCTIONS
// ═══════════════════════════════════════

function initDarkMode() {
  if (localStorage.getItem('meteo-dark') === '1') {
    document.documentElement.classList.add('dark');
  }
  updateDarkBtn();
  const btn = $('dark-toggle');
  if (!btn) return;
  btn.addEventListener('click', () => {
    document.documentElement.classList.toggle('dark');
    localStorage.setItem('meteo-dark', isDark() ? '1' : '0');
    updateDarkBtn();
    applyI18n();
    if ($('weather-grid')) renderCheckboxes();
    if ($('live-fmt') && window._liveData) renderFormatted(window._liveData);
    if (_charts && Object.keys(_charts).length) renderForecast(window._liveData);
    if ($('model-info') && _cachedModels) {
      const sel = $('model-sel');
      if (sel) updateModelInfo(_cachedModels[sel.value], $('model-info'));
    }
  });
}

function updateDarkBtn() {
  const btn = $('dark-toggle');
  if (!btn) return;
  btn.textContent = isDark() ? '☀️' : '🌙';
  btn.title = isDark() ? t('lbl_light') : t('lbl_dark');
}

function initCommon() {
  initDarkMode();
  initLangSwitcher();
  applyI18n();
}

function initConfigPage() {
  initCommon();
  renderCheckboxes();

  const bwAll  = $('w-all');  if (bwAll)  bwAll.addEventListener('click',  ()=>toggleAll('weather-grid',true));
  const bwNone = $('w-none'); if (bwNone) bwNone.addEventListener('click', ()=>toggleAll('weather-grid',false));
  const baAll  = $('a-all');  if (baAll)  baAll.addEventListener('click',  ()=>toggleAll('air-grid',true));
  const baNone = $('a-none'); if (baNone) baNone.addEventListener('click', ()=>toggleAll('air-grid',false));

  initCitySearch();

  renderModelSelector();
  initRateLimitInputs();
  initWebhook();

  loadConfig().then(() => {
    renderCheckboxes();
    renderModelSelector();
    syncRateLimitInputs();
    const whu = $('wh-url'); if (whu && config.webhook_url) whu.value = config.webhook_url;
    if (config.city) {
      $('city-input').value = config.city;
      showCityInfo(config.city, config.latitude, config.longitude);
    }
  }).catch(e => console.warn('[Config] API unreachable, using defaults:', e.message));
}

let _autoRefreshTimer = null;
let _countdownTimer   = null;
const AUTO_REFRESH_SEC = 60;

function startAutoRefresh() {
  clearInterval(_autoRefreshTimer);
  clearInterval(_countdownTimer);

  let remaining = AUTO_REFRESH_SEC;
  const cnt = $('auto-refresh-cnt');

  const tick = () => {
    remaining--;
    if (cnt) cnt.textContent = `· ↻ ${remaining}s`;
    if (remaining <= 0) {
      remaining = AUTO_REFRESH_SEC;
      refreshLive();
    }
  };
  if (cnt) cnt.textContent = `· ↻ ${remaining}s`;
  _countdownTimer = setInterval(tick, 1000);
}

function initLivePage() {
  initCommon();

  const vfmt = $('vfmt');
  const vraw = $('vraw');
  if (vfmt) vfmt.addEventListener('click', () => {
    liveView='fmt';
    vfmt.classList.add('active'); vraw.classList.remove('active');
    show('live-fmt'); hide('raw-json');
    if (window._liveData) show('forecast-section');
  });
  if (vraw) vraw.addEventListener('click', () => {
    liveView='raw';
    vraw.classList.add('active'); vfmt.classList.remove('active');
    hide('live-fmt'); hide('forecast-section'); show('raw-json');
    if (window._liveData) $('raw-json').textContent = JSON.stringify(window._liveData, null, 2);
  });

  const refBtn = $('refresh-btn');
  if (refBtn) refBtn.addEventListener('click', () => {
    startAutoRefresh();
    refreshLive();
  });

  refreshLive().then(() => startAutoRefresh());
}

function initDocsPage() {
  initCommon();
  renderDocs();
}
