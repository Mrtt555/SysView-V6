"""
SysView Bridge v6
═══════════════════════════════════════════════════════════════
FastAPI · SysViewHardware · Aether (proxy Open-Meteo)
═══════════════════════════════════════════════════════════════
  GET  /v1/health   → état du bridge
  GET  /v1/perf     → CPU / GPU / RAM / VRAM / Réseau / Disques
  GET  /v1/weather  → Météo + QAI + Pollen (via Aether)
  GET  /v1/media    → Lecture média (extension Chrome uniquement)
  GET  /v1/status   → Diagnostic complet
  POST /v1/config   → Paramètres depuis Wallpaper Engine
  POST /v1/media    → Mise à jour média (extension Chrome)
═══════════════════════════════════════════════════════════════
Prérequis :
  - Python 3.10+ installé depuis python.org
  - SysViewHardware.exe en Administrateur (port 8086)
      → inclus dans SysView V6/SysViewHardware/
      → setup.bat le compile et le lance automatiquement
  - Aether (setup.bat le télécharge automatiquement)
      → Interface de config : http://127.0.0.1:8001
═══════════════════════════════════════════════════════════════
"""

# ============================================================
# IMPORTS
# ============================================================

import sys
sys.dont_write_bytecode = True

import os
import subprocess
import time
import threading
import traceback
import concurrent.futures
import json
from pathlib import Path

from contextlib import asynccontextmanager
from datetime import datetime
from logging.handlers import RotatingFileHandler
import logging

import requests as _req
import psutil

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from fastapi.middleware.cors import CORSMiddleware

from slowapi import Limiter
from slowapi.util import get_remote_address
from slowapi.errors import RateLimitExceeded

import uvicorn
import config

# ============================================================
# LOGGING
# ============================================================

_LOG_DIR  = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
_LOG_PATH = os.path.join(_LOG_DIR, "sysview.log")
os.makedirs(_LOG_DIR, exist_ok=True)

# Repartir propre à chaque démarrage
with open(_LOG_PATH, "w", encoding="utf-8") as _f:
    _f.write(
        f"=== SysView Bridge v6 — "
        f"{datetime.now().strftime('%Y-%m-%d %H:%M:%S')} ===\n"
    )

_logger = logging.getLogger("sysview")
_logger.setLevel(logging.DEBUG)

_fh = RotatingFileHandler(
    _LOG_PATH,
    mode="a",
    maxBytes=10 * 1024 * 1024,   # 10 Mo
    backupCount=5,
    encoding="utf-8",
)
class _SysViewFormatter(logging.Formatter):
    """Remplace les noms de niveau Python par des abbreviations fixes (5 car)
    et les encadre de crochets :  [INFO] [WARN] [ERROR] [FATAL]"""
    _LEVELS = {
        "DEBUG":    "DEBUG",
        "INFO":     "INFO",
        "WARNING":  "WARN",
        "ERROR":    "ERROR",
        "CRITICAL": "FATAL",
    }
    def format(self, record):
        record = logging.makeLogRecord(record.__dict__)
        record.levelname = self._LEVELS.get(record.levelname, record.levelname[:5])
        return super().format(record)

_fh.setFormatter(
    _SysViewFormatter(
        "[%(asctime)s] [%(levelname)s] %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )
)
_logger.addHandler(_fh)

# Redirige les erreurs uvicorn (port deja occupe, etc.) vers notre fichier log
_uvi_err = logging.getLogger("uvicorn.error")
_uvi_err.addHandler(_fh)
_uvi_err.propagate = False


def log_ok(sec, msg):   _logger.info   (f"[{sec:<12}]  OK  {msg}")
def log_warn(sec, msg): _logger.warning(f"[{sec:<12}]     {msg}")
def log_err(sec, msg):  _logger.error  (f"[{sec:<12}]     {msg}")
def log_info(sec, msg): _logger.info   (f"[{sec:<12}]     {msg}")

# ============================================================
# PID FILE  (pour stop.bat)
# ============================================================

_PID_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bridge.pid")

# ============================================================
# LIFESPAN — ecrit le PID quand uvicorn est lie au port
# ============================================================

@asynccontextmanager
async def _lifespan(_app):
    """Ecrit bridge.pid apres la liaison au port, le supprime a l'arret."""
    try:
        with open(_PID_FILE, "w") as _pf:
            _pf.write(str(os.getpid()))
    except Exception as _e:
        log_warn("SERVER", f"PID non ecrit : {_e}")
    log_ok("SERVER", f"http://127.0.0.1:{config.API_PORT}")
    log_info("SERVER", f"Docs : http://localhost:{config.API_PORT}/docs")
    yield
    # --- arrêt ---
    _aether_stop()
    _disk_executor.shutdown(wait=False)
    try:
        os.remove(_PID_FILE)
    except Exception:
        pass
    log_info("SERVER", "Bridge arrêté")

# ============================================================
# FASTAPI
# ============================================================

app = FastAPI(
    title="SysView Bridge",
    version="6.0",
    docs_url="/docs",
    lifespan=_lifespan,
)

# CORS — WE renderer (null origin) + extension Chrome (chrome-extension://)
# Restriction aux origines locales : bloque les sites web tiers (ex. pages malveillantes)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["null", "http://127.0.0.1:5001", "http://localhost:5001"],
    allow_origin_regex=r"chrome-extension://.*",
    allow_credentials=False,
    allow_methods=["GET", "POST"],
    allow_headers=["Content-Type"],
)

# Access-Control-Allow-Private-Network — requis par Chromium (réseau local)
@app.middleware("http")
async def _private_network_header(request: Request, call_next):
    response = await call_next(request)
    response.headers["Access-Control-Allow-Private-Network"] = "true"
    return response


limiter = Limiter(key_func=get_remote_address)
app.state.limiter = limiter


@app.exception_handler(RateLimitExceeded)
async def _rate_limit_handler(request, exc):
    return JSONResponse(
        status_code=429,
        content={"error": "Too many requests", "retry_after": 60},
    )

# ============================================================
# ÉTAT PARTAGÉ
# ============================================================

# ── Hardware ──────────────────────────────────────────────────────────────────
PERF = {
    "cpu_name":    "CPU",
    "cpu_usage":   0.0,
    "cpu_temp":    None,   # None = LHM indisponible → affiche '—'
    "gpu_name":    "GPU",
    "gpu_usage":   0.0,
    "gpu_temp":    None,
    "vram_used":   0,
    "vram_total":  0,
    "ram_usage":   0.0,
    "ram_used_mb": 0,
    "ram_total_mb":0,
    "net_dl_kb":   0.0,
    "net_ul_kb":   0.0,
    "lhm_online":  False,
}

DISKS = {}    # {"c": {"used_gb":…, "total_gb":…, "percent":…, "display":"X/Y"}, …}

# ── Météo (enrichie via Aether) ───────────────────────────────────────────────
WEATHER = {
    "om_temp":           None,    # °C température
    "om_feels_like":     None,    # °C ressenti
    "om_humidity":       None,    # % humidité relative
    "om_uv":             None,    # indice UV
    "om_precip":         None,    # mm précipitations (heure courante)
    "om_precip_prob":    None,    # % probabilité de précipitations
    "om_wind":           None,    # km/h vitesse vent
    "om_wind_dir":       None,    # ° direction vent (0=N, 90=E …)
    "om_weather_code":   None,    # WMO weather code
    "om_aqi":            None,    # European AQI (0–500)
    "om_aqi_label":      None,
    "om_pollen":         None,    # grain/m³ cumul (graminées + bouleau + aulne + ambroisie)
    "om_pollen_label":   None,
    "om_pm10":           None,    # µg/m³ PM10
    "om_pm25":           None,    # µg/m³ PM2.5
    "aether_model":      None,    # modèle météo actif (ex. "Météo-France AROME")
}

# ── Média ─────────────────────────────────────────────────────────────────────
MEDIA = {
    "title":       "",
    "artist":      "",
    "source":      "",       # "extension" (Chrome)
    "playing":     False,
    "position":    0.0,
    "duration":    0.0,
    "thumb_url":   "",
    "last_update": 0.0,
}

# ── Config runtime (reçu depuis Wallpaper Engine via POST /v1/config) ─────────
RUNTIME = {
    "lat":                  50.73,
    "lon":                  3.13,
    "city":                 "HALLUIN",      # ville courante (géocodée via Aether)
    "lhm_enabled":          True,
    "weather_interval_min": 10,
    "network_iface":        "auto",         # "auto" | "eth" | "wifi"
}

_weather_event = threading.Event()   # déclenche un refresh météo immédiat

RUNTIME_FILE = Path(__file__).parent / "runtime_config.json"

# ── Verrous ───────────────────────────────────────────────────────────────────
perf_lock    = threading.Lock()
weather_lock = threading.Lock()
media_lock   = threading.Lock()
runtime_lock = threading.Lock()

# ============================================================
# AETHER — sous-processus proxy Open-Meteo multi-modèles
# ============================================================

_aether_proc: subprocess.Popen | None = None

def _aether_start() -> None:
    """Démarre Aether en sous-processus silencieux sur le port 8001."""
    global _aether_proc
    aether_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Aether")
    if not os.path.exists(os.path.join(aether_dir, "main.py")):
        log_warn("AETHER", f"Non trouvé dans {aether_dir} — lancez install.bat")
        return
    try:
        _aether_proc = subprocess.Popen(
            [sys.executable, "-m", "uvicorn", "main:app",
             "--host", "127.0.0.1", "--port", "8001", "--log-level", "error"],
            cwd=aether_dir,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        log_ok("AETHER", f"Démarré (PID {_aether_proc.pid}) — http://127.0.0.1:8001")
    except Exception as exc:
        log_warn("AETHER", f"Impossible de démarrer : {exc}")

def _aether_stop() -> None:
    """Arrête proprement le sous-processus Aether."""
    global _aether_proc
    if _aether_proc and _aether_proc.poll() is None:
        _aether_proc.terminate()
        try:
            _aether_proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            _aether_proc.kill()
    _aether_proc = None

def _aether_geocode(query: str) -> tuple[float, float, str] | None:
    """Géocode une ville via Open-Meteo Geocoding API (direct, sans passer par Aether).
    Retourne (lat, lon, city_name) ou None si aucun résultat / erreur."""
    try:
        r = _req.get(
            config.GEOCODING_URL,
            params={"name": query, "count": 1, "language": "fr", "format": "json"},
            timeout=8
        )
        if not r.ok:
            log_warn("AETHER", f"Géocodage HTTP {r.status_code} pour {query!r}")
            return None
        results = r.json().get("results", [])
        if not results:
            log_warn("AETHER", f"Aucun résultat pour {query!r}")
            return None
        first   = results[0]
        lat     = float(first["latitude"])
        lon     = float(first["longitude"])
        name    = first.get("name", query)
        country = first.get("country", "")
        log_ok("AETHER", f"Géocodage {query!r} → {name}, {country} ({lat:.4f}, {lon:.4f})")
        return lat, lon, name
    except Exception as exc:
        log_warn("AETHER", f"Géocodage erreur pour {query!r} : {exc}")
        return None


def _aether_configure(lat: float, lon: float, city: str | None = None) -> None:
    """Pousse lat/lon + nom de ville + paramètres Open-Meteo vers Aether.
    Le champ city met à jour l'affichage du panel Aether (config.json côté Aether)."""
    payload = {
        "latitude":  lat,
        "longitude": lon,
        # Météo courante : température, ressenti, humidité, précipitations réelles (mm),
        # code météo, couverture nuageuse, vent, direction, UV
        "weather_params": [
            "temperature_2m", "apparent_temperature", "relative_humidity_2m",
            "precipitation", "weather_code", "cloud_cover",
            "wind_speed_10m", "wind_direction_10m", "uv_index"
        ],
        # Qualité de l'air : AQI européen, particules fines, 4 types de pollens
        "air_quality_params": [
            "european_aqi", "pm10", "pm2_5",
            "grass_pollen", "birch_pollen", "alder_pollen", "ragweed_pollen"
        ],
    }
    if city:
        payload["city"] = city
    try:
        r = _req.post(f"{config.AETHER_URL}/api/config", json=payload, timeout=5)
        if r.ok:
            city_str = f" ({city})" if city else ""
            log_ok("AETHER", f"Configuré → lat={lat} lon={lon}{city_str}")
        else:
            log_warn("AETHER", f"Config — HTTP {r.status_code}")
    except Exception as exc:
        log_warn("AETHER", f"Config inaccessible : {exc}")

# ============================================================
# RUNTIME PERSISTENCE
# ============================================================

def _save_runtime() -> None:
    """Persiste les paramètres runtime sur disque pour survie au redémarrage."""
    try:
        with runtime_lock:
            data = {
                "weather_interval_min": RUNTIME["weather_interval_min"],
                "network_iface":        RUNTIME["network_iface"],
                "city":                 RUNTIME.get("city", ""),
                "lat":                  RUNTIME["lat"],
                "lon":                  RUNTIME["lon"],
            }
        with open(RUNTIME_FILE, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
    except Exception as e:
        log_warn("CONFIG", f"runtime_config.json non sauvegardé : {e}")


def _load_runtime() -> None:
    """Recharge les paramètres runtime sauvegardés (survie au redémarrage du bridge)."""
    try:
        if not RUNTIME_FILE.exists():
            return
        with open(RUNTIME_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
        with runtime_lock:
            if "weather_interval_min" in data:
                RUNTIME["weather_interval_min"] = max(1, min(15, int(data["weather_interval_min"])))
            if "network_iface" in data and data["network_iface"] in ("auto", "eth", "wifi"):
                RUNTIME["network_iface"] = data["network_iface"]
            if "city" in data and data["city"]:
                RUNTIME["city"] = str(data["city"])
            if "lat" in data:
                RUNTIME["lat"] = float(data["lat"])
            if "lon" in data:
                RUNTIME["lon"] = float(data["lon"])
        log_info(
            "INIT",
            f"Config runtime chargée — intervalle={RUNTIME['weather_interval_min']}min"
            f"  ville={RUNTIME.get('city', '?')}",
        )
    except Exception as exc:
        log_warn("INIT", f"runtime_config.json illisible : {exc}")


# ============================================================
# SYSVIEWHARDWARE — capteurs via LibreHardwareMonitorLib
# ============================================================
# SysViewHardware.exe expose un JSON plat sur port 8086.
# Les clés correspondent exactement à celles attendues par
# hardware_loop() — aucune transformation nécessaire.
# ============================================================

_LHM_CACHE   = {}
_LHM_CACHE_T = 0.0
_LHM_ONLINE  = False
_lhm_lock    = threading.Lock()   # protège _LHM_CACHE / _LHM_CACHE_T (TOCTOU)


def _lhm_fetch() -> dict:
    """Requête HTTP vers SysViewHardware. Logge les transitions on/off."""
    global _LHM_ONLINE
    try:
        r = _req.get(config.LHM_URL, timeout=3)
        if r.status_code == 200:
            try:
                data = r.json()
            except Exception:
                if _LHM_ONLINE:
                    _LHM_ONLINE = False
                    log_warn("LHM", "SysViewHardware — réponse JSON invalide (corrompue ?)")
                return {}
            if not _LHM_ONLINE:
                _LHM_ONLINE = True
                log_ok("LHM", "SysViewHardware — OK")
            return data
        else:
            if _LHM_ONLINE:
                _LHM_ONLINE = False
                log_warn("LHM", f"SysViewHardware HTTP {r.status_code} — service en erreur ?")
            return {}
    except Exception:
        if _LHM_ONLINE:
            _LHM_ONLINE = False
            log_warn(
                "LHM",
                "SysViewHardware inaccessible — vérifier que "
                "SysViewHardware.exe tourne en Administrateur (port 8086).",
            )
    return {}


def get_lhm() -> dict:
    """Retourne les données capteurs. Cache 4 s (> timeout HTTP 3 s).

    Thread-safe : le verrou _lhm_lock garantit qu'un seul thread déclenche
    le fetch HTTP — les autres voient le timer avancé et retournent le cache.
    """
    global _LHM_CACHE, _LHM_CACHE_T
    now = time.monotonic()
    with _lhm_lock:
        if now - _LHM_CACHE_T < 4.0:
            return _LHM_CACHE
        # Avancer le timer AVANT de libérer le lock : tout thread concurrent
        # lira un timer "récent" et retournera le cache sans déclencher
        # un deuxième fetch HTTP simultané.
        _LHM_CACHE_T = now
    data = _lhm_fetch()   # fetch sans lock (I/O, peut durer jusqu'à 3 s)
    if data:
        with _lhm_lock:
            _LHM_CACHE = data
    with _lhm_lock:
        return _LHM_CACHE

# ============================================================
# THREAD HARDWARE
# ============================================================

_net_prev      = None
_net_prev_time = 0.0


def hardware_loop():
    """Pole LHM toutes les 500ms. Fallback psutil si LHM indisponible."""
    global _net_prev, _net_prev_time
    log_info("PERF", "Thread hardware démarré")
    _perf_count = 0

    while True:
        try:
            with runtime_lock:
                lhm_enabled = RUNTIME.get("lhm_enabled", True)
                net_iface   = RUNTIME.get("network_iface", "auto")

            lhm = get_lhm() if lhm_enabled else {}
            mem = psutil.virtual_memory()

            # ── Réseau (psutil, priorité LHM si dispo) ──────────────────────
            net = psutil.net_io_counters()
            now = time.monotonic()
            dl_kb = ul_kb = 0.0
            if _net_prev and (now - _net_prev_time) > 0:
                dt    = now - _net_prev_time
                dl_kb = max(0.0, (net.bytes_recv - _net_prev.bytes_recv) / dt / 1e3)
                ul_kb = max(0.0, (net.bytes_sent - _net_prev.bytes_sent) / dt / 1e3)
            _net_prev, _net_prev_time = net, now

            # Sélection de l'interface réseau selon la config WE
            # auto → LHM WiFi (513/514) + Ethernet (468/469) ; eth / wifi → LHM seul
            if net_iface == "wifi":
                if lhm.get("net_dl_kb") is not None and lhm["net_dl_kb"] >= 0:
                    dl_kb = lhm["net_dl_kb"]
                if lhm.get("net_ul_kb") is not None and lhm["net_ul_kb"] >= 0:
                    ul_kb = lhm["net_ul_kb"]
            elif net_iface == "eth":
                if lhm.get("net_eth_dl_kb") is not None and lhm["net_eth_dl_kb"] >= 0:
                    dl_kb = lhm["net_eth_dl_kb"]
                if lhm.get("net_eth_ul_kb") is not None and lhm["net_eth_ul_kb"] >= 0:
                    ul_kb = lhm["net_eth_ul_kb"]
            else:  # auto : somme LHM WiFi + Ethernet (fallback psutil si LHM absent)
                w_dl = lhm.get("net_dl_kb");     w_ul = lhm.get("net_ul_kb")
                e_dl = lhm.get("net_eth_dl_kb"); e_ul = lhm.get("net_eth_ul_kb")
                if w_dl is not None or e_dl is not None:
                    dl_kb = (w_dl or 0.0) + (e_dl or 0.0)
                if w_ul is not None or e_ul is not None:
                    ul_kb = (w_ul or 0.0) + (e_ul or 0.0)
                # si aucun sensor LHM dispo → dl_kb/ul_kb psutil inchangés

            # ── CPU : LHM si dispo, sinon psutil (fallback animation) ────────
            cpu_usage = lhm.get("cpu_usage")
            if cpu_usage is None:
                cpu_usage = psutil.cpu_percent(interval=None)
                # Logguer périodiquement si LHM est online mais cpu_usage absent
                # (sensor ID manquant pour ce hardware — ID mismatch partiel).
                # Toutes les ~5 min, aligné sur le heartbeat PERF.
                if lhm_enabled and _LHM_ONLINE and _perf_count > 0 and _perf_count % 600 == 0:
                    log_warn(
                        "PERF",
                        "cpu_usage absent de SysViewHardware — fallback psutil actif.",
                    )

            with perf_lock:
                # Utiliser `x if x is not None else fallback` — l'opérateur `or`
                # traite 0 / 0.0 comme falsy et remplacerait des valeurs valides.
                PERF.update({
                    "cpu_name":   lhm["cpu_name"] if lhm.get("cpu_name") is not None else PERF["cpu_name"],
                    "cpu_usage":  round(float(cpu_usage if cpu_usage is not None else 0), 1),
                    "cpu_temp":   lhm.get("cpu_temp"),
                    "gpu_name":   lhm["gpu_name"] if lhm.get("gpu_name") is not None else PERF["gpu_name"],
                    "gpu_usage":  round(float(v), 1) if (v := lhm.get("gpu_usage"))  is not None else 0.0,
                    "gpu_temp":   lhm.get("gpu_temp"),
                    "vram_used":  int(v) if (v := lhm.get("vram_used"))  is not None else 0,
                    "vram_total": int(v) if (v := lhm.get("vram_total")) is not None else 0,
                    "ram_usage":  round(v, 1)    if (v := lhm.get("ram_usage"))    is not None else round(mem.percent, 1),
                    # LHM en primaire (SysViewHardware Data sensors), psutil en fallback
                    "ram_used_mb":  v if (v := lhm.get("ram_used_mb"))  is not None else (mem.used  // (1024 * 1024)),
                    "ram_total_mb": v if (v := lhm.get("ram_total_mb")) is not None else (mem.total // (1024 * 1024)),
                    "net_dl_kb":  round(dl_kb, 1),
                    "net_ul_kb":  round(ul_kb, 1),
                    "lhm_online": _LHM_ONLINE,
                })

            _perf_count += 1
            if _perf_count > 0 and _perf_count % 600 == 0:   # log toutes les ~5 min
                with perf_lock:
                    log_info(
                        "PERF",
                        f"Cycle {_perf_count} — "
                        f"CPU: {PERF['cpu_usage']}% "
                        f"GPU: {PERF['gpu_usage']}% "
                        f"RAM: {PERF['ram_usage']}%",
                    )

        except Exception as e:
            log_err("PERF", traceback.format_exc().strip())

        time.sleep(0.5)

# ============================================================
# THREAD DISQUES
# ============================================================

_disk_executor = concurrent.futures.ThreadPoolExecutor(max_workers=1, thread_name_prefix="disk_parts")

def _safe_disk_partitions(timeout_s: float = 5.0):
    """Appelle disk_partitions() avec timeout pour éviter le blocage sur lecteurs réseau/USB gelés."""
    try:
        fut = _disk_executor.submit(psutil.disk_partitions, False)
        return fut.result(timeout=timeout_s)
    except concurrent.futures.TimeoutError:
        log_warn("DISK", f"disk_partitions() timeout ({timeout_s}s) — lecteur réseau/USB gelé ?")
        return []
    except Exception as e:
        log_warn("DISK", f"disk_partitions() erreur : {e}")
        return []


def _disk_from_lhm(entry: dict) -> dict:
    """Construit une entrée DISKS depuis les données SysViewHardware (valeurs en GiB).
    Adapte l'unité à To si le volume dépasse 1024 GiB."""
    used_g = round(float(entry.get("used_gb",  0.0)), 2)
    tot_g  = round(float(entry.get("total_gb", 0.0)), 2)
    free_g = round(float(entry.get("free_gb",  0.0)), 2)
    pct    = round(float(entry.get("percent",  0.0)), 1)
    # Conversion To si volume > 1024 GiB (SysViewHardware rapporte en GiB)
    used_u = "To" if used_g >= 1024.0 else "Go"
    tot_u  = "To" if tot_g  >= 1024.0 else "Go"
    free_u = "To" if free_g >= 1024.0 else "Go"
    used_d = round(used_g / 1024.0, 2) if used_u == "To" else used_g
    tot_d  = round(tot_g  / 1024.0, 2) if tot_u  == "To" else tot_g
    return {
        "used_gb":    used_g,
        "total_gb":   tot_g,
        "free_gb":    free_g,
        "used_unit":  used_u,
        "total_unit": tot_u,
        "free_unit":  free_u,
        "percent":    pct,
        "display":    f"{used_d:.2f}{used_u}/{tot_d:.0f}{tot_u}",
    }


def disk_loop():
    """Met à jour les données disques toutes les 10s.
    SysViewHardware (DriveInfo) en primaire — psutil en fallback."""
    GiB = 1_073_741_824
    TiB = 1_099_511_627_776
    log_info("DISK", "Thread disques démarré")
    _prev = {}

    while True:
        try:
            # ── Primaire : SysViewHardware (DriveInfo) ────────────────────────
            lhm_disks = get_lhm().get("disks")
            if lhm_disks and isinstance(lhm_disks, dict):
                for letter, entry in lhm_disks.items():
                    info    = _disk_from_lhm(entry)
                    cur_pct = info["percent"]
                    with perf_lock:
                        DISKS[letter] = info
                    if _prev.get(letter) != cur_pct:
                        _prev[letter] = cur_pct
                        log_ok("DISK", f"[HW] {letter.upper()}: {info['display']} ({cur_pct}%)")
            else:
                # ── Fallback : psutil ─────────────────────────────────────────
                for part in _safe_disk_partitions():
                    mp = part.mountpoint      # ex : "C:\\"
                    if len(mp) < 2 or mp[1] != ":":
                        continue
                    letter = mp[0].lower()
                    try:
                        try:
                            u = _disk_executor.submit(psutil.disk_usage, mp).result(timeout=3.0)
                        except concurrent.futures.TimeoutError:
                            log_warn("DISK", f"disk_usage({mp!r}) timeout — lecteur gelé, ignoré")
                            continue
                        used_v = u.used  / TiB if u.used  >= TiB else u.used  / GiB
                        tot_v  = u.total / TiB if u.total >= TiB else u.total / GiB
                        free_v = u.free  / TiB if u.free  >= TiB else u.free  / GiB
                        used_u = "To" if u.used  >= TiB else "Go"
                        tot_u  = "To" if u.total >= TiB else "Go"
                        free_u = "To" if u.free  >= TiB else "Go"
                        display = f"{used_v:.2f}{used_u}/{tot_v:.0f}{tot_u}"
                        info = {
                            "used_gb":   round(used_v, 2),
                            "total_gb":  round(tot_v,  2),
                            "free_gb":   round(free_v, 2),
                            "used_unit": used_u,
                            "total_unit":tot_u,
                            "free_unit": free_u,
                            "percent":   round(u.percent, 1),
                            "display":   display,
                        }
                        with perf_lock:
                            DISKS[letter] = info
                        cur_pct = round(u.percent, 1)
                        if _prev.get(letter) != cur_pct:
                            _prev[letter] = cur_pct
                            log_ok("DISK", f"{letter.upper()}: {display} ({cur_pct}%)")
                    except Exception as e:
                        log_warn("DISK", f"disk_usage({mp!r}) erreur : {e}")
        except Exception as e:
            log_err("DISK", traceback.format_exc().strip())

        time.sleep(10)

# ============================================================
# THREAD MÉTÉO
# ============================================================

def weather_loop():
    """Météo via Aether (proxy Open-Meteo multi-modèles). Intervalle configurable."""
    log_info("WEATHER", "Thread météo démarré")
    time.sleep(5)    # laisser Aether démarrer et se lier au port 8001

    _fail        = 0
    _configured  = False   # True dès que _aether_configure() a été appelé une première fois

    while True:
      try:
        # clear() en tête de boucle : si set() arrive pendant le fetch HTTP,
        # il sera respecté à la prochaine itération plutôt qu'effacé après le wait.
        _weather_event.clear()

        with runtime_lock:
            lat  = RUNTIME["lat"]
            lon  = RUNTIME["lon"]
            city = RUNTIME.get("city", "")

        # ── Configuration initiale d'Aether (une seule fois au démarrage) ─────
        if not _configured:
            _aether_configure(lat, lon, city=city or None)
            _configured = True

        # ── Fetch Aether /api/live_data ───────────────────────────────────────
        ok = False
        try:
            r    = _req.get(f"{config.AETHER_URL}/api/live_data", timeout=15)
            r.raise_for_status()   # lève HTTPError si 4xx/5xx avant de parser
            data = r.json()

            cu = data.get("weather",     {}).get("current", {})
            aq = data.get("air_quality", {}).get("current", {})

            # ── Météo courante ────────────────────────────────────────────────
            temp   = cu.get("temperature_2m")
            precip      = cu.get("precipitation")
            precip_prob = cu.get("precipitation_probability")   # None avec AROME
            wind        = cu.get("wind_speed_10m")
            code        = cu.get("weather_code")

            # ── Probabilité de précipitations (fallback direct Open-Meteo) ────
            # AROME ne fournit pas precipitation_probability en current → appel
            # léger sans modèle forcé (best_match supporte cette variable).
            if precip_prob is None:
                try:
                    rp = _req.get(
                        config.OPEN_METEO_URL,
                        params={
                            "latitude":  lat,
                            "longitude": lon,
                            "current":   "precipitation_probability",
                            "timezone":  "auto",
                            "format":    "json",
                        },
                        timeout=5,
                    )
                    if rp.ok:
                        precip_prob = rp.json().get("current", {}).get("precipitation_probability")
                except Exception as _exc:
                    log_warn("WEATHER", f"prob_pluie fallback erreur : {_exc}")

            # ── Qualité de l'air ──────────────────────────────────────────────
            aqi     = aq.get("european_aqi")
            grass   = aq.get("grass_pollen")
            birch   = aq.get("birch_pollen")
            alder   = aq.get("alder_pollen")
            ragweed = aq.get("ragweed_pollen")

            # Cumul pollens : None si toutes les sources sont absentes (hors saison)
            if grass is None and birch is None and alder is None and ragweed is None:
                pollen = None
            else:
                pollen = round((grass or 0) + (birch or 0) + (alder or 0) + (ragweed or 0), 1)

            # Seuils QAI European : 0-20 Bon, 21-40 Correct, 41-60 Modéré,
            # 61-80 Mauvais, 81+ Très mauvais (bornes incluses).
            aqi_label = (
                "—"            if aqi is None else
                "Bon"          if aqi <= 20  else
                "Correct"      if aqi <= 40  else
                "Modere"       if aqi <= 60  else
                "Mauvais"      if aqi <= 80  else
                "Tres mauvais"
            )
            pollen_label = (
                "—"          if pollen is None else
                "Nul"        if pollen == 0    else
                "Faible"     if pollen < 20    else
                "Modere"     if pollen < 75    else
                "Eleve"      if pollen < 150   else
                "Tres eleve"
            )

            # Nom du modèle météo actif (ex. "Météo-France AROME")
            model = data.get("sources", {}).get("weather", {}).get("name_en", "")

            with weather_lock:
                WEATHER["om_temp"]         = temp
                WEATHER["om_feels_like"]   = cu.get("apparent_temperature")
                WEATHER["om_humidity"]     = cu.get("relative_humidity_2m")
                WEATHER["om_uv"]           = cu.get("uv_index")
                # Garder None si absent — le frontend distingue "0 mm" de "donnée indisponible"
                WEATHER["om_precip"]       = round(float(precip), 1) if precip is not None else None
                WEATHER["om_precip_prob"]  = int(precip_prob) if precip_prob is not None else None
                WEATHER["om_wind"]         = round(float(wind),   1) if wind   is not None else None
                WEATHER["om_wind_dir"]     = cu.get("wind_direction_10m")
                WEATHER["om_weather_code"] = code
                WEATHER["om_aqi"]          = aqi
                WEATHER["om_aqi_label"]    = aqi_label
                WEATHER["om_pollen"]       = pollen
                WEATHER["om_pollen_label"] = pollen_label
                WEATHER["om_pm10"]         = aq.get("pm10")
                WEATHER["om_pm25"]         = aq.get("pm2_5")
                WEATHER["aether_model"]    = model
                _lv = (temp, code, WEATHER["om_wind"])

            log_ok("WEATHER",
                f"[Aether/{model or 'auto'}] "
                f"temp={_lv[0]}°C  code={_lv[1]}  vent={_lv[2]}km/h  prob_pluie={precip_prob}%")
            log_ok("WEATHER",
                f"[Aether] QAI={aqi} ({aqi_label})  pollen={pollen} ({pollen_label})")
            ok    = True
            _fail = 0

        except Exception as exc:
            _fail += 1
            log_warn("WEATHER", f"Aether erreur ({_fail}) : {exc}")

        with runtime_lock:
            interval_s = max(60, int(RUNTIME.get("weather_interval_min") or 10) * 60)
        delay = interval_s if ok else min(30 * (2 ** min(_fail - 1, 4)), interval_s)
        if ok:
            log_info("WEATHER", f"Prochain refresh dans {delay // 60}min")
        else:
            log_info("WEATHER", f"Prochain retry dans {delay}s")
        _weather_event.wait(timeout=delay)

      except Exception:
          # Guard global : protège les sections hors try/except (clear, runtime_lock,
          # calcul du délai...). Sans ce catch, une exception inattendue tuerait le
          # thread silencieusement (pas de console avec .pyw).
          log_err("WEATHER", traceback.format_exc().strip())
          time.sleep(30)

# ============================================================
# MEDIA — extension Chrome uniquement
# ============================================================

_ext_last_post = 0.0    # timestamp du dernier POST /v1/media (extension Chrome)

# ============================================================
# ENDPOINTS
# ============================================================

@app.get("/v1/health")
@limiter.limit("350/minute")
async def health(request: Request):
    return {"status": "online", "version": "6.0"}


@app.get("/v1/perf")
@limiter.limit("350/minute")
async def perf(request: Request):
    with perf_lock:
        p = PERF.copy()
        d = DISKS.copy()
    return {
        "lhm_online": p["lhm_online"],
        "cpu": {
            "name":  p["cpu_name"],
            "usage": p["cpu_usage"],   # %
            "temp":  p["cpu_temp"],    # °C ou null
        },
        "gpu": {
            "name":  p["gpu_name"],
            "usage": p["gpu_usage"],
            "temp":  p["gpu_temp"],
        },
        "ram": {
            "usage":    p["ram_usage"],    # %
            "used_mb":  p["ram_used_mb"],
            "total_mb": p["ram_total_mb"],
        },
        "vram": {
            "used_mb":  p["vram_used"],
            "total_mb": p["vram_total"],
        },
        "network": {
            "download_kb": p["net_dl_kb"],
            "upload_kb":   p["net_ul_kb"],
        },
        "disks": d,
    }


@app.get("/v1/weather")
@limiter.limit("350/minute")
async def weather(request: Request):
    with weather_lock:
        payload = dict(WEATHER)
    return payload


@app.get("/v1/media")
@limiter.limit("350/minute")
async def media_get(request: Request):
    with media_lock:
        m = dict(MEDIA)
    # Live position: interpolate elapsed time since last extension snapshot
    # Plafonner elapsed à 30s : si le bridge redémarre ou que l'extension
    # n'a pas posté depuis longtemps, l'interpolation ne déborde pas.
    if m.get("playing") and m.get("last_update") and m.get("position") is not None:
        elapsed  = min(time.time() - m["last_update"], 30.0)
        duration = m.get("duration") or 0.0
        pos      = m["position"] + elapsed
        m["position"] = pos if duration <= 0 else min(pos, duration)
    return m


@app.get("/v1/status")
@limiter.limit("60/minute")
async def status(request: Request):
    with perf_lock:
        p = PERF.copy()
    with weather_lock:
        w = WEATHER.copy()
    with media_lock:
        m = MEDIA.copy()
    now = time.time()

    def _fmt_uptime(s: float) -> str:
        s = int(s)
        h, rem = divmod(s, 3600)
        mn, sec = divmod(rem, 60)
        if h:  return f"{h}h {mn:02d}m"
        if mn: return f"{mn}m {sec:02d}s"
        return f"{sec}s"

    with media_lock:
        _ext_last = _ext_last_post
    ext_age = round(now - _ext_last) if _ext_last > 0 else None

    media_state = (
        "playing" if m.get("title") and m.get("playing") else
        "paused"  if m.get("title") else
        "idle"
    )

    return {
        "name":   "SysView Bridge v6",
        "uptime": _fmt_uptime(now - _START_TIME),
        "port":   config.API_PORT,
        "modules": {
            "psutil":  "ok",
            "lhm":     "ok"      if p["lhm_online"]                else "offline",
            "aether":  "ok"      if w.get("om_temp") is not None   else "pending",
            "model":   w.get("aether_model") or "—",
        },
        "endpoints": {
            "health":       "ok",
            "perf":         "ok",
            "weather":      "ok"  if w.get("om_temp") is not None  else "pending",
            "aether_ui":    f"{config.AETHER_URL}",
            "media":        media_state,
        },
        "extension": {
            "active":      ext_age is not None and ext_age < 10,
            "last_seen_s": ext_age,
        },
    }


# ── POST /v1/media — mise à jour depuis extension Chrome ─────────────────────
@app.post("/v1/media")
@limiter.limit("350/minute")
async def media_post(request: Request):
    global _ext_last_post
    try:
        d         = await request.json()
        new_title = d.get("title", "")
        now       = time.time()
        with media_lock:
            old_title = MEDIA.get("title", "")
            # Priorité source : n'accepter un NOUVEAU titre que si playing=True
            # OU si aucun titre n'est actuellement affiché (état idle).
            # Empêche qu'une source en pause (ex. YouTube visible après fermeture
            # de Jellyfin) ne s'impose sur un titre déjà en cours — mais laisse
            # passer un premier titre même en pause (page chargée, lecture imminent).
            _ext_last_post = now   # marquer l'extension active dès réception, même si le
                                   # POST est ignoré (priorité source) — sinon le statut
                                   # /v1/status affiche "inactive" alors que l'ext tourne
            if new_title and new_title != old_title and old_title and MEDIA.get("playing", False) and not d.get("playing", False):
                log_info(
                    "MEDIA",
                    f"POST ignoré (priorité source) : {new_title!r} écarté"
                    f" — {old_title!r} en cours",
                )
                return JSONResponse(status_code=200, content={"ok": True})
            MEDIA.update({
                "title":       new_title,
                "artist":      d.get("artist",    ""),
                "source":      "extension",
                "playing":     d.get("playing",   False),
                "position":    d.get("position",  0.0),
                "duration":    d.get("duration",  0.0),
                "thumb_url":   d.get("thumb_url", ""),
                "last_update": now,
            })
        if new_title and new_title != old_title:
            log_ok(
                "MEDIA",
                f'"{new_title}" — {d.get("artist","?")} '
                f'({d.get("source","extension")})',
            )
        elif not new_title and old_title:
            log_info("MEDIA", "Lecture arretee")
        return JSONResponse(status_code=200, content={"ok": True})
    except Exception as e:
        log_err("MEDIA", str(e))
        return JSONResponse(status_code=400, content={"error": str(e)})


# ── POST /v1/config — paramètres depuis Wallpaper Engine ─────────────────────
@app.post("/v1/config")
@limiter.limit("60/minute")
async def set_config(request: Request):
    """Reçoit city, lhm_enabled, weather_interval_min depuis Wallpaper Engine."""
    try:
        d           = await request.json()
        changed     = []
        geo_changed = False

        # ── Géocodage ville → lat/lon via Aether ─────────────────────────────
        # Lancé en arrière-plan pour ne pas bloquer la réponse HTTP.
        # _geocode_and_update met à jour RUNTIME["lat/lon"] puis déclenche
        # _aether_configure + _weather_event.set().
        if "city" in d and d["city"]:
            city = str(d["city"]).strip()
            if city:
                changed.append("city")
                with runtime_lock:
                    RUNTIME["city"] = city
                def _geocode_and_update(_city=city):
                    result = _aether_geocode(_city)
                    if result:
                        lat_new, lon_new, geo_name = result
                        with runtime_lock:
                            RUNTIME["lat"] = lat_new
                            RUNTIME["lon"] = lon_new
                        _aether_configure(lat_new, lon_new, city=geo_name)
                        _save_runtime()   # persiste les nouvelles coords avant le refresh météo
                        _weather_event.set()
                threading.Thread(
                    target=_geocode_and_update,
                    daemon=True,
                    name="geocode",
                ).start()

        # ── Autres paramètres (intervalle, interface réseau, LHM) ─────────────
        with runtime_lock:
            if "lhm_enabled" in d:
                new_val = bool(d["lhm_enabled"])
                if new_val != RUNTIME["lhm_enabled"]:
                    changed.append("lhm_enabled")
                RUNTIME["lhm_enabled"] = new_val
            if "weather_interval_min" in d:
                try:
                    new_val = max(1, min(15, int(d["weather_interval_min"])))
                    if new_val != RUNTIME["weather_interval_min"]:
                        changed.append("weather_interval_min")
                        RUNTIME["weather_interval_min"] = new_val
                        _weather_event.set()   # réveille le thread pour appliquer le nouvel intervalle
                    else:
                        RUNTIME["weather_interval_min"] = new_val
                except (ValueError, TypeError):
                    log_warn("CONFIG", f"weather_interval_min invalide ignoré : {d['weather_interval_min']!r} — attendu int")
            if "network_iface" in d:
                new_val = str(d["network_iface"])
                if new_val in ("auto", "eth", "wifi"):
                    if new_val != RUNTIME.get("network_iface", "auto"):
                        changed.append("network_iface")
                    RUNTIME["network_iface"] = new_val
                else:
                    log_warn("CONFIG", f"network_iface invalide ignoré : {new_val!r} — attendu 'auto'|'eth'|'wifi'")

        if changed:
            with runtime_lock:
                _log_city = RUNTIME.get("city", "?")
                _log_intv = RUNTIME["weather_interval_min"]
                _log_iface= RUNTIME["network_iface"]
            log_info(
                "CONFIG",
                f"city={_log_city!r}  interval={_log_intv}min  iface={_log_iface}",
            )
            _save_runtime()

        return JSONResponse(status_code=200, content={"ok": True, "updated": changed})
    except Exception as e:
        log_err("CONFIG", str(e))
        return JSONResponse(status_code=400, content={"error": str(e)})

# ============================================================
# DÉMARRAGE
# ============================================================

_START_TIME = 0.0  # valeur précise définie dans __main__ après chargement du module

log_info("INIT", "=== SysView Bridge v6 ===")
log_info("INIT", "psutil  : OK")
log_info("INIT", "media   : extension Chrome uniquement")
log_info("INIT", f"LHM     : {config.LHM_URL}")
log_info("INIT", f"Port    : {config.API_PORT}")
log_info("INIT", f"Log     : {_LOG_PATH}")

if __name__ == "__main__":
    _START_TIME = time.time()
    _load_runtime()
    _aether_start()
    threading.Thread(target=hardware_loop, daemon=True, name="hardware").start()
    threading.Thread(target=disk_loop,     daemon=True, name="disk").start()
    threading.Thread(target=weather_loop,  daemon=True, name="weather").start()
    log_ok("INIT", "Threads démarrés (hardware, disk, weather)")
    log_info("SERVER", f"Demarrage sur le port {config.API_PORT}...")

    try:
        uvicorn.run(
            app,
            host="127.0.0.1",
            port=config.API_PORT,
            log_level="warning",
            log_config=None,   # sys.stdout=None avec .pyw → formatter uvicorn crashe sans ca
        )
    except Exception as _e:
        log_err("SERVER", f"Crash fatal : {_e}")
        log_err("SERVER", traceback.format_exc())
