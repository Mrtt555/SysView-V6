from fastapi import FastAPI, Request, HTTPException
from fastapi.responses import HTMLResponse
from fastapi.staticfiles import StaticFiles
from fastapi.middleware.cors import CORSMiddleware
from slowapi import Limiter, _rate_limit_exceeded_handler
from slowapi.util import get_remote_address
from slowapi.errors import RateLimitExceeded
from pydantic import BaseModel
import asyncio
import httpx
import json
from pathlib import Path
from typing import Optional, List

WEATHER_MODELS: dict[str, dict] = {
    "best_match": {
        "name": "Sélection automatique",
        "name_en": "Automatic selection",
        "provider": "Open-Meteo (ECMWF · DWD · Météo-France · NOAA GFS…)",
        "description": "Choisit automatiquement le meilleur modèle selon la localisation.",
        "description_en": "Automatically selects the best model based on location.",
        "region": "Mondial",
    },
    "ecmwf_ifs025": {
        "name": "ECMWF IFS",
        "name_en": "ECMWF IFS",
        "provider": "European Centre for Medium-Range Weather Forecasts (ECMWF)",
        "description": "Modèle global de référence européen — résolution 0.25°.",
        "description_en": "European reference global model — 0.25° resolution.",
        "region": "Mondial",
    },
    "meteofrance_seamless": {
        "name": "Météo-France (ARPEGE + AROME)",
        "name_en": "Météo-France (ARPEGE + AROME)",
        "provider": "Météo-France",
        "description": "Modèle national français, haute résolution sur la France et l'Europe.",
        "description_en": "French national model, high resolution over France and Europe.",
        "region": "Europe / France",
    },
    "meteofrance_arome_france": {
        "name": "Météo-France AROME France (1.3 km)",
        "name_en": "Météo-France AROME France (1.3 km)",
        "provider": "Météo-France",
        "description": "Très haute résolution, France métropolitaine uniquement.",
        "description_en": "Very high resolution, mainland France only.",
        "region": "France métropolitaine",
    },
    "dwd_icon_seamless": {
        "name": "DWD ICON",
        "name_en": "DWD ICON",
        "provider": "Deutscher Wetterdienst (DWD — Service météo allemand)",
        "description": "Modèle allemand, excellent sur l'Europe centrale.",
        "description_en": "German model, excellent over central Europe.",
        "region": "Europe centrale",
    },
    "dwd_icon_eu": {
        "name": "DWD ICON-EU (7 km)",
        "name_en": "DWD ICON-EU (7 km)",
        "provider": "Deutscher Wetterdienst (DWD — Service météo allemand)",
        "description": "Version Europe du modèle ICON, haute résolution (7 km).",
        "description_en": "European version of ICON, high resolution (7 km).",
        "region": "Europe",
    },
    "gfs_seamless": {
        "name": "NOAA GFS",
        "name_en": "NOAA GFS",
        "provider": "National Oceanic and Atmospheric Administration (NOAA — USA)",
        "description": "Modèle global américain, idéal pour les Amériques.",
        "description_en": "US global model, ideal for the Americas.",
        "region": "Mondial",
    },
    "ukmo_seamless": {
        "name": "UK Met Office",
        "name_en": "UK Met Office",
        "provider": "Met Office (Royaume-Uni)",
        "description": "Modèle britannique, bon sur l'Europe du Nord-Ouest.",
        "description_en": "British model, good over Northwest Europe.",
        "region": "Europe NW",
    },
}

# Bounding-box France métropolitaine (mainland + Corse, hors DOM-TOM)
_METRO_FRANCE = (41.3, 51.1, -5.2, 9.6)  # lat_min, lat_max, lon_min, lon_max


def _resolve_model(model_id: str, lat: float, lon: float) -> str:
    """
    Quand best_match est sélectionné, choisit automatiquement le modèle le plus
    précis selon la localisation :
      - France métropolitaine (mainland + Corse) → meteofrance_arome_france (1,3 km)
      - Reste du monde                           → best_match (sélection auto Open-Meteo)
    Tout autre modèle explicitement choisi est renvoyé tel quel.
    """
    if model_id != "best_match":
        return model_id
    lat_min, lat_max, lon_min, lon_max = _METRO_FRANCE
    if lat_min <= lat <= lat_max and lon_min <= lon <= lon_max:
        return "meteofrance_arome_france"
    return "best_match"


DEFAULT_CONFIG = {
    "city": "Paris",
    "latitude": 48.8566,
    "longitude": 2.3522,
    "weather_model": "best_match",
    "rate_limit_search": 10,
    "rate_limit_live": 20,
    "weather_params": [
        "temperature_2m", "relative_humidity_2m", "apparent_temperature",
        "precipitation", "weather_code", "cloud_cover", "wind_speed_10m",
        "wind_direction_10m", "wind_gusts_10m", "uv_index"
    ],
    "air_quality_params": [
        "european_aqi", "pm10", "pm2_5", "grass_pollen", "birch_pollen"
    ],
    "webhook_url": "",
}

CONFIG_PATH = Path("config.json")

# Cache module-level — évite une lecture disque à chaque requête rate-limited.
# Invalidé par save_config() à chaque POST /api/config.
_cfg_cache: dict | None = None


def load_config() -> dict:
    global _cfg_cache
    if _cfg_cache is not None:
        return _cfg_cache
    if not CONFIG_PATH.exists():
        save_config(DEFAULT_CONFIG)
        return _cfg_cache  # save_config a déjà mis à jour le cache
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        _cfg_cache = json.load(f)
    return _cfg_cache


def save_config(cfg: dict) -> None:
    global _cfg_cache
    with open(CONFIG_PATH, "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2, ensure_ascii=False)
    _cfg_cache = cfg   # met à jour le cache après écriture


limiter = Limiter(key_func=get_remote_address)

app = FastAPI(
    title="API Météo & Environnement",
    description=(
        "API proxy personnalisée vers Open-Meteo. Configurez dynamiquement les paramètres "
        "exposés depuis l'interface d'administration, puis interrogez /api/live_data pour "
        "obtenir les données agrégées."
    ),
    version="1.1",
    contact={"name": "Mrtt555 (Astralcodes)"},
)
app.state.limiter = limiter
app.add_exception_handler(RateLimitExceeded, _rate_limit_exceeded_handler)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)
app.mount("/frontend", StaticFiles(directory="frontend"), name="frontend")


class ConfigUpdate(BaseModel):
    city: Optional[str] = None
    latitude: Optional[float] = None
    longitude: Optional[float] = None
    weather_model: Optional[str] = None
    weather_params: Optional[List[str]] = None
    air_quality_params: Optional[List[str]] = None
    rate_limit_search: Optional[int] = None
    rate_limit_live: Optional[int] = None
    webhook_url: Optional[str] = None


def _search_rate() -> str:
    cfg = load_config()
    limit = max(1, min(int(cfg.get("rate_limit_search", 10)), 7000))
    return f"{limit}/minute"


def _live_rate() -> str:
    cfg = load_config()
    limit = max(1, min(int(cfg.get("rate_limit_live", 20)), 7000))
    return f"{limit}/minute"


@app.get("/api/weather_models", tags=["Configuration"], summary="Liste des modèles météo disponibles")
async def get_weather_models():
    return {"models": WEATHER_MODELS}


@app.get("/", response_class=HTMLResponse, tags=["UI"], summary="Configuration")
async def serve_ui():
    return HTMLResponse(content=Path("frontend/index.html").read_text(encoding="utf-8"))


@app.get("/live", response_class=HTMLResponse, tags=["UI"], summary="Résultats Live")
async def serve_live():
    return HTMLResponse(content=Path("frontend/live.html").read_text(encoding="utf-8"))


@app.get("/documentation", response_class=HTMLResponse, tags=["UI"], summary="Documentation")
async def serve_docs_page():
    return HTMLResponse(content=Path("frontend/docs.html").read_text(encoding="utf-8"))


@app.get(
    "/api/config",
    tags=["Configuration"],
    summary="Lire la configuration actuelle",
    description="Retourne le fichier config.json avec la ville, les coordonnées et les paramètres sélectionnés.",
)
async def get_config():
    return load_config()


@app.post(
    "/api/config",
    tags=["Configuration"],
    summary="Mettre à jour la configuration",
    description="Met à jour partiellement la configuration (ville, coordonnées, paramètres). Les champs omis ne sont pas modifiés.",
)
async def update_config(data: ConfigUpdate):
    cfg = load_config()
    updates = data.model_dump(exclude_none=True)
    cfg.update(updates)
    save_config(cfg)
    return {"status": "ok", "config": cfg}


@app.get(
    "/api/search_city",
    tags=["Géocodage"],
    summary="Rechercher une ville",
    description="Interroge l'API de géocodage Open-Meteo. Limité à 10 requêtes/minute par IP.",
)
@limiter.limit(_search_rate)
async def search_city(request: Request, q: str):
    if not q or len(q.strip()) < 2:
        raise HTTPException(status_code=400, detail="La requête doit contenir au moins 2 caractères.")
    async with httpx.AsyncClient(timeout=10.0) as client:
        resp = await client.get(
            "https://geocoding-api.open-meteo.com/v1/search",
            params={"name": q.strip(), "count": 8, "language": "fr", "format": "json"},
        )
        resp.raise_for_status()
        data = resp.json()
    return {
        "results": [
            {
                "name": r.get("name"),
                "country": r.get("country"),
                "country_code": r.get("country_code", ""),
                "admin1": r.get("admin1", ""),
                "latitude": r.get("latitude"),
                "longitude": r.get("longitude"),
            }
            for r in data.get("results", [])
        ]
    }


async def _build_live_response(cfg: dict) -> dict:
    lat = cfg.get("latitude", 48.8566)
    lon = cfg.get("longitude", 2.3522)
    weather_params = cfg.get("weather_params", [])
    air_params     = cfg.get("air_quality_params", [])
    configured_model = cfg.get("weather_model", "best_match")
    model_id = _resolve_model(configured_model, lat, lon)
    if model_id not in WEATHER_MODELS:
        model_id = "best_match"
    model_info = WEATHER_MODELS[model_id]

    # Les trois requêtes Open-Meteo sont indépendantes — on les lance en parallèle
    # avec asyncio.gather() pour réduire la latence de L1+L2+L3 à max(L1,L2,L3).
    async with httpx.AsyncClient(timeout=15.0) as client:

        async def _fetch_current() -> dict:
            if not weather_params:
                return {}
            r = await client.get(
                "https://api.open-meteo.com/v1/forecast",
                params={
                    "latitude": lat, "longitude": lon,
                    "current": ",".join(weather_params),
                    "models": model_id,
                    "timezone": "auto", "forecast_days": 1,
                },
            )
            r.raise_for_status()
            return r.json()

        async def _fetch_forecast() -> dict:
            if not weather_params:
                return {}
            r = await client.get(
                "https://api.open-meteo.com/v1/forecast",
                params={
                    "latitude": lat, "longitude": lon,
                    "hourly": "temperature_2m,precipitation,weather_code,wind_speed_10m",
                    "models": model_id,
                    "timezone": "auto", "forecast_days": 2,
                },
            )
            r.raise_for_status()
            return r.json()

        async def _fetch_air() -> dict:
            if not air_params:
                return {}
            r = await client.get(
                "https://air-quality-api.open-meteo.com/v1/air-quality",
                params={
                    "latitude": lat, "longitude": lon,
                    "current": ",".join(air_params),
                    "timezone": "auto",
                },
            )
            r.raise_for_status()
            return r.json()

        weather_data, forecast_data, air_data = await asyncio.gather(
            _fetch_current(), _fetch_forecast(), _fetch_air()
        )

    return {
        "config": {
            "city": cfg.get("city"),
            "latitude": lat,
            "longitude": lon,
            "weather_model": configured_model,   # modèle tel que configuré par l'utilisateur
            "effective_model": model_id,         # modèle réellement utilisé (peut différer si auto-sélection France)
        },
        "sources": {
            "weather": {
                "model_id":    model_id,
                "name":        model_info["name"],
                "name_en":     model_info["name_en"],
                "provider":    model_info["provider"],
                "description": model_info["description"],
                "region":      model_info["region"],
            },
            "air_quality": {
                "name":           "Copernicus CAMS",
                "name_en":        "Copernicus CAMS",
                "provider":       "Copernicus Atmosphere Monitoring Service (CAMS) — Union européenne",
                "description":    "Service européen de surveillance de l'atmosphère en temps réel.",
                "description_en": "European real-time atmosphere monitoring service.",
                "region":         "Europe / Mondial",
            },
        },
        "weather":     weather_data,
        "air_quality": air_data,
        "forecast":    forecast_data,
    }


def _format_discord(data: dict) -> dict:
    city = data.get("config", {}).get("city", "?")
    w  = data.get("weather", {}).get("current", {})
    wu = data.get("weather", {}).get("current_units", {})
    a  = data.get("air_quality", {}).get("current", {})
    src = data.get("sources", {}).get("weather", {})
    fields = []
    for key, label, emoji in [
        ("temperature_2m",       "Température",    "🌡️"),
        ("apparent_temperature", "Ressenti",        "🤔"),
        ("relative_humidity_2m", "Humidité",        "💧"),
        ("wind_speed_10m",       "Vent",            "💨"),
        ("precipitation",        "Précipitations",  "🌧️"),
        ("cloud_cover",          "Nuages",          "☁️"),
        ("uv_index",             "Indice UV",       "☀️"),
    ]:
        if key in w and w[key] is not None:
            unit = wu.get(key, "")
            fields.append({"name": f"{emoji} {label}", "value": f"`{w[key]} {unit}`.strip()", "inline": True})
    if "european_aqi" in a and a["european_aqi"] is not None:
        fields.append({"name": "🌍 IQA européen", "value": f"`{round(a['european_aqi'])}`", "inline": True})
    ts = data.get("weather", {}).get("current", {}).get("time", "")
    return {
        "embeds": [{
            "title": f"🌤️ Météo — {city}",
            "color": 2450155,
            "fields": fields[:25],
            "footer": {"text": f"Modèle : {src.get('name','?')} · Astralcodes"},
            "timestamp": ts if ts else None,
        }]
    }


@app.get(
    "/api/live_data",
    tags=["Données"],
    summary="Données météo et environnement en direct",
    description=(
        "Agrège les données depuis l'API Météo et l'API Qualité de l'air d'Open-Meteo, "
        "selon la configuration active. Inclut les prévisions horaires 24 h pour les graphiques. "
        "Limité à 20 requêtes/minute par IP."
    ),
)
@limiter.limit(_live_rate)
async def live_data(request: Request):
    return await _build_live_response(load_config())


@app.post(
    "/api/webhook/trigger",
    tags=["Webhook"],
    summary="Déclencher le webhook manuellement",
    description=(
        "Récupère les données live et les envoie via POST à l'URL configurée. "
        "Format Discord embed si l'URL contient discord.com/api/webhooks."
    ),
)
async def trigger_webhook():
    cfg = load_config()
    url = (cfg.get("webhook_url") or "").strip()
    if not url:
        raise HTTPException(status_code=400, detail="Aucune URL webhook configurée.")
    data = await _build_live_response(cfg)
    payload = _format_discord(data) if "discord.com/api/webhooks" in url else data
    async with httpx.AsyncClient(timeout=10.0) as client:
        resp = await client.post(url, json=payload, headers={"User-Agent": "API-Meteo-Astralcodes/1.1"})
    return {"status": "ok", "http_status": resp.status_code, "url": url}
