# 🌤️ Aether — Weather Dashboard

> Tableau de bord météo full-stack propulsé par **Astralcodes** · Mrtt555  
> Proxy FastAPI vers [Open-Meteo](https://open-meteo.com) avec interface d'administration, graphiques de prévisions, qualité de l'air et webhook Discord.

---

## ✨ Fonctionnalités

- **Tableau de bord 3 pages** — Configuration, Résultats Live, Documentation
- **Recherche de ville** avec autocomplétion (géocodage Open-Meteo)
- **8 modèles météo** au choix : Météo-France AROME, DWD ICON, ECMWF, NOAA GFS, UK Met Office…
- **Recommandation de modèle automatique** selon la localisation (code pays)
- **Prévisions 24 h** avec graphiques température (courbe) et précipitations (barres) via Chart.js
- **Qualité de l'air** — IQA européen, PM10, PM2.5, pollens (Copernicus CAMS)
- **Actualisation automatique** toutes les 60 secondes avec compte à rebours
- **Mode sombre** complet (palette WCAG AA) avec persistance localStorage
- **Bilingue** FR / EN avec persistance localStorage
- **Webhook** — envoi des données live vers n'importe quelle URL ; embed Discord natif
- **Rate limiting configurable** par IP (max 7 000 req/min) via SlowAPI
- **Vue JSON brut** avec fenêtre de code élégante
- **PWA ready** — favicon SVG + manifest.json
- **Swagger UI** & ReDoc intégrés (`/docs`, `/redoc`)

---

## 🗂️ Structure du projet

```
.
├── main.py                  # Backend FastAPI
├── config.json              # Configuration persistée (auto-créée au démarrage)
├── requirements.txt
└── frontend/
    ├── index.html           # Page Configuration
    ├── live.html            # Page Résultats Live
    ├── docs.html            # Page Documentation
    ├── app.js               # Toute la logique JS (shared)
    ├── style.css            # Classes custom + dark mode
    ├── favicon.svg          # Icône SVG
    └── manifest.json        # PWA manifest
```

---

## 🚀 Installation

### Prérequis

- Python 3.10+

### Étapes

```bash
# 1. Cloner le dépôt
git clone https://github.com/Mrtt555/aether.git
cd aether

# 2. Créer l'environnement virtuel
python -m venv .venv
.venv\Scripts\activate        # Windows
# source .venv/bin/activate   # Linux / macOS

# 3. Installer les dépendances
pip install -r requirements.txt

# 4. Lancer le serveur
uvicorn main:app --reload --port 8000
```

Ouvre ensuite [http://localhost:8000](http://localhost:8000) dans ton navigateur.

---

## 🔌 Endpoints API

| Méthode | Endpoint | Description |
|---------|----------|-------------|
| `GET` | `/api/config` | Retourne la configuration actuelle |
| `POST` | `/api/config` | Met à jour partiellement la configuration |
| `GET` | `/api/search_city?q=...` | Recherche géographique (max 8 résultats) |
| `GET` | `/api/live_data` | Données météo + qualité de l'air agrégées |
| `GET` | `/api/weather_models` | Liste des modèles météo disponibles |
| `POST` | `/api/webhook/trigger` | Envoie les données live vers l'URL webhook |

Documentation interactive : [`/docs`](http://localhost:8000/docs) (Swagger) · [`/redoc`](http://localhost:8000/redoc)

---

## 🛰️ Modèles météo disponibles

| ID | Nom | Couverture |
|----|-----|-----------|
| `best_match` | Sélection automatique | Mondial |
| `ecmwf_ifs025` | ECMWF IFS (0.25°) | Mondial |
| `meteofrance_seamless` | Météo-France ARPEGE + AROME | Europe / France |
| `meteofrance_arome_france` | Météo-France AROME (1.3 km) | France métropolitaine |
| `dwd_icon_seamless` | DWD ICON | Europe centrale |
| `dwd_icon_eu` | DWD ICON-EU (7 km) | Europe |
| `gfs_seamless` | NOAA GFS | Mondial |
| `ukmo_seamless` | UK Met Office | Europe NW |

> **Conseil** : utilisez `meteofrance_arome_france` pour la France, `dwd_icon_seamless` pour l'Allemagne/Belgique, `ukmo_seamless` pour le Royaume-Uni.

---

## ⚙️ Configuration

La configuration est stockée dans `config.json` et modifiable depuis l'interface :

| Champ | Défaut | Description |
|-------|--------|-------------|
| `city` | `Paris` | Nom de la ville |
| `latitude` | `48.8566` | Latitude |
| `longitude` | `2.3522` | Longitude |
| `weather_model` | `best_match` | Modèle de prévision |
| `weather_params` | 10 paramètres | Paramètres météo exposés |
| `air_quality_params` | 5 paramètres | Paramètres qualité de l'air |
| `rate_limit_search` | `10` | Limite req/min pour la recherche |
| `rate_limit_live` | `20` | Limite req/min pour les données live |
| `webhook_url` | `""` | URL webhook (Discord ou générique) |

---

## 🔗 Webhook

Configurez une URL webhook depuis l'interface. Aether détecte automatiquement les URLs Discord et envoie un **embed formaté** avec les données météo clés.

**Format Discord :**
```json
{
  "embeds": [{
    "title": "🌤️ Météo — Paris",
    "color": 2450155,
    "fields": [
      { "name": "🌡️ Température", "value": "`18.4 °C`", "inline": true },
      ...
    ],
    "footer": { "text": "Modèle : Météo-France · Astralcodes" }
  }]
}
```

Pour toute autre URL, le payload JSON complet est envoyé.

---

## ⚡ Rate Limiting

Le rate limiting est géré par **SlowAPI** et configurable à chaud depuis l'interface (sans redémarrage du serveur) :

- **Recherche de ville** : 10 req/min par défaut (max 7 000)
- **Données live** : 20 req/min par défaut (max 7 000)

En cas de dépassement, l'API retourne `HTTP 429 Too Many Requests`.

---

## 🛠️ Stack technique

| Couche | Technologie |
|--------|------------|
| Backend | [FastAPI](https://fastapi.tiangolo.com/) + [Uvicorn](https://www.uvicorn.org/) |
| HTTP client | [httpx](https://www.python-httpx.org/) (async) |
| Rate limiting | [SlowAPI](https://github.com/laurentS/slowapi) |
| Validation | [Pydantic v2](https://docs.pydantic.dev/) |
| CSS | [Tailwind CSS](https://tailwindcss.com/) (CDN) |
| Graphiques | [Chart.js v4](https://www.chartjs.org/) (CDN) |
| Données | [Open-Meteo](https://open-meteo.com/) (gratuit, sans clé API) |

---

## 📋 Conditions d'utilisation — Open-Meteo

- ✅ **Gratuit** — Aucune clé API, aucune inscription requise
- 📊 **Usage non-commercial** — Jusqu'à **10 000 appels/jour** offerts
- 📜 **Licence CC BY 4.0** — Attribution requise : citer *Open-Meteo.com*
- 🏢 **Usage commercial** — Plans payants disponibles

---

## 📄 Licence

Projet personnel — **© 2026 Mrtt555 · Astralcodes** — Tous droits réservés.  
Données météo fournies par [Open-Meteo](https://open-meteo.com) sous licence [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
