# ============================================================
# SysView Bridge — Configuration interne minimale
# Toutes les autres options (lat/lon, intervalle météo,
# interface réseau...) passent par Wallpaper Engine via POST /v1/config
# ============================================================

# Port d'écoute du bridge
API_PORT = 5001

# URL SysViewHardware — service capteurs passif (LibreHardwareMonitorLib)
# Endpoint /data.json — JSON plat, clés identiques aux attentes de hardware_loop()
LHM_URL = "http://127.0.0.1:8086/data.json"  # IPv4 explicite — évite la résolution ::1 sur Windows 11

# URL Aether — proxy Open-Meteo multi-modèles (port 8001 par défaut)
AETHER_URL = "http://127.0.0.1:8001"

# Open-Meteo Geocoding API — utilisé directement pour la recherche de ville
# (plus fiable que l'endpoint /api/search_city d'Aether)
GEOCODING_URL = "https://geocoding-api.open-meteo.com/v1/search"

# Open-Meteo Forecast API — utilisé en direct pour les variables non fournies
# par AROME en current (ex: precipitation_probability)
OPEN_METEO_URL = "https://api.open-meteo.com/v1/forecast"
