🇫🇷 **Français** | [🇬🇧 English](README_ENG.md)

---

# SysView V6 — Wallpaper Engine System Monitor

Fond d'écran interactif pour Wallpaper Engine affichant en temps réel :
**CPU · GPU · RAM · VRAM · Réseau · Stockage · Météo · Pollen · Qualité de l'air · Média · Visualiseur audio**

Compatible Windows 10 / 11 x64 — Wallpaper Engine requis

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        WALLPAPER ENGINE                              │
│  SysView.html  (Chromium isolé — accès réseau local autorisé)        │
│                                                                      │
│  Horloge · Date · Ville   Monitoring CPU/GPU/RAM   Météo/QAI/Pollen  │
│  Stockage C:→H:           VRAM · Réseau            Visualiseur audio │
│                      Média (titre · miniature · barre)               │
│                                                                      │
│  GET /v1/perf    (500 ms)   GET /v1/weather     GET /v1/media (500ms)│
│  POST /v1/config (debounce 300 ms)                                   │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
           ┌───────────────────▼───────────────────────────────┐
           │       SysViewBridge.pyw  —  FastAPI  :5001         │
           │                                                    │
           │  hardware_loop (500ms) ──► SysViewHardware :8086   │
           │                       └──► psutil (fallback CPU)   │
           │  disk_loop     (10s)  ──► psutil disk_usage        │
           │  weather_loop (1–15min)──► Aether :8001            │
           │                       └──► Open-Meteo (prob. pluie)│
           │  POST /v1/media      ◄── Extension Chrome (1s)     │
           └──────────┬──────────────────────────┬─────────────┘
                      │                          │
       ┌──────────────▼──────────────┐  ┌────────▼────────────────────┐
       │    SysViewHardware.exe       │  │  Aether — proxy Open-Meteo  │
       │    LibreHardwareMonitorLib   │  │  FastAPI :8001               │
       │    127.0.0.1:8086  (Admin)   │  │  Interface web :8001         │
       │    Poll 1s — JSON plat       │  │  Météo-France AROME (FR)     │
       │    cpu · gpu · ram · net     │  │  GET /api/live_data          │
       │    GET /data.json            │  │  POST /api/config (lat/lon)  │
       └──────────────────────────────┘  └────────────┬────────────────┘
                                                      │
                                         ┌────────────▼────────────────┐
                                         │  Open-Meteo  (HTTPS, sans   │
                                         │  clé API)                   │
                                         │  Forecast · QAI · Pollen    │
                                         │  Geocoding (ville → lat/lon)│
                                         └─────────────────────────────┘

       ┌──────────────────────────────────┐
       │  SysViewExtension  (Chrome MV3)  │
       │  content.js  → détecte médias    │
       │  background.js → POST /v1/media  │
       └──────────────────────────────────┘
```

---

## Contenu du projet

```
SysView V6/
├── SysView.html                   ← Wallpaper principal (HTML/CSS/JS vanilla)
├── project.json                   ← Propriétés Wallpaper Engine (panneau Personnaliser)
├── preview.gif                    ← Aperçu miniature dans la bibliothèque WE
├── setup.bat                      ← Installateur tout-en-un (une seule commande)
├── README.md                      ← Ce fichier (FR)
├── README_ENG.md                  ← English version
│
├── API V3/
│   ├── SysViewBridge.pyw          ← Serveur FastAPI (bridge principal, port 5001)
│   ├── config.py                  ← Ports et URLs des services
│   ├── runtime_config.json        ← Config persistée : ville · coordonnées · intervalle
│   │                                 (créé/mis à jour automatiquement)
│   ├── install.bat                ← Installateur bridge seul (sans setup.bat)
│   ├── stop.bat                   ← Arrêt du bridge
│   ├── uninstall.bat              ← Désinstallation complète
│   ├── logs/
│   │   └── sysview.log            ← Journal rotatif (10 Mo × 5)
│   ├── SysViewHardware/           ← Service C# de capteurs (source)
│   │   ├── Program.cs
│   │   ├── SysViewHardware.csproj
│   │   └── app.manifest           ← Élévation admin automatique (requireAdministrator)
│   ├── SysViewHardware.exe        ← Binaire compilé (créé par setup.bat, ignoré par git)
│   └── aether/                    ← Proxy Open-Meteo (téléchargé par setup.bat)
│       ├── main.py
│       ├── requirements.txt
│       └── frontend/              ← Interface web (http://127.0.0.1:8001)
│
└── SysViewExtension/
    ├── manifest.json              ← Manifest MV3 (Chromium)
    ├── content.js                 ← Détecte les médias sur toutes les pages
    ├── background.js              ← Service worker → POST /v1/media
    └── README.txt                 ← Instructions d'installation de l'extension
```

---

## Installation

> **Une seule commande suffit pour tout installer.**
> `setup.bat` gère automatiquement les 6 étapes ci-dessous.

---

### Étape 0 — Wallpaper Engine

1. Télécharger le ZIP depuis GitHub et le dézipper
2. Dans **Wallpaper Engine** → bas de bibliothèque → **"Ouvrir un fond d'écran"**
3. Sélectionner `SysView.html`
4. WE copie les fichiers dans :
   ```
   ...\wallpaper_engine\projects\myprojects\SysView V6\
   ```
5. **WE → Paramètres → Général** : activer **"Autoriser l'accès réseau aux wallpapers web"** *(obligatoire)*

> **Accéder au dossier projet** : clic droit sur SysView dans la bibliothèque → *"Ouvrir le dossier"*

---

### Étape 1 — Installer tout le reste (setup.bat)

Depuis le dossier projet WE, **double-cliquer `setup.bat`**.

Le script effectue automatiquement :

| Étape | Action |
|-------|--------|
| **1/6** | Télécharge SysView V6 depuis GitHub (mise à jour) |
| **2/6** | Compile `SysViewHardware.exe` via .NET 8 SDK (téléchargé si absent) — configure le démarrage admin automatique via Planificateur de tâches |
| **3/6** | Vérifie Python 3.10+ — le télécharge depuis python.org si absent |
| **4/6** | Installe les paquets Python du bridge (`fastapi` `uvicorn` `requests` `psutil` `slowapi`) |
| **5/6** | Télécharge Aether depuis GitHub + installe ses dépendances |
| **6/6** | Configure le démarrage automatique au login Windows + lance tout |

**Durée estimée :** 3 à 10 min selon la connexion (première compilation .NET ~2–3 min, NuGet inclus).

---

### Vérification

Ouvrir dans le navigateur : [http://127.0.0.1:5001/v1/status](http://127.0.0.1:5001/v1/status)

```json
{
  "name":   "SysView Bridge v6",
  "uptime": "1m 30s",
  "port":   5001,
  "modules": {
    "psutil":  "ok",
    "lhm":     "ok",
    "aether":  "ok",
    "model":   "Météo-France AROME"
  },
  "endpoints": {
    "health":    "ok",
    "perf":      "ok",
    "weather":   "ok",
    "aether_ui": "http://127.0.0.1:8001",
    "media":     "idle"
  },
  "extension": {
    "active":      false,
    "last_seen_s": null
  }
}
```

| Champ | Valeur attendue |
|-------|----------------|
| `modules.lhm` | `"ok"` — SysViewHardware tourne avec droits admin sur le port 8086 |
| `modules.aether` | `"ok"` — météo chargée (peut être `"pending"` les premières secondes) |

Interface web Aether (config météo, prévisions, QAI) : [http://127.0.0.1:8001](http://127.0.0.1:8001)

---

### Étape 2 — Extension navigateur *(optionnel)*

Permet d'afficher dans la barre média les vidéos lues dans le navigateur
(YouTube, Twitch, Netflix, Spotify Web…) avec titre, artiste et miniature.

**Navigateurs compatibles :** Brave · Chrome · Edge · Opera · Vivaldi *(Chromium uniquement)*

**Sites supportés :** YouTube · YouTube Music · Twitch · Netflix · Prime Video · Vimeo · Dailymotion · Plex · Emby · Jellyfin · tout site HTML5 `<video>` / `<audio>`

**Installation :**

1. Ouvrir la page des extensions :
   - Brave : `brave://extensions` · Chrome : `chrome://extensions` · Edge : `edge://extensions`
2. Activer le **Mode développeur**
3. Cliquer **"Charger l'extension non empaquetée"**
4. Sélectionner le dossier `SysViewExtension\` dans le dossier projet

**Vérifier :** lire une vidéo YouTube → ouvrir [http://127.0.0.1:5001/v1/media](http://127.0.0.1:5001/v1/media) — titre et artiste doivent apparaître.

---

## Configuration du wallpaper

Dans le panneau **Personnaliser** de Wallpaper Engine :

### Couleurs & Style

| Paramètre | Description |
|-----------|-------------|
| **Language** | FR / EN — bascule tous les textes instantanément |
| **Background image** | Image de fond JPG/PNG/GIF/WEBP (optionnel) |
| **UI Scale** | Échelle de l'interface (100 = taille normale) |
| **Opacity** | Opacité des panneaux (0 = invisible, 100 = plein) |
| **Bottom bar height** | Hauteur en px au-dessus de la barre des tâches Windows |
| **Accent color** | Couleur principale — bordures, barres, lueurs |
| **Secondary accent color** | Couleur secondaire — labels, reflets |
| **Background color** | Couleur de fond de base |
| **Text color** | Couleur du texte général |
| **Clock format** | 24h ou 12h (AM/PM) |
| **Temperature unit** | °C ou °F |
| **Show temperature decimal** | Décimale sur la température météo (ex: 15.6 au lieu de 16) |

### Localisation & Météo

| Paramètre | Description |
|-----------|-------------|
| **Show city and country** | Affiche la ville sous l'horloge et dans le panneau météo |
| **Set weather location** | Activer pour saisir manuellement la ville |
| **City name** | Nom de la ville (ex : `PARIS`) — géocodée automatiquement |
| **Weather refresh interval** | Intervalle de rafraîchissement météo (1 à 15 min) |
| **Show weather source badge** | Afficher/cacher le badge "Open-Meteo" |

> La ville est géocodée automatiquement via Open-Meteo Geocoding API. Les coordonnées et l'intervalle sont sauvegardés dans `runtime_config.json` et restaurés au redémarrage du bridge.

### Seuils de température

| Paramètre | Défaut |
|-----------|--------|
| **CPU — warning temp** | 80 °C (orange) |
| **CPU — critical temp** | 91 °C (rouge) |
| **GPU — warning temp** | 80 °C (orange) |
| **GPU — critical temp** | 95 °C (rouge) |

### Panneaux à afficher

| Paramètre | Description |
|-----------|-------------|
| **Show Monitoring panel** | Panneau CPU / GPU / RAM / VRAM / Réseau |
| **Show CPU / GPU / VRAM / RAM / Network block** | Blocs individuels |
| **Network interface to display** | `Auto` (WiFi + Ethernet), `Ethernet`, `Wi-Fi` |
| **Show Storage panel** | Disques C: à H: |
| **Show Disk C/D/E/F/G/H** | Lecteurs individuels |
| **Show free space on disks** | Affiche l'espace libre restant |
| **Show Media player** | Barre du bas (miniature, progression, visualiseur audio) |
| **Show Weather panel** | Panneau météo côté droit |

---

## Fonctionnalités

### Monitoring matériel

SysViewHardware lit les capteurs via **LibreHardwareMonitorLib** (bibliothèque .NET intégrée).
Le bridge le consulte toutes les 500 ms et applique un lissage EMA avant d'exposer les données.

| Métrique | Source | Fallback |
|----------|--------|----------|
| Nom CPU / GPU | SysViewHardware | — |
| Température CPU | SysViewHardware (Package / Tdie / Tctl) | — |
| Charge CPU | SysViewHardware (Total Load) | psutil |
| Température GPU | SysViewHardware (Core) | — |
| Charge GPU | SysViewHardware (Core Load) | — |
| VRAM utilisée / totale | SysViewHardware (SmallData, MB) | — |
| RAM % | SysViewHardware (Memory Load) | psutil |
| Réseau Wi-Fi KB/s | SysViewHardware (Throughput, détection par nom NIC) | — |
| Réseau Ethernet KB/s | SysViewHardware (Throughput, détection par nom NIC) | — |
| Réseau Auto | Somme Wi-Fi + Ethernet | psutil |
| Disques (C: → H:) | psutil (toutes les 10 s) | — |

**Lissage EMA :**
- CPU · GPU · RAM · VRAM : α = 0.60 (t₉₀ ≈ 1.9 s) — transitions fluides
- Réseau : α = 0.97 — très réactif aux pics

**Couleurs de température :** < seuil\_warn → accent / ≥ seuil\_warn → orange / ≥ seuil\_crit → rouge
Température et charge sont colorées indépendamment.

### Météo · Pollen · Qualité de l'air

- Source : **Open-Meteo** via **Aether** (gratuit, sans clé API)
- Modèle automatique selon la localisation (Météo-France AROME pour la France)
- Intervalle configurable de **1 à 15 minutes** dans WE
- Rafraîchissement immédiat à chaque changement de ville ou de coordonnées
- Retry automatique avec backoff exponentiel en cas d'échec

| Indicateur | Niveaux |
|------------|---------|
| Température / Ressenti | °C ou °F |
| Probabilité de pluie | 0–100 % (Open-Meteo best_match — fallback si AROME absent) |
| Vent | km/h |
| QAI Europe | 🟢 Bon · 🟡 Correct · 🟠 Modéré · 🔴 Mauvais · 🔴 Très mauvais |
| Pollen | 🟢 Nul · 🟢 Faible · 🟡 Modéré · 🟠 Élevé · 🔴 Très élevé (grains/m³) |

> **Pollen** : somme graminées + bouleau + aulne + ambroisie (Copernicus CAMS).

### Panneau média

- Titre, artiste, miniature, barre de progression interpolée
- Source : extension Chrome (YouTube, Twitch, Netflix, tout site HTML5)
- Priorité source : un titre en pause ne remplace pas un titre déjà en lecture
- Position interpolée côté bridge (pas de saut visible)

### Visualiseur audio

- Alimenté par `wallpaperRegisterAudioListener` (Wallpaper Engine)
- Réagit à **toute la sortie audio du PC** (jeux, musique, vidéos…)
- 24 barres — spectre 0–63 bins (canal gauche)
- EMA asymétrique : montée α = 0.80 (rapide) / descente α = 0.18 (douce)

---

## Endpoints de l'API

| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/v1/health` | État du bridge (uptime, version) |
| GET | `/v1/perf` | CPU · GPU · RAM · VRAM · Réseau · Disques |
| GET | `/v1/weather` | Météo · QAI · Pollen · Probabilité de pluie |
| GET | `/v1/media` | Lecture en cours (titre · artiste · position interpolée) |
| GET | `/v1/status` | Diagnostic complet (modules · endpoints · extension) |
| POST | `/v1/config` | Reçoit la config depuis WE (ville · interface réseau · intervalle) |
| POST | `/v1/media` | Reçoit les données de l'extension Chrome |
| GET | `/docs` | Documentation interactive Swagger UI |

**Ports actifs après installation :**

| Service | Port | URL |
|---------|------|-----|
| SysViewBridge | 5001 | http://127.0.0.1:5001 |
| Aether (météo) | 8001 | http://127.0.0.1:8001 |
| SysViewHardware | 8086 | http://127.0.0.1:8086/data.json |

---

## Dépannage

| Problème | Solution |
|----------|----------|
| `modules.lhm = "offline"` dans /v1/status | SysViewHardware non lancé — relancer via le Planificateur de tâches `SysViewHardware` ou exécuter manuellement en Administrateur |
| CPU / GPU / températures à `—` | SysViewHardware hors ligne (voir ci-dessus) |
| `modules.aether = "pending"` | Normal les premières secondes — attendre le premier cycle météo |
| Réseau à 0 en mode WiFi ou Ethernet | Vérifier que SysViewHardware détecte bien les interfaces réseau (`/data.json` → clés `net_dl_kb` / `net_eth_dl_kb`) |
| Probabilité de pluie vide (`—`) | Aether encore en démarrage — attendre 1–3 min |
| Ville non reconnue | Saisir le nom seul, sans code pays (ex : `PARIS` et non `PARIS, FR`) |
| Mauvais intervalle après redémarrage | Vérifier que `runtime_config.json` existe dans `API V3\` |
| Panneau média vide | Installer l'extension Chrome et lancer une vidéo dans le navigateur |
| `extension.active = false` | Extension non installée, ou lecture sur un onglet en arrière-plan |
| Wallpaper ne récupère aucune donnée | WE → Paramètres → Général → activer *"Autoriser l'accès réseau"* |
| Bridge ne démarre pas | Lire `API V3\logs\sysview.log` |
| Python non détecté | Installer depuis python.org *(pas le Microsoft Store)* |
| Port 5001 déjà utilisé | Lancer `stop.bat` puis relancer `install.bat` |
| Aether inaccessible (:8001) | Relancer `install.bat` ou vérifier les logs |

---

## Gestion du bridge

Les scripts dans `API V3\` permettent de gérer le bridge indépendamment de `setup.bat` :

| Script | Action |
|--------|--------|
| `install.bat` | Installe les paquets Python + Aether + démarre le bridge |
| `stop.bat` | Arrête le bridge (lit `bridge.pid`) |
| `uninstall.bat` | Arrête le bridge · supprime les paquets Python · supprime `aether\` · supprime le raccourci de démarrage |

---

## Désinstallation

| Composant | Action |
|-----------|--------|
| **SysViewHardware** | Planificateur de tâches → supprimer `SysViewHardware` · supprimer `SysViewHardware.exe` |
| **Bridge + Aether** | `API V3\uninstall.bat` |
| **Extension** | Page extensions du navigateur → SysView Media Bridge → Supprimer |
| **Wallpaper Engine** | Clic droit sur SysView → Supprimer |

---

## Technologies

### SysViewHardware — `API V3/SysViewHardware/`

| Élément | Détail |
|---------|--------|
| **LibreHardwareMonitorLib 0.9.4** | Bibliothèque .NET — accès bas niveau aux capteurs matériel |
| **IVisitor / UpdateVisitor** | Pattern visiteur LHM — parcourt Computer → Hardware → SubHardware → Sensor |
| **Détection par nom** | Température CPU : contient `package` / `tdie` / `tctl` ; GPU : `core` ; WiFi : `wi-fi` / `wifi` / `wireless` / `wlan` |
| **SensorType.SmallData** | Valeur en MB pour VRAM utilisée / totale |
| **SensorType.Throughput** | Valeur en B/s ÷ 1000 = KB/s pour le réseau |
| **app.manifest** | `requireAdministrator` — élévation UAC automatique à chaque lancement |
| **dotnet publish** | `net8.0-windows` · `win-x64` · `SelfContained` · `PublishSingleFile` → exe autonome ~50 Mo |
| **HttpListener** | Serveur HTTP minimal sur `127.0.0.1:8086` — `GET /data.json` · `GET /health` |
| **Poll 1s** | Thread de fond — pas de blocage du serveur HTTP |

### SysViewBridge — `API V3/SysViewBridge.pyw`

| Technologie | Usage |
|-------------|-------|
| **FastAPI + Uvicorn** | Framework ASGI — routing, validation, docs Swagger |
| **slowapi** | Rate limiting — 350/min sur les endpoints de poll, 60/min sur status / config |
| **psutil** | Disques (toutes les 10 s) · fallback CPU · fallback réseau |
| **requests** | HTTP vers SysViewHardware (:8086), Aether (:8001), Open-Meteo Geocoding, Open-Meteo Forecast |
| **3 threads démons** | `hardware_loop` (500 ms) · `disk_loop` (10 s) · `weather_loop` (1–15 min) |
| **threading.Lock** | `perf_lock` `weather_lock` `media_lock` `runtime_lock` — accès thread-safe |
| **threading.Event** | `_weather_event` — réveille immédiatement weather_loop si la ville / l'intervalle change |
| **Cache LHM 4 s** | Évite de re-tenter une requête bloquante à chaque tick 500 ms si SysViewHardware est hors ligne |
| **json + pathlib** | Persistance de la config runtime dans `runtime_config.json` |
| **RotatingFileHandler** | Log rotatif 10 Mo × 5 dans `logs/sysview.log` |
| **CORS + Private Network** | Headers nécessaires pour le renderer Chromium de WE et l'extension Chrome |

### Aether — `API V3/aether/`

| Concept | Détail |
|---------|--------|
| **FastAPI :8001** | Proxy Open-Meteo avec interface web d'administration |
| **Modèle automatique** | Sélection du meilleur modèle selon les coordonnées (AROME pour la France) |
| **POST /api/config** | Reçoit ville + lat/lon depuis le bridge — synchronise l'affichage web |
| **GET /api/live_data** | Agrège météo courante + QAI + pollen (Copernicus CAMS) |

### Frontend — `SysView.html`

| Technologie | Usage |
|-------------|-------|
| **HTML/CSS/JS vanilla** | Aucun framework — compatible avec le renderer Chromium de WE |
| **CSS Custom Properties** | Variables `--ac` `--ac2` `--bg` `--tx` `--pa` `--sz` modifiées en temps réel par WE |
| **`wallpaperPropertyListener`** | API WE — reçoit les changements de propriétés utilisateur |
| **`wallpaperRegisterAudioListener`** | API WE — 128 bins de fréquence audio (~30 fps) |
| **EMA** | α = 0.60 hardware · α = 0.97 réseau · EMA asymétrique visualiseur audio |
| **fetch() + setInterval** | Polling 500 ms (perf + media) |
| **Debounce 300 ms** | Regroupe les appels POST /v1/config |

### Extension Chrome — `SysViewExtension/`

| Fichier | Rôle |
|---------|------|
| **manifest.json** | MV3 · permissions `<all_urls>` + `http://127.0.0.1:5001/*` |
| **content.js** | Injecté dans toutes les pages — détecte `<video>` et MediaSession API |
| **background.js** | Service worker MV3 — reçoit les messages, POST vers `/v1/media` |

---

*SysView V6 — Windows 10 / 11 x64*  
*GitHub : [github.com/Mrtt555/SysView-V6](https://github.com/Mrtt555/SysView-V6)*
