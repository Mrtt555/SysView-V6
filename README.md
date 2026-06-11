# SysView V6 — Wallpaper Engine System Monitor

Fond d'écran interactif pour **Wallpaper Engine** affichant en temps réel :  
**CPU · GPU · RAM · VRAM · Réseau · Stockage · Météo · Pollen · Qualité de l'air · Média · Visualiseur audio**

Windows 10 / 11 x64 — Wallpaper Engine requis — aucune dépendance Python

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                       WALLPAPER ENGINE                           │
│   SysView.html  (Chromium isolé — accès réseau local autorisé)   │
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
│  └──────────────────┘  │  MediaState                         │   │
│  ┌──────────────────┐  │  Source unique : extension navigateur│   │
│  │  RuntimeConfig   │  └─────────────────────────────────────┘   │
│  │  AppData JSON    │                                            │
│  └──────────────────┘                                            │
└──────────────────────────────────────────────────────────────────┘
         ▲                               ▲
         │ POST /v1/media (500 ms)       │ HTTPS (Open-Meteo)
┌────────┴───────────────────────┐  ┌───┴─────────────────────────┐
│  Extension Chrome / Edge       │  │  api.open-meteo.com         │
│  "SysView Media Bridge" (MV3)  │  │  air-quality-api.open-meteo │
│  content-main.js  (world MAIN) │  └─────────────────────────────┘
│    └─ lit navigator.mediaSession
│  content.js  (world ISOLATED)  │
│    └─ lit <video> · pousse API │
│  background.js  (service worker│
│    └─ relaie vers :5001        │
└────────────────────────────────┘
  Netflix · Disney+ · Prime Video
  YouTube · Spotify · Twitch · etc.

Données persistées : %AppData%\SysViewManager\
  ├── Hardware.json        (maj. 500 ms)
  ├── Weather.json         (maj. toutes les 10 min par défaut)
  └── runtime_config.json  (ville · lat/lon · modèle · intervalle)
```

---

## Contenu du projet

```
myprojects/
├── README.md
│
├── SysView V6/                   ← Wallpaper (chargé par Wallpaper Engine)
│   ├── SysView.html                Point d'entrée WE
│   ├── project.json                Propriétés Wallpaper Engine (panneau Personnaliser)
│   ├── manifest.json               Manifest web (icône, nom)
│   ├── preview.gif                 Aperçu miniature dans la bibliothèque WE
│   │
│   └── src/                      ← Source web (ES modules)
│       ├── app.js                    Orchestrateur Alpine.js · Web Worker · rAF
│       ├── style.css                 Styles (Tailwind extend + custom)
│       ├── tailwind.config.js        Config Tailwind (mirroir CDN)
│       ├── core/
│       │   ├── DataWorker.js           Web Worker : fetch API · LERP ~30 fps
│       │   ├── ThemeManager.js         WE property listener · variables CSS
│       │   └── WallpaperAPI.js         Helpers API Wallpaper Engine
│       ├── components/
│       │   ├── MonitoringWidget.js     Rendu CPU/GPU/RAM/VRAM/Réseau
│       │   ├── MediaWidget.js          Barre média · album art · progression
│       │   ├── WeatherWidget.js        HTML météo (WMO · QAI · pollen)
│       │   └── ClockWidget.js          Horloge · date
│       └── assets/icons/
│           └── favicon.svg
│
├── SysViewManager/               ← Application C# .NET 8 (source)
│   ├── Program.cs                  Point d'entrée · tâche planifiée · AppData
│   ├── BridgeServer.cs             ASP.NET Core Minimal API :5001
│   ├── HardwareService.cs          LHM poll 500 ms · export Hardware.json
│   ├── WeatherService.cs           Open-Meteo direct · export Weather.json
│   ├── DiskService.cs              Disques via DriveInfo
│   ├── MediaState.cs               État du lecteur média (source : extension)
│   ├── Logger.cs                   Journalisation structurée
│   ├── RuntimeConfig.cs            Config persistée dans AppData
│   ├── TrayApp.cs                  Icône tray WinForms · toggle auto-start
│   ├── SysViewManager.csproj
│   ├── app.manifest                requireAdministrator (LHM)
│   ├── app.ico
│   │
│   └── browser-ext/              ← Extension Chrome/Edge (MV3)
│       ├── manifest.json             Déclaration extension (permissions, scripts)
│       ├── background.js             Service worker : reçoit et relaie vers :5001
│       ├── content-main.js           World MAIN : lit navigator.mediaSession
│       ├── content.js                World ISOLATED : lit <video> · envoie message
│       ├── setup-ext.ps1             Script d'installation de l'extension
│       └── icons/                    Icônes 16 · 48 · 128 px
│
├── SysViewManager.exe            ← Binaire compilé (prêt à l'emploi)
│
├── installer/
│   └── setup.iss                 ← Installeur Inno Setup (SysViewV6_Setup.exe)
│
├── scripts/
│   ├── publish.ps1               ← Build Release + signature de code
│   ├── compile.bat               ← Raccourci : lance publish.ps1 -Kill -Version
│   └── version.txt               ← Version courante
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
| **F** | Installation de l'extension **SysView Media Bridge** dans Chrome/Edge |

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
4. Installer l'extension navigateur manuellement (voir section ci-dessous)

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
| `endpoints.media` | `"idle"` au repos · titre + service quand un lecteur est actif |

---

### Wallpaper Engine — paramètre réseau

**WE → Paramètres → Général** : activer **"Autoriser l'accès réseau aux wallpapers web"** *(obligatoire)*.

---

## Médias via extension navigateur

SysView V6 utilise l'extension **SysView Media Bridge** (Chrome/Edge, MV3) pour afficher le titre, l'artiste, la miniature et la progression du contenu en cours de lecture dans le navigateur.

### Comment ça fonctionne

| Script | World | Rôle |
|--------|-------|------|
| `content-main.js` | **MAIN** | Lit `navigator.mediaSession.metadata` + état de lecture |
| `content.js` | **ISOLATED** | Lit l'élément `<video>` (position, durée) · envoie vers le service worker |
| `background.js` | Service Worker | Relaie les données vers `POST /v1/media` (127.0.0.1:5001) |

> Les deux scripts `content` coexistent sur chaque onglet. Le monde MAIN accède aux objets JavaScript de la page (mediaSession) ; le monde ISOLATED accède au DOM et à l'API Chrome.

### Services supportés

| Plateforme | Titre | Artiste | Image | Position |
|------------|-------|---------|-------|----------|
| Netflix | ✅ DOM | — | ✅ CDN | ✅ |
| Disney+ | ✅ mediaSession | — | ✅ mediaSession | ✅ |
| Prime Video | ✅ DOM | — | ✅ CDN | ✅ |
| YouTube | ✅ mediaSession | ✅ | ✅ | ✅ |
| Spotify Web | ✅ mediaSession | ✅ | ✅ | ✅ |
| Twitch | ✅ mediaSession | ✅ | ✅ | ✅ |
| Crunchyroll | ✅ mediaSession | — | ✅ | ✅ |
| Tout lecteur HTML5 | ✅ document.title | — | — | ✅ |

> **DRM (Netflix, Disney+, Prime Video)** : la durée de la vidéo peut être masquée par le DRM. La position est toujours disponible via `video.currentTime`.

### Installation manuelle de l'extension

1. Ouvrir Chrome/Edge → `chrome://extensions` (ou `edge://extensions`)
2. Activer le **mode développeur**
3. Cliquer **Charger l'extension non empaquetée**
4. Sélectionner le dossier `SysViewManager/browser-ext/`

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
| POST | `/v1/media` | Réception des données de l'extension navigateur |

**Rate limiting :** 350 req/min sur les endpoints de poll · 60 req/min sur `/v1/status` et `/v1/config`.

---

## Données exportées

### `Hardware.json` — mis à jour toutes les **500 ms**

```json
{
  "timestamp": "2026-06-11T12:00:00Z",
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
  "timestamp":          "2026-06-11T12:00:00Z",
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
| Panneau média vide | Vérifier que l'extension est installée et activée · ouvrir une page de lecture · vérifier `endpoints.media` dans `/v1/status` |
| Titre/image incorrects | Certaines plateformes nécessitent un moment de lecture (mediaSession peuplé après lecture) |
| Extension sans effet | Vérifier que le mode développeur est activé dans `chrome://extensions` et que le service worker est actif |
| RAM / VRAM affichent `— Go` | SysViewManager non démarré |
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
| Média | **Extension navigateur** (MV3) via `POST /v1/media` |
| Météo | **Open-Meteo** (Forecast · Air Quality · Geocoding) — 3 appels `Task.WhenAll` |
| Modèle météo auto | France bbox → `meteofrance_arome_france`, sinon `best_match` |
| Config persistée | `System.Text.Json` · `%AppData%\SysViewManager\runtime_config.json` |
| Auto-start | `schtasks.exe /create /sc ONLOGON /rl HIGHEST` |
| Exports JSON | `Hardware.json` (500 ms) · `Weather.json` (intervalle configurable) |
| CORS | `null` (WE renderer) · `127.0.0.1` |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` — 350/min poll · 60/min config |

### Extension navigateur (SysView Media Bridge)

| Composant | Technologie |
|-----------|-------------|
| Standard | Chrome MV3 (Manifest V3) |
| `content-main.js` | World **MAIN** — `navigator.mediaSession` · `CustomEvent` JSON |
| `content.js` | World **ISOLATED** — `<video>` poll 500 ms · `chrome.runtime.sendMessage` |
| `background.js` | Service Worker — `fetch POST /v1/media` |
| Sélection vidéo | `readyState ≥ 1` · tri par `currentTime` puis résolution puis durée |
| Titre fallback | `document.title` → DOM service-specific → nom du service |
| Image fallback | `video.poster` → CDN DOM (`nflximg`, `m.media-amazon.com`) → CSS bg |

### Front-end (SysView.html + src/)

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

*SysView V6 — Windows 10 / 11 x64 — v1.0.0*  
*[github.com/Mrtt555/SysView-V6](https://github.com/Mrtt555/SysView-V6)*
