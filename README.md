🇫🇷 **Français** | [🇬🇧 English](README_ENG.md)

---

# SysView V6 — Wallpaper Engine System Monitor

Fond d'écran interactif pour Wallpaper Engine affichant en temps réel :
**CPU · GPU · RAM · VRAM · Réseau · Stockage · Météo · Pollen · Qualité de l'air · Média · Visualiseur audio**

Compatible Windows 10 / 11 x64 — Wallpaper Engine requis

> GitHub : [github.com/Mrtt555/sysview-wallpaper-engine](https://github.com/Mrtt555/sysview-wallpaper-engine)

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                       WALLPAPER ENGINE                           │
│  SysView.html (Chromium isolé, réseau local autorisé)            │
│                                                                  │
│  ┌──────────┐  ┌─────────────┐  ┌──────────┐  ┌─────────────┐  │
│  │ Horloge  │  │  Monitoring │  │  Météo   │  │    Média    │  │
│  │ Date/    │  │  CPU · GPU  │  │  Pollen  │  │  Titre +    │  │
│  │ Ville    │  │  RAM · VRAM │  │  QAI     │  │  Miniature  │  │
│  │          │  │  Réseau     │  │  Pluie % │  │  Viz audio  │  │
│  └──────────┘  └─────┬───────┘  └────┬─────┘  └──────┬──────┘  │
└────────────────────── │ ──────────── │ ────────────── │ ────────┘
          GET /v1/perf ─┘  GET /v1/weather  GET /v1/media
          POST /v1/config (ville·iface·intervalle — debounce 300ms)
                         ▼
         ┌───────────────────────────────────────────────────┐
         │     SysViewBridge.pyw  —  FastAPI  0.0.0.0:5001   │
         │                                                   │
         │  hardware_loop (750ms) ──► LHM /data.json         │
         │                      └──► psutil (fallback)       │
         │  disk_loop (10s)     ──► psutil disk usage        │
         │  weather_loop (1–15min)► Aether :8001             │
         │                      └──► Open-Meteo (direct)     │
         │  POST /v1/media     ◄── SysViewExtension (1s)     │
         └──────┬──────────────────────────────┬─────────────┘
                │                              │
   ┌────────────▼───────────────┐   ┌──────────▼──────────────┐
   │  Aether — proxy Open-Meteo │   │  LibreHardwareMonitor    │
   │  FastAPI :8001             │   │  (Admin, port 8086)      │
   │  Météo-France AROME (FR)   │   │                          │
   │  /api/live_data            │   │  GET /data.json          │
   │  /api/config (city/lat/lon)│   │  → arbre JSON capteurs   │
   │  Interface web :8001       │   │  CPU/GPU/VRAM/Réseau     │
   └────────────┬───────────────┘   └──────────────────────────┘
                │
   ┌────────────▼───────────────────────────────────────────────┐
   │  Open-Meteo (HTTPS, sans clé API)                          │
   │  · Forecast API — météo + QAI + pollen (via Aether)        │
   │  · Geocoding API — ville → lat/lon (direct bridge)         │
   │  · Forecast API — prob. pluie % (direct bridge, fallback)  │
   └────────────────────────────────────────────────────────────┘

   ┌──────────────────────────────┐
   │  Navigateur Chromium         │
   │  SysViewExtension (MV3)      │
   │  content.js + background.js  │
   │  POST /v1/media              │
   └──────────────────────────────┘
```


---

## Contenu du dossier

```
SysView V6/
├── SysView.html              ← Wallpaper principal (HTML/CSS/JS vanilla)
├── project.json              ← Configuration Wallpaper Engine (propriétés UI)
├── preview.gif               ← Aperçu miniature
├── README.md                 ← Ce fichier (FR)
├── README_ENG.md             ← English version
│
├── API V3/
│   ├── SysViewBridge.pyw     ← Serveur FastAPI (bridge principal)
│   ├── config.py             ← Port + URLs LHM, Aether, Open-Meteo
│   ├── runtime_config.json   ← Config runtime persistée (créé automatiquement)
│   ├── install.bat           ← Installation en 1 clic (Python + paquets + démarrage auto)
│   ├── stop.bat              ← Arrêt du bridge
│   ├── uninstall.bat         ← Désinstallation complète
│   ├── logs/
│   │   └── sysview.log       ← Journal rotatif (10 Mo × 5 fichiers)
│   └── aether/               ← Proxy Open-Meteo (téléchargé par install.bat)
│       ├── main.py
│       ├── config.json       ← Ville + lat/lon + modèle météo
│       ├── requirements.txt
│       └── frontend/         ← Interface web Aether (http://127.0.0.1:8001)
│
└── SysViewExtension/
    ├── manifest.json         ← Manifest MV3 (Chromium)
    ├── content.js            ← Détection vidéos/audio sur toutes les pages
    ├── background.js         ← Service worker → envoie au bridge
    └── README.txt            ← Instructions d'installation de l'extension
```

---

## Installation

> **Ordre recommandé :** Wallpaper Engine → LHM → Bridge Python → Extension navigateur

---

### Étape 1 — Wallpaper Engine

1. Télécharger et dézipper le ZIP depuis GitHub
2. Ouvrir **Wallpaper Engine** → en bas de la bibliothèque, cliquer **"Ouvrir un fond d'écran"**
3. Sélectionner `SysView.html` depuis le dossier dézippé

WE crée automatiquement un dossier projet et y copie tous les fichiers :
```
...\wallpaper_engine\projects\myprojects\SysView V6\
```

> **Accéder au dossier projet** : clic droit sur SysView dans la bibliothèque → **"Ouvrir le dossier"**
> Toutes les étapes suivantes (bridge, extension) se font depuis ce dossier.

4. Dans **Paramètres WE → Général** :
   - Activer **"Autoriser l'accès réseau aux wallpapers web"** *(obligatoire)*

5. Dans le panneau **Personnaliser** du wallpaper, configurer les options ci-dessous :

---

### Étape 2 — LibreHardwareMonitor (LHM)

LHM fournit les températures CPU/GPU, les charges, la VRAM et les vitesses réseau
via une API HTTP locale. Il doit tourner en **Administrateur**.

**Télécharger LHM :**
- Page GitHub officielle : [github.com/LibreHardwareMonitor/LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/)
- Téléchargement direct v0.9.6 **recommandé :** [LibreHardwareMonitor.NET.10.zip](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.NET.10.zip)

**Installation :**

1. Dézipper l'archive dans un dossier permanent (ex: `C:\LHM\`)
2. Faire un **clic droit sur `LibreHardwareMonitor.exe` → Exécuter en tant qu'administrateur**
   > ⚠️ Les droits administrateur sont indispensables pour lire les capteurs matériels.
3. Dans le menu **Options** :
   - Cocher **Remote Web Server** → LHM écoute sur le port 8086
   - Cocher **Run On Windows Startup** *(recommandé)*
   - Cocher **Start Minimized** *(recommandé)*
4. Vérifier que le serveur fonctionne en ouvrant :
   **http://localhost:8086/data.json** → doit renvoyer un arbre JSON avec tous les capteurs

> **Remarque :** Si `data.json` répond avec une erreur ou est vide, LHM n'a pas
> été lancé en administrateur. Fermer et relancer en mode admin.

---

### Étape 3 — Bridge Python (API)

Le bridge est un serveur FastAPI léger qui tourne en arrière-plan,
interroge LHM et psutil, et expose les données au wallpaper sur le port 5001.
Il démarre également **Aether** (proxy Open-Meteo) en sous-processus sur le port 8001.

**Prérequis :** Python 3.10+ depuis [python.org](https://www.python.org/) *(pas le Microsoft Store)*

Depuis le **dossier projet WE** → ouvrir `API V3\` → **double-cliquer `install.bat`**

Le script fait tout automatiquement :
1. Vérifie Python — le télécharge automatiquement si absent
2. Installe les paquets bridge : `fastapi` `uvicorn[standard]` `requests` `psutil` `slowapi`
3. Clone Aether depuis GitHub et installe ses dépendances
4. Crée un raccourci de démarrage automatique dans `%APPDATA%\...\Startup\`
5. Lance le bridge immédiatement (qui lance Aether en sous-processus)

**Vérifier que tout fonctionne :**

Ouvrir dans le navigateur : [http://127.0.0.1:5001/v1/status](http://127.0.0.1:5001/v1/status)

```json
{
  "name": "SysView Bridge v5",
  "uptime": "2m 14s",
  "port": 5000,
  "modules": {
    "psutil":     "ok",
    "lhm":        "ok",
    "open_meteo": "ok"
  },
  "endpoints": {
    "health":  "ok",
    "perf":    "ok",
    "weather": "ok",
    "media":   "idle"
  },
  "extension": {
    "active":      false,
    "last_seen_s": null
  }
}
```

| Champ | Valeur attendue |
|-------|----------------|
| `modules.lhm` | `"ok"` — LHM tourne et répond sur le port 8086 |
| `modules.open_meteo` | `"ok"` — météo chargée (peut être `"pending"` les premières secondes) |

Interface web Aether (configuration météo, prévisions, QAI) : [http://127.0.0.1:8001](http://127.0.0.1:8001)

Autres scripts dans `API V3\` :

| Fichier | Action |
|---------|--------|
| `stop.bat` | Arrêter le bridge (lit `bridge.pid`) |
| `uninstall.bat` | Supprime les paquets Python + le raccourci de démarrage + le dossier Aether |

---

### Configuration du wallpaper

Dans le panneau **Personnaliser** du wallpaper :

#### Couleurs & Style

| Paramètre | Description |
|-----------|-------------|
| **Language** | FR / EN — bascule tous les textes instantanément |
| **Background image** | Image de fond JPG/PNG/GIF/WEBP (optionnel) |
| **UI Scale** | Échelle de l'interface — 100 = taille normale |
| **Opacity** | Opacité des panneaux, cartes et ombres (0 = invisible, 100 = plein) |
| **Bottom bar height** | Hauteur en px au-dessus de la barre des tâches Windows |
| **Accent color** | Couleur principale — bordures, barres, lueurs |
| **Secondary accent color** | Couleur secondaire plus claire — labels, reflets |
| **Background color** | Couleur de fond de base du wallpaper |
| **Text color** | Couleur du texte général |
| **Clock format** | Horloge 24h ou 12h (AM/PM) |
| **Temperature unit** | °C ou °F |
| **Show temperature decimal** | Affiche la décimale sur la température météo (ex : 15.6 au lieu de 16) |

#### Localisation & Météo

| Paramètre | Description |
|-----------|-------------|
| **Show city and country** | Afficher le nom de la ville sous l'horloge et dans le panneau météo |
| **Set weather location** | Activer pour saisir manuellement la ville |
| **City name** | Nom de la ville (ex : PARIS) — géocodée automatiquement via Open-Meteo |
| **Weather refresh interval** | Intervalle de rafraîchissement météo (1 à 15 min) |
| **Show weather source badge** | Afficher/cacher le badge "Open-Meteo" dans le panneau météo |

> Le bridge géocode la ville automatiquement (Open-Meteo Geocoding API) et configure Aether avec les coordonnées GPS résultantes. La configuration est persistée dans `runtime_config.json` — l'intervalle et la ville sont restaurés au redémarrage du bridge.

#### Seuils de température

| Paramètre | Description |
|-----------|-------------|
| **CPU — warning temp** | Seuil orange CPU (défaut : 80 °C) |
| **CPU — critical temp** | Seuil rouge CPU (défaut : 91 °C) |
| **GPU — warning temp** | Seuil orange GPU (défaut : 80 °C) |
| **GPU — critical temp** | Seuil rouge GPU (défaut : 95 °C) |

#### Panneaux à afficher

| Paramètre | Description |
|-----------|-------------|
| **Show Monitoring panel** | Panneau principal CPU/GPU/RAM/VRAM/Réseau |
| **Show CPU / GPU / VRAM / RAM / Network block** | Blocs individuels (nécessitent Show Monitoring) |
| **Network interface to display** | `Auto` (WiFi+Ethernet), `Ethernet`, `Wi-Fi` |
| **Show Storage panel** | Panneau disques C: à H: |
| **Show Disk C/D/E/F/G/H** | Lecteurs individuels |
| **Show free space on disks** | Afficher l'espace libre restant sur chaque lecteur |
| **Show Media player** | Barre du bas (miniature, progression, visualiseur audio) |
| **Show Weather panel** | Panneau météo côté droit |

---

### Étape 4 — Extension navigateur *(optionnel)*

L'extension permet d'afficher dans la barre média les vidéos lues dans le navigateur
(YouTube, Twitch, Netflix, Spotify Web, etc.) avec titre, artiste et miniature.

**Navigateurs compatibles :** Brave · Chrome · Edge · Opera · Vivaldi *(Chromium uniquement)*
Firefox non supporté.

**Sites supportés :**
YouTube (+ Shorts) · YouTube Music · Twitch · Netflix · Prime Video · Vimeo · Dailymotion · Plex · Emby · Jellyfin
· tout site avec une balise HTML5 `<video>` ou `<audio>`

**Installation :**

1. Ouvrir la page des extensions du navigateur :
   - Brave : `brave://extensions`
   - Chrome : `chrome://extensions`
   - Edge : `edge://extensions`
2. Activer le **Mode développeur** (toggle en haut à droite)
3. Cliquer **"Charger l'extension non empaquetée"**
4. Sélectionner le dossier `SysViewExtension\` depuis le **dossier projet WE**
   *(clic droit sur le wallpaper → "Ouvrir le dossier")*

**Vérifier :** Lancer une vidéo YouTube, puis ouvrir
[http://127.0.0.1:5001/v1/media](http://127.0.0.1:5001/v1/media) — titre et artiste doivent apparaître.

---

## Fonctionnalités détaillées

### Monitoring matériel

- Rafraîchissement toutes les **750 ms**
- Lissage **EMA α=0.60** sur CPU, GPU, RAM, VRAM (t₉₀ ≈ 1,9 s) → transitions fluides sans téléportation
- Lissage **EMA α=0.97** sur le réseau (très réactif aux pics)
- Transition CSS des barres : 0.75 s
- Couleurs de température : `< seuil_warn` → accent / `≥ seuil_warn` → orange / `≥ seuil_crit` → rouge
- Température et charge colorées **indépendamment**
- Températures monitoring toujours affichées en entier (°C/°F) — la décimale ne s'applique qu'à la météo

Sources des données :

| Métrique | Source principale | Fallback |
|----------|-------------------|----------|
| Temp CPU | LHM (ID 66) | — |
| Usage CPU | LHM (ID 73) | psutil |
| Temp GPU | LHM (ID 187) | — |
| Usage GPU | LHM (ID 193) | — |
| VRAM utilisée | LHM (ID 208) | — |
| VRAM totale | LHM (ID 210) | — |
| RAM % | LHM (ID 120) | psutil |
| Réseau WiFi | LHM (ID 513/514) RawValue B/s | — |
| Réseau Ethernet | LHM (ID 468/469) RawValue B/s | — |
| Réseau Auto | LHM WiFi + Ethernet | psutil |
| Disques | psutil (C: à H:) | — |

> **Mode réseau `auto`** : somme WiFi + Ethernet depuis LHM. Fallback psutil si LHM est hors ligne.

### Météo · Pollen · Qualité de l'air

- Source : **Open-Meteo** via **Aether** (gratuit, sans clé API)
- Modèle automatique selon la localisation (Météo-France AROME pour la France)
- Géocodage automatique : saisir le nom de la ville, le bridge récupère les coordonnées GPS
- Intervalle configurable de **1 à 15 minutes** dans WE
- Rafraîchissement immédiat à chaque changement de ville ou de coordonnées
- Retry automatique avec backoff exponentiel si la requête échoue
- Configuration runtime persistée dans `runtime_config.json` (survit au redémarrage du bridge)

| Indicateur | Affichage | Niveaux |
|------------|-----------|---------|
| Température | Valeur °C/°F (décimale optionnelle) | — |
| Probabilité de pluie | % (direct Open-Meteo best_match) | 0–100 % |
| Vent | km/h | — |
| Pollen | Label coloré + valeur grains/m³ | 🟢 Nul · 🟢 Faible · 🟡 Modéré · 🟠 Élevé · 🔴 Très élevé |
| QAI Europe | Indice 0–100 + label | 🟢 Bon · 🟡 Correct · 🟠 Modéré · 🔴 Mauvais · 🔴 Très mauvais |

> **Pollen** : somme graminées + bouleau + aulne + ambroisie en grains/m³ (Copernicus CAMS via Aether).  
> **Prob. pluie** : le modèle AROME ne fournit pas cette variable — le bridge fait un appel léger direct à Open-Meteo avec le modèle `best_match`.

### Panneau média

- **Extension Chrome uniquement** : YouTube (+ Shorts), Twitch, Netflix, Prime Video, tout site HTML5 vidéo/audio
- Priorité source : un onglet en pause ne peut pas remplacer un média déjà actif — mais est accepté si la barre est en veille (titre vide)
- Délai idle de 30 secondes : protège contre les faux positifs lors d'un seek ou d'un chargement
- Interpolation côté bridge : la position de lecture est recalculée en continu (pas de saut)
- Mises à jour même si le navigateur est minimisé ou en arrière-plan, tant qu'un média est en lecture

### Visualiseur audio

- Alimenté par **`wallpaperRegisterAudioListener`** de Wallpaper Engine
- Réagit à **toute la sortie audio du PC** (musique, jeux, vidéos, etc.)
- 24 barres mappant les bins de fréquence 0–63 (spectre LR du canal gauche)
- **EMA asymétrique** : montée α=0.80 (rapide) / descente α=0.18 (douce)
- Couleur violette fixe, indépendante des couleurs accent configurées dans WE
- Barres au minimum quand aucun son ne joue

---

## Endpoints de l'API

| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/v1/health` | État du bridge (uptime, version) |
| GET | `/v1/perf` | CPU · GPU · RAM · VRAM · Réseau · Disques |
| GET | `/v1/weather` | Météo · QAI · Pollen · Prob. pluie (cache Aether/Open-Meteo) |
| GET | `/v1/media` | Lecture en cours (titre · artiste · position · miniature) |
| GET | `/v1/status` | Diagnostic complet (modules · endpoints · extension) |
| POST | `/v1/config` | Reçoit la configuration depuis WE (ville · iface · intervalle) |
| POST | `/v1/media` | Reçoit les données de l'extension Chrome |
| GET | `/docs` | Documentation interactive FastAPI (Swagger UI) |

---

## Dépannage

| Problème | Solution |
|----------|----------|
| `modules.lhm = "offline"` dans /v1/status | LHM non lancé, ou lancé sans droits admin, ou Remote Web Server désactivé |
| CPU/GPU/temp à `—` | LHM → Options → Remote Web Server → vérifier port 8086 |
| Réseau à 0 en mode WiFi/Ethernet | Vérifier que LHM voit bien les capteurs réseau dans son interface |
| Réseau en mode Auto = 0 | LHM hors ligne → bascule automatique sur psutil |
| Météo `"pending"` au démarrage | Normal les premières secondes, retry automatique |
| Prob. pluie vide (`—`) | Aether encore en démarrage — attendre le premier refresh (1–3 min) |
| Ville non reconnue | Saisir uniquement le nom de la ville sans pays (ex: `PARIS` et non `PARIS, FR`) |
| Après redémarrage, mauvais intervalle | Vérifier que `runtime_config.json` existe dans `API V3\` |
| Panneau média vide | Installer l'extension Chrome et lancer une vidéo dans le navigateur |
| Extension : `active = false` | Extension non installée ou vidéo sur un onglet en arrière-plan |
| Wallpaper ne récupère aucune donnée | WE → Paramètres → Général → activer "Autoriser l'accès réseau" |
| Bridge ne démarre pas | Lire `API V3\logs\sysview.log` |
| Python non détecté | Installer depuis python.org *(pas le Microsoft Store)* |
| port 5001 déjà utilisé | Lancer `stop.bat` puis relancer `install.bat` |
| Aether inaccessible (:8001) | Relancer `install.bat` ou vérifier les logs |

---

## Désinstallation

- **LHM :** fermer l'application, supprimer le dossier
- **Bridge :** `API V3\uninstall.bat` (arrête le bridge + supprime les paquets Python + le dossier `aether\` + raccourci démarrage)
- **Extension :** page extensions du navigateur → SysView Media Bridge → Supprimer
- **Wallpaper Engine :** clic droit sur SysView → Supprimer

---

## Technologies utilisées

### Frontend — `SysView.html`

| Technologie | Usage |
|-------------|-------|
| **HTML/CSS/JS vanilla** | Aucun framework — compatible avec le renderer Chromium embarqué de WE |
| **CSS Custom Properties** | Variables `--ac` `--ac2` `--bg` `--tx` `--pa` `--sz` modifiées en temps réel par WE |
| **CSS transform + transition** | Animation des barres de monitoring et du visualiseur audio |
| **`wallpaperPropertyListener`** | API WE — reçoit les changements de propriétés de l'utilisateur |
| **`wallpaperRegisterAudioListener`** | API WE — reçoit 128 bins de fréquence audio à ~30 fps |
| **EMA (Exponential Moving Average)** | Lissage des métriques hardware — α=0.60 / α=0.97 réseau |
| **fetch() + setInterval** | Polling périodique des endpoints bridge (750ms perf, 500ms media) |
| **Debounce 300ms / 500ms** | Regroupe les appels POST /v1/config et les recherches de ville |

### Backend — `SysViewBridge.pyw`

| Technologie | Usage |
|-------------|-------|
| **FastAPI** | Framework ASGI — routing, validation, docs Swagger auto |
| **Uvicorn** | Serveur ASGI — boucle asyncio, gestion des connexions |
| **slowapi** | Rate limiting — 350/min sur les endpoints de poll (perf · weather · media · health), 60/min sur status · config |
| **psutil** | CPU %, RAM %, disques, réseau (fallback si LHM absent) |
| **requests** | HTTP vers LHM (8085), Aether (8000), Open-Meteo Geocoding, Open-Meteo Forecast |
| **threading** | 3 threads démons : hardware\_loop, disk\_loop, weather\_loop |
| **concurrent.futures** | Isole `psutil.disk_partitions()` dans un thread dédié avec timeout 5 s |
| **threading.Lock** | `perf_lock` `weather_lock` `media_lock` `runtime_lock` — accès thread-safe |
| **threading.Event** | `_weather_event` — réveille weather\_loop immédiatement si la ville/intervalle change |
| **json + pathlib** | Persistance de la config runtime (`runtime_config.json`) |
| **RotatingFileHandler** | Log rotatif 10 Mo × 5 fichiers dans `logs/sysview.log` |
| **CORS + Private Network** | Headers nécessaires pour le renderer Chromium de WE |

### Aether — proxy Open-Meteo

| Concept | Détail |
|---------|--------|
| **FastAPI :8001** | Proxy Open-Meteo avec interface web d'administration |
| **Modèle auto** | Sélection automatique du meilleur modèle selon les coordonnées (AROME pour FR) |
| **POST /api/config** | Reçoit ville + lat/lon depuis le bridge — synchronise l'affichage web |
| **GET /api/live_data** | Agrège météo courante + qualité de l'air + pollen |
| **config.json** | Ville, coordonnées, modèle météo, paramètres — persistés sur disque |

### Hardware — LibreHardwareMonitor

| Concept | Détail |
|---------|--------|
| **API HTTP locale** | `GET /data.json` → arbre JSON de tous les capteurs |
| **IDs stables** | Chaque capteur a un ID numérique stable entre les sessions |
| **RawValue vs Value** | `RawValue` = valeur brute en B/s (réseau) — `Value` est auto-scalé par LHM (KB/MB/GB) |
| **Droits admin** | Obligatoires pour accéder aux capteurs kernel (PMU, SMBus, WMI) |
| **port 8086** | Configurable dans Options → Remote Web Server |

### Extension Chrome — `SysViewExtension/`

| Fichier | Rôle |
|---------|------|
| **manifest.json** | MV3, permissions `<all_urls>` + `http://127.0.0.1:5001/*` |
| **content.js** | Injecté dans toutes les pages — détecte `<video>`, `MediaSession API`, extrait titre/artiste/miniature |
| **background.js** | Service worker — reçoit les messages de content.js, POST vers `/v1/media` |

### Flux de données

```
1. HARDWARE (750ms)
   LHM /data.json → _lhm_parse() → IDs 66/73/120/187/193/208/210/468/469/513/514
   psutil          → CPU% / RAM / net_io_counters / disk_usage
                    ↓ sélection iface (auto/eth/wifi)
                    ↓ EMA α=0.60 (α=0.97 réseau)
                    → PERF dict (thread-safe)
                    ← GET /v1/perf (HTML, 750ms)
                    → renderBars() + couleurs seuils

2. MÉTÉO (configurable 1–15 min)
   Bridge → Aether /api/live_data
     → Open-Meteo (Météo-France AROME pour FR) : temp / précip / vent / code
     → Open-Meteo (CAMS) : QAI européen / pollen grains/m³
   Bridge → Open-Meteo Geocoding API (nom ville → lat/lon + nom normalisé)
   Bridge → Open-Meteo Forecast API (prob. pluie % — fallback si AROME null)
   Bridge → Aether POST /api/config (lat/lon + nom ville → sync interface Aether)
   → runtime_config.json (intervalle + ville + lat/lon persistés)
   → WEATHER dict
   ← GET /v1/weather (HTML)
   → renderWeather()

3. MÉDIA — Extension Chrome
   content.js (1s) → background.js → POST /v1/media
   → MEDIA dict
   Priorité : nouveau titre accepté uniquement si playing=True

4. AFFICHAGE MÉDIA
   HTML pollMediaBridge (500ms) ← GET /v1/media
   bridge renvoie position interpolée (time.time() - last_update)
   → renderMediaProgress() transition CSS 1s linear

5. CONFIGURATION
   WE applyUserProperties → debounce 300ms → POST /v1/config
   → RUNTIME dict → hardware_loop / weather_loop lisent à chaque cycle
   → runtime_config.json (sauvegarde automatique à chaque changement)

6. VISUALISEUR AUDIO
   WE wallpaperRegisterAudioListener → audioArray[128] à ~30fps
   → bins 0-63 mappés sur 24 barres (peak max par groupe)
   → EMA asymétrique : montée α=0.80 / descente α=0.18
   → CSS transform scaleY(0.04 … 0.90)
```

---

*SysView V6 — Windows 10 / 11 x64*
*GitHub : [github.com/Mrtt555/sysview-wallpaper-engine](https://github.com/Mrtt555/sysview-wallpaper-engine)*
