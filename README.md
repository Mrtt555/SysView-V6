# SysView V6 — Wallpaper Engine System Monitor

Fond d'écran interactif pour **Wallpaper Engine** affichant en temps réel :  
**CPU · GPU · RAM · VRAM · Réseau · Stockage · Météo · Pollen · Qualité de l'air · Média · Visualiseur audio**

Windows 10 / 11 x64 — Wallpaper Engine requis — aucune dépendance Python

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                       WALLPAPER ENGINE                           │
│   index.html  (Chromium isolé — accès réseau local autorisé)     │
│   src/  (ES modules : app.js · DataWorker · composants)          │
│                                                                  │
│   Horloge · Date · Ville    CPU/GPU/RAM/VRAM    Météo/QAI/Pollen │
│   Stockage C:→H:            Réseau              Visualiseur audio│
│                   Média (titre · miniature · barre)              │
│                                                                  │
│   GET /v1/perf     (500 ms)    GET /v1/weather                   │
│   GET /v1/media    (500 ms)    POST /v1/config  (debounce 300 ms)│
└────────────────────────┬─────────────────────────────────────────┘
                         │ HTTP 127.0.0.1:5001
┌────────────────────────▼─────────────────────────────────────────┐
│                SysViewManager.exe  (Admin — tâche planifiée)     │
│                                                                  │
│  ┌──────────────────┐  ┌─────────────────────────────────────┐   │
│  │  BridgeServer    │  │  HardwareService (LHM)              │   │
│  │  ASP.NET Core    │  │  CPU · GPU · RAM · Réseau           │   │
│  │  :5001           │  │  Poll 500 ms → Hardware.json        │   │
│  └──────────────────┘  └─────────────────────────────────────┘   │
│  ┌──────────────────┐  ┌─────────────────────────────────────┐   │
│  │  WeatherService  │  │  DiskService                        │   │
│  │  Open-Meteo      │  │  DriveInfo (C:→H:)                  │   │
│  │  direct (HTTPS)  │  └─────────────────────────────────────┘   │
│  │  → Weather.json  │  ┌─────────────────────────────────────┐   │
│  └──────────────────┘  │  SmtcService                        │   │
│  ┌──────────────────┐  │  Windows Media Transport Controls   │   │
│  │  TmdbService     │  │  Musique · Vidéo · Navigateurs      │   │
│  │  TMDB poster     │  └─────────────────────────────────────┘   │
│  │  fallback        │  ┌─────────────────────────────────────┐   │
│  └──────────────────┘  │  RuntimeConfig                      │   │
│                        │  runtime_config.json (AppData)      │   │
│                        └─────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
         ▲                               ▲
         │ SMTC (Windows natif)          │ HTTPS (Open-Meteo / TMDB)
┌────────┴───────────────┐    ┌──────────┴──────────────────────┐
│  Spotify · YouTube     │    │  api.open-meteo.com             │
│  VLC · Groove · etc.   │    │  air-quality-api.open-meteo.com │
│  (tout lecteur Windows)│    │  api.themoviedb.org (posters)   │
└────────────────────────┘    └─────────────────────────────────┘

Données persistées : %AppData%\SysViewManager\
  ├── Hardware.json        (maj. 500 ms)
  ├── Weather.json         (maj. toutes les 10 min par défaut)
  └── runtime_config.json  (ville · lat/lon · modèle · intervalle)
```

---

## Contenu du projet

```
SysView V6/
├── index.html                    ← Wallpaper principal (point d'entrée WE)
├── project.json                  ← Propriétés Wallpaper Engine (panneau Personnaliser)
├── manifest.json                 ← Manifest web (icône, nom)
├── preview.gif                   ← Aperçu miniature dans la bibliothèque WE
├── README.md                     ← Ce fichier
│
├── src/                          ← Source web (ES modules)
│   ├── app.js                      Orchestrateur Alpine.js · Web Worker · rAF
│   ├── style.css                   Styles (Tailwind extend + custom)
│   ├── tailwind.config.js          Config Tailwind (mirroir CDN)
│   ├── core/
│   │   ├── DataWorker.js             Web Worker : fetch API · LERP ~30 fps
│   │   ├── ThemeManager.js           WE property listener · variables CSS
│   │   └── WallpaperAPI.js           Helpers API Wallpaper Engine
│   ├── components/
│   │   ├── MonitoringWidget.js       Rendu CPU/GPU/RAM/VRAM/Réseau
│   │   ├── MediaWidget.js            Barre média · album art · progression
│   │   ├── WeatherWidget.js          HTML météo (WMO · QAI · pollen)
│   │   └── ClockWidget.js            Horloge · date
│   └── assets/icons/
│       └── favicon.svg
│
├── SysViewManager/               ← Application C# .NET 8 (source)
│   ├── Program.cs                  Point d'entrée · tâche planifiée · AppData
│   ├── BridgeServer.cs             ASP.NET Core Minimal API :5001
│   ├── HardwareService.cs          LHM poll 500 ms · export Hardware.json
│   ├── WeatherService.cs           Open-Meteo direct · export Weather.json
│   ├── DiskService.cs              Disques via DriveInfo
│   ├── SmtcService.cs              SMTC natif Windows (tout lecteur)
│   ├── TmdbService.cs              Fallback pochette via TMDB API
│   ├── MediaState.cs               État du lecteur média
│   ├── Logger.cs                   Journalisation structurée
│   ├── RuntimeConfig.cs            Config persistée dans AppData
│   ├── TrayApp.cs                  Icône tray WinForms · toggle auto-start
│   ├── SysViewManager.csproj
│   ├── app.manifest                requireAdministrator (LHM)
│   └── app.ico
│
├── installer/
│   └── setup.iss                 ← Installeur Inno Setup (SysViewV6_Setup.exe)
│
├── scripts/
│   ├── publish.ps1               ← Build Release + signature de code
│   └── compile.bat               ← Raccourci : lance publish.ps1 -Kill -Version
│
└── .github/workflows/
    └── release.yml               ← CI/CD : build + GitHub Release automatique
```

---

## Installation

### Option A — Installeur (recommandé)

1. Télécharger **`SysViewV6_Setup.exe`** depuis les [Releases GitHub](https://github.com/Mrtt555/SysView-V6/releases/latest)
2. Lancer en tant qu'**administrateur**
3. L'installeur effectue automatiquement :

| Étape | Action |
|-------|--------|
| **A** | Géocodage de votre ville (Open-Meteo) → lat/lon |
| **B** | Téléchargement du wallpaper SysView V6 depuis GitHub |
| **C** | Téléchargement de `SysViewManager.exe` (pré-compilé GitHub Releases) |
| **D** | Écriture de `runtime_config.json` dans `%AppData%\SysViewManager\` |
| **E** | Tâche planifiée ONLOGON / HIGHEST + lancement immédiat |

> Le dossier Wallpaper Engine est détecté automatiquement depuis le registre Steam.  
> Si aucun release GitHub n'est disponible, l'installeur compile depuis les sources (nécessite .NET 8 SDK, ~2 min).

---

### Option B — Manuel

1. Cloner ou télécharger le dépôt dans le dossier `myprojects` de Wallpaper Engine
2. Compiler `SysViewManager.exe` :
   ```
   dotnet publish SysViewManager/SysViewManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
   ```
   Ou double-cliquer **`scripts\compile.bat`** (ajuster la version dans le fichier).
3. Lancer `SysViewManager.exe` **en tant qu'administrateur** — il crée automatiquement sa tâche planifiée au premier lancement

---

### Vérification

Ouvrir : [http://127.0.0.1:5001/v1/status](http://127.0.0.1:5001/v1/status)

```json
{
  "name":   "SysView Bridge v6",
  "uptime": "1m 30s",
  "port":   5001,
  "modules": {
    "lhm":        "ok",
    "weather":    "ok",
    "model":      "meteofrance_arome_france",
    "model_name": "Météo-France AROME France"
  },
  "endpoints": {
    "health":  "ok",
    "perf":    "ok",
    "weather": "ok",
    "media":   "idle"
  }
}
```

| Champ | Valeur attendue |
|-------|----------------|
| `modules.lhm` | `"ok"` — LHM actif (capteurs admin requis) |
| `modules.weather` | `"ok"` — première météo chargée (peut être `"pending"` les premières secondes) |

---

### Wallpaper Engine — paramètre réseau

**WE → Paramètres → Général** : activer **"Autoriser l'accès réseau aux wallpapers web"** *(obligatoire)*.

---

## Médias via SMTC

SysView V6 utilise **Windows Media Transport Controls (SMTC)** — l'API média native de Windows — pour afficher le titre, l'artiste et la miniature du contenu en cours de lecture, **sans extension navigateur**.

Tout lecteur qui expose ses métadonnées à Windows est supporté :

| Application | Support |
|-------------|---------|
| Spotify | ✅ Titre · artiste · pochette |
| YouTube Music (PWA) | ✅ Titre · artiste · pochette |
| VLC · Groove Music | ✅ Titre · artiste |
| Navigateurs (Chrome/Edge) | ✅ Via l'API `MediaSession` HTML5 |
| Films & TV Windows | ✅ |

> **Fallback pochette :** si SMTC ne fournit pas d'image, SysViewManager interroge l'API TMDB pour récupérer le poster correspondant.

---

## API locale

Tous les endpoints sont servis sur `http://127.0.0.1:5001`.

| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/v1/health` | État du bridge (status, version) |
| GET | `/v1/perf` | CPU · GPU · RAM · VRAM · Réseau · Disques |
| GET | `/v1/weather` | Météo courante · QAI · Pollen · Prévisions 48 h |
| GET | `/v1/media` | Lecture en cours (titre · artiste · position · miniature) |
| GET | `/v1/models` | Liste des modèles météo disponibles |
| GET | `/v1/status` | Diagnostic complet (modules · endpoints) |
| POST | `/v1/config` | Configuration (ville · modèle météo · interface réseau · intervalle) |

**Rate limiting :** 350 req/min sur les endpoints de poll · 60 req/min sur `/v1/status` et `/v1/config`.

---

## Données exportées

### `Hardware.json` — mis à jour toutes les **500 ms**

```json
{
  "timestamp": "2026-06-10T12:00:00Z",
  "lhm_online": true,
  "cpu":  { "name": "AMD Ryzen 9 7900X", "usage": 12.4, "temp": 52.1 },
  "gpu":  { "name": "NVIDIA RTX 4080",   "usage": 8.0,  "temp": 45.0,
            "vram_used_mb": 2048, "vram_total_mb": 16384 },
  "ram":  { "usage": 38.5, "used_mb": 12432, "total_mb": 32768 },
  "network": {
    "download_kb": 245.8, "upload_kb": 12.1,
    "wifi_dl_kb":  245.8, "wifi_ul_kb": 12.1,
    "eth_dl_kb":   0.0,   "eth_ul_kb":  0.0
  },
  "disks": {
    "c": { "used_gb": 312.5, "total_gb": 953.9, "free_gb": 641.4, "percent": 32.8 }
  }
}
```

### `Weather.json` — mis à jour selon l'intervalle configuré (défaut 10 min)

```json
{
  "timestamp":          "2026-06-10T12:00:00Z",
  "temp":               18.4,
  "feels_like":         16.9,
  "humidity":           72,
  "uv":                 3.2,
  "precip":             0.0,
  "precip_prob":        15,
  "wind":               22.0,
  "wind_gusts":         38.0,
  "wind_dir":           270,
  "weather_code":       1,
  "cloud_cover":        25,
  "aqi":                18,
  "aqi_label":          "Bon",
  "pollen":             12.5,
  "pollen_label":       "Faible",
  "pm10":               14,
  "pm25":               8,
  "weather_model":      "meteofrance_arome_france",
  "weather_model_name": "Météo-France AROME France",
  "forecast":           { "hourly": { "..." : "..." } }
}
```

### `runtime_config.json` — configuration persistée

```json
{
  "lat":                  48.8566,
  "lon":                  2.3522,
  "city":                 "Paris",
  "weather_interval_min": 10,
  "network_iface":        "auto",
  "weather_model":        "best_match"
}
```

---

## Modèles météo

Configurable via `POST /v1/config` (`weather_model`) ou le panneau WE.

| ID | Nom | Fournisseur | Zone |
|----|-----|-------------|------|
| `best_match` *(défaut)* | Sélection automatique | Open-Meteo (ECMWF · DWD · Météo-France · NOAA…) | Mondial |
| `meteofrance_arome_france` | AROME France (1,3 km) | Météo-France | France métropolitaine |
| `meteofrance_seamless` | ARPEGE + AROME | Météo-France | Europe / France |
| `ecmwf_ifs025` | IFS | ECMWF | Mondial |
| `dwd_icon_seamless` | ICON | DWD | Europe centrale |
| `dwd_icon_eu` | ICON-EU (7 km) | DWD | Europe |
| `gfs_seamless` | GFS | NOAA | Mondial |
| `ukmo_seamless` | Met Office | Met Office | Europe NW |

> **Résolution automatique :** si `best_match` est sélectionné et que la position est dans la France métropolitaine (41,3°–51,1°N / -5,2°–9,6°E), `meteofrance_arome_france` est utilisé automatiquement.

---

## Icône tray

SysViewManager apparaît dans la barre système avec un cercle coloré :

| Couleur | Signification |
|---------|--------------|
| 🟢 Vert | LHM **et** météo actifs |
| 🟠 Orange | L'un des deux en attente |
| 🔴 Rouge | Les deux inactifs |

**Menu contextuel :**
- Statut LHM (CPU · température en direct)
- Statut météo (température · vent · modèle)
- Actualiser la météo manuellement
- ⚡ Toggle démarrage automatique (tâche planifiée)
- 📁 Ouvrir le dossier `%AppData%\SysViewManager\`
- 🌐 Bridge API `/docs`
- ✕ Quitter

---

## Configuration Wallpaper Engine

Dans le panneau **Personnaliser** de Wallpaper Engine :

### Localisation & Météo

| Paramètre | Description |
|-----------|-------------|
| **Set weather location** | Activer pour saisir manuellement la ville |
| **City name** | Nom de la ville — géocodée automatiquement (lat/lon sauvegardés) |
| **Weather refresh interval** | Intervalle de rafraîchissement 1–15 min |
| **Temperature unit** | °C ou °F |
| **Show weather source badge** | Afficher/masquer le badge Open-Meteo |

### Affichage

| Paramètre | Description |
|-----------|-------------|
| **Language** | FR / EN |
| **Background image** | Image JPG/PNG/GIF/WEBP optionnelle |
| **UI Scale** | Échelle globale de l'interface |
| **Accent / Secondary / Background / Text color** | Thème couleur |
| **Clock format** | 24h ou 12h |
| **Show temperature decimal** | Ex : 15,6 °C au lieu de 16 °C |
| **Bottom bar height** | Marge au-dessus de la barre des tâches Windows |

### Panneaux

| Paramètre | Description |
|-----------|-------------|
| **Show Monitoring panel** | CPU / GPU / RAM / VRAM / Réseau |
| **Network interface** | Auto · Wi-Fi · Ethernet |
| **Show Storage panel** | Disques C: → H: |
| **Show free space on disks** | Espace libre restant |
| **Show Media player** | Barre du bas (miniature · progression · visualiseur) |
| **Show Weather panel** | Panneau météo |

### Seuils de température

| Capteur | Warning (orange) | Critical (rouge) |
|---------|-----------------|-----------------|
| CPU | 80 °C | 91 °C |
| GPU | 80 °C | 95 °C |

---

## Démarrage automatique

SysViewManager gère lui-même sa tâche planifiée Windows :

- **Au premier lancement :** la tâche `SysViewManager` est créée automatiquement (`ONLOGON / HIGHEST`)
- **Depuis le tray :** menu → ⚡ *Démarrage auto* pour activer / désactiver
- **Instance unique :** un mutex global empêche plusieurs instances simultanées

```
Planificateur de tâches → SysViewManager
  Déclencheur : À l'ouverture de session (ONLOGON)
  Niveau :      Le plus élevé (HIGHEST — requis pour LHM)
```

---

## Dépannage

| Problème | Solution |
|----------|----------|
| `modules.lhm = "offline"` | SysViewManager non lancé en admin — vérifier la tâche planifiée ou relancer manuellement |
| CPU / GPU / températures à `—` | LHM hors ligne (voir ci-dessus) |
| `modules.weather = "pending"` | Normal les 3–5 premières secondes — attendre le premier cycle |
| Météo jamais chargée | Vérifier la connexion Internet · tester `https://api.open-meteo.com/v1/forecast?latitude=48.85&longitude=2.35&current=temperature_2m` |
| Ville non reconnue | Saisir le nom seul sans code pays (`Paris` pas `Paris, FR`) |
| Réseau toujours à 0 | LHM hors ligne — fallback `NetworkInterface` actif — vérifier `Hardware.json` |
| Panneau média vide | Vérifier que le lecteur expose ses métadonnées Windows (SMTC) · tester via Spotify ou Groove Music |
| RAM / VRAM affichent `— Go` | Normal sans bridge — SysViewManager non démarré |
| Wallpaper ne reçoit aucune donnée | WE → Paramètres → Général → activer *"Autoriser l'accès réseau aux wallpapers web"* |
| Port 5001 déjà occupé | Tray → ✕ Quitter · tuer le processus occupant 5001 · relancer |
| Plusieurs icônes tray | Un mutex bloque la 2e instance — vérifier le Gestionnaire des tâches |

---

## Technologies

### SysViewManager.exe

| Composant | Technologie |
|-----------|-------------|
| Application | C# .NET 8 · WinForms (`ApplicationContext`) · Single-file self-contained |
| Bridge HTTP | ASP.NET Core Minimal API · Kestrel `127.0.0.1:5001` |
| Capteurs matériel | **LibreHardwareMonitor 0.9.6** (`IVisitor · IHardware · ISensor`) |
| Média natif | **SMTC** (`Windows.Media.Control`) — tout lecteur Windows |
| Pochette fallback | **TMDB API** — recherche par titre/artiste |
| Météo | **Open-Meteo** (Forecast · Air Quality · Geocoding) — 3 appels `Task.WhenAll` |
| Modèle météo auto | France bbox → `meteofrance_arome_france`, sinon `best_match` |
| Config persistée | `System.Text.Json` · `%AppData%\SysViewManager\runtime_config.json` |
| Auto-start | `schtasks.exe /create /sc ONLOGON /rl HIGHEST` |
| Exports JSON | `Hardware.json` (500 ms) · `Weather.json` (intervalle configurable) |
| CORS | `null` (WE renderer) · `127.0.0.1` |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` — 350/min poll · 60/min config |

### Front-end (index.html + src/)

| Technologie | Usage |
|-------------|-------|
| ES Modules (`type="module"`) | Architecture `src/core/` + `src/components/` |
| **Alpine.js v3** | État réactif (show/hide · labels · météo · média) |
| **Tailwind CSS v3 CDN** | Classes utilitaires · thème via CSS custom properties |
| **Web Worker** (`DataWorker.js`) | Fetch API + LERP ~30 fps dans thread séparé |
| `requestAnimationFrame` | Rendu DOM haute fréquence (barres · températures) |
| CSS Custom Properties | `--p --s --bg --tx` modifiées en temps réel par WE |
| `wallpaperPropertyListener` | API WE — reçoit les changements utilisateur |
| `wallpaperRegisterAudioListener` | API WE — 128 bins audio (~30 fps) → visualiseur |
| LERP exponentiel | `f = 1 - exp(-k·dt)` dans le Worker · lissage CPU/GPU/RAM/Réseau |

---

## CI/CD

Le workflow `.github/workflows/release.yml` se déclenche sur un tag `vX.Y.Z` :

1. Checkout sur `windows-latest`
2. `dotnet publish` — `win-x64 · self-contained · PublishSingleFile`
3. Publication automatique du **GitHub Release** avec `SysViewManager.exe` en pièce jointe

L'installeur `SysViewV6_Setup.exe` télécharge cet exe directement depuis le Release — **il n'est pas nécessaire de cloner le dépôt pour l'utiliser**.

---

*SysView V6 — Windows 10 / 11 x64*  
*[github.com/Mrtt555/SysView-V6](https://github.com/Mrtt555/SysView-V6)*
