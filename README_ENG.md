[🇫🇷 Français](README.md) | 🇬🇧 **English**

---

# SysView V6 — Wallpaper Engine System Monitor

Interactive wallpaper for Wallpaper Engine displaying in real time:
**CPU · GPU · RAM · VRAM · Network · Storage · Weather · Pollen · Air Quality · Media · Audio Visualizer**

Compatible with Windows 10 / 11 x64 — Wallpaper Engine required

> GitHub: [github.com/Mrtt555/sysview-wallpaper-engine](https://github.com/Mrtt555/sysview-wallpaper-engine)

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                       WALLPAPER ENGINE                           │
│  SysView.html (isolated Chromium, local network allowed)         │
│                                                                  │
│  ┌──────────┐  ┌─────────────┐  ┌──────────┐  ┌─────────────┐  │
│  │  Clock   │  │  Monitoring │  │ Weather  │  │    Media    │  │
│  │ Date/    │  │  CPU · GPU  │  │  Pollen  │  │  Title +    │  │
│  │ Location │  │  RAM · VRAM │  │  AQI     │  │  Thumbnail  │  │
│  │          │  │  Network    │  │  Rain %  │  │  Audio viz  │  │
│  └──────────┘  └─────┬───────┘  └────┬─────┘  └──────┬──────┘  │
└────────────────────── │ ──────────── │ ────────────── │ ────────┘
          GET /v1/perf ─┘  GET /v1/weather  GET /v1/media
          POST /v1/config (city·iface·interval — debounce 300ms)
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
   │  Aether — Open-Meteo proxy │   │  LibreHardwareMonitor    │
   │  FastAPI :8001             │   │  (Admin, port 8086)      │
   │  Météo-France AROME (FR)   │   │                          │
   │  /api/live_data            │   │  GET /data.json          │
   │  /api/config (city/lat/lon)│   │  → full JSON sensor tree │
   │  Web UI at :8001           │   │  CPU/GPU/VRAM/Network    │
   └────────────┬───────────────┘   └──────────────────────────┘
                │
   ┌────────────▼───────────────────────────────────────────────┐
   │  Open-Meteo (HTTPS, no API key required)                   │
   │  · Forecast API — weather + AQI + pollen (via Aether)      │
   │  · Geocoding API — city → lat/lon (direct bridge)          │
   │  · Forecast API — rain probability % (direct bridge,       │
   │                    fallback when AROME returns null)        │
   └────────────────────────────────────────────────────────────┘

   ┌──────────────────────────────┐
   │  Chromium browser            │
   │  SysViewExtension (MV3)      │
   │  content.js + background.js  │
   │  POST /v1/media              │
   └──────────────────────────────┘
```


---

## Folder Structure

```
SysView V6/
├── SysView.html              ← Main wallpaper (vanilla HTML/CSS/JS)
├── project.json              ← Wallpaper Engine configuration (UI properties)
├── preview.gif               ← Thumbnail preview
├── README.md                 ← French version
├── README_ENG.md             ← This file (EN)
│
├── API V3/
│   ├── SysViewBridge.pyw     ← FastAPI server (main bridge)
│   ├── config.py             ← Port + LHM, Aether, Open-Meteo URLs
│   ├── runtime_config.json   ← Persisted runtime config (auto-created)
│   ├── install.bat           ← One-click installer (Python + packages + auto-start)
│   ├── stop.bat              ← Stop the bridge
│   ├── uninstall.bat         ← Full uninstall
│   ├── logs/
│   │   └── sysview.log       ← Rotating log (10 MB × 5 files)
│   └── aether/               ← Open-Meteo proxy (downloaded by install.bat)
│       ├── main.py
│       ├── config.json       ← City + lat/lon + weather model
│       ├── requirements.txt
│       └── frontend/         ← Aether web UI (http://127.0.0.1:8001)
│
└── SysViewExtension/
    ├── manifest.json         ← MV3 manifest (Chromium)
    ├── content.js            ← Video/audio detection on all pages
    ├── background.js         ← Service worker → sends to bridge
    └── README.txt            ← Extension installation instructions
```

---

## Installation

> **Recommended order:** Wallpaper Engine → LHM → Python Bridge → Browser Extension

---

### Step 1 — Wallpaper Engine

1. Download and unzip the ZIP from GitHub
2. Open **Wallpaper Engine** → at the bottom of the library, click **"Open a wallpaper"**
3. Select `SysView.html` from the unzipped folder

WE automatically creates a project folder and copies all files there:
```
...\wallpaper_engine\projects\myprojects\SysView V6\
```

> **Access the project folder:** right-click SysView in the library → **"Open folder"**
> All following steps (bridge, extension) are done from inside this folder.

4. In **WE Settings → General**:
   - Enable **"Allow network access for web wallpapers"** *(required)*

5. In the wallpaper's **Customize** panel, configure the options below:

---

### Step 2 — LibreHardwareMonitor (LHM)

LHM provides CPU/GPU temperatures, load percentages, VRAM and network speeds
through a local HTTP API. It must run as **Administrator**.

**Download LHM:**
- Official GitHub page: [github.com/LibreHardwareMonitor/LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/)
- Direct download v0.9.6 **recommended:** [LibreHardwareMonitor.NET.10.zip](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.NET.10.zip)

**Installation:**

1. Extract the archive to a permanent folder (e.g. `C:\LHM\`)
2. **Right-click `LibreHardwareMonitor.exe` → Run as administrator**
   > ⚠️ Administrator rights are required to read hardware sensors.
3. In the **Options** menu:
   - Check **Remote Web Server** → LHM listens on port 8086
   - Check **Run On Windows Startup** *(recommended)*
   - Check **Start Minimized** *(recommended)*
4. Verify the server is running by opening:
   **http://localhost:8086/data.json** → should return a JSON tree with all sensors

> **Note:** If `data.json` returns an error or is empty, LHM was not launched
> with administrator rights. Close it and relaunch as admin.

---

### Step 3 — Python Bridge (API)

The bridge is a lightweight FastAPI server running in the background,
polling LHM and psutil, and exposing data to the wallpaper on port 5001.
It also starts **Aether** (Open-Meteo proxy) as a subprocess on port 8001.

**Prerequisite:** Python 3.10+ from [python.org](https://www.python.org/) *(not the Microsoft Store)*

From the **WE project folder** → open `API V3\` → **double-click `install.bat`**

The script does everything automatically:
1. Checks for Python — downloads it automatically if not found
2. Installs bridge packages: `fastapi` `uvicorn[standard]` `requests` `psutil` `slowapi`
3. Clones Aether from GitHub and installs its dependencies
4. Creates an auto-start shortcut in `%APPDATA%\...\Startup\`
5. Launches the bridge immediately (which starts Aether as a subprocess)

**Verify everything is working:**

Open in your browser: [http://127.0.0.1:5001/v1/status](http://127.0.0.1:5001/v1/status)

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

| Field | Expected value |
|-------|----------------|
| `modules.lhm` | `"ok"` — LHM is running and responding on port 8086 |
| `modules.open_meteo` | `"ok"` — weather loaded (may show `"pending"` for the first few seconds) |

Aether web UI (weather configuration, forecasts, AQI): [http://127.0.0.1:8001](http://127.0.0.1:8001)

Other scripts in `API V3\`:

| File | Action |
|------|--------|
| `stop.bat` | Stop the bridge (reads `bridge.pid`) |
| `uninstall.bat` | Removes Python packages + startup shortcut + Aether folder |

---

### Wallpaper settings

In the wallpaper's **Customize** panel:

#### Colors & Style

| Setting | Description |
|---------|-------------|
| **Language** | FR / EN — switches all text instantly |
| **Background image** | Background image JPG/PNG/GIF/WEBP (optional) |
| **UI Scale** | Interface scale — 100 = normal size |
| **Opacity** | Opacity of panels, cards and shadows (0 = invisible, 100 = solid) |
| **Bottom bar height** | Height in px above the Windows taskbar |
| **Accent color** | Main color — borders, bars, glows |
| **Secondary accent color** | Lighter secondary color — labels, highlights |
| **Background color** | Base background color of the wallpaper |
| **Text color** | General text color |
| **Clock format** | 24h or 12h (AM/PM) clock |
| **Temperature unit** | °C or °F |
| **Show temperature decimal** | Show decimal on weather temperature (e.g. 15.6 instead of 16) |

#### Location & Weather

| Setting | Description |
|---------|-------------|
| **Show city and country** | Display city name under the clock and in the weather panel |
| **Set weather location** | Enable to manually enter your city |
| **City name** | City name (e.g. PARIS, LONDON) — geocoded automatically via Open-Meteo |
| **Weather refresh interval** | Refresh interval in minutes (1 to 15) |
| **Show weather source badge** | Show/hide the "Open-Meteo" badge in the weather panel |

> The bridge geocodes the city automatically (Open-Meteo Geocoding API) and configures Aether with the resulting GPS coordinates. Settings are persisted in `runtime_config.json` — interval and city are restored on bridge restart.

#### Temperature Thresholds

| Setting | Description |
|---------|-------------|
| **CPU — warning temp** | Orange threshold for CPU (default: 80 °C) |
| **CPU — critical temp** | Red threshold for CPU (default: 91 °C) |
| **GPU — warning temp** | Orange threshold for GPU (default: 80 °C) |
| **GPU — critical temp** | Red threshold for GPU (default: 95 °C) |

#### Panels to Display

| Setting | Description |
|---------|-------------|
| **Show Monitoring panel** | Main CPU/GPU/RAM/VRAM/Network panel |
| **Show CPU / GPU / VRAM / RAM / Network block** | Individual blocks (require Show Monitoring) |
| **Network interface to display** | `Auto` (WiFi+Ethernet), `Ethernet`, `Wi-Fi` |
| **Show Storage panel** | Drives C: to H: |
| **Show Disk C/D/E/F/G/H** | Individual drives |
| **Show free space on disks** | Display remaining free space for each drive |
| **Show Media player** | Bottom bar (thumbnail, progress, audio visualizer) |
| **Show Weather panel** | Right-side weather panel |

---

### Step 4 — Browser Extension *(optional)*

The extension displays in the media bar the videos playing in your browser
(YouTube, Twitch, Netflix, Spotify Web, etc.) with title, artist and thumbnail.

**Compatible browsers:** Brave · Chrome · Edge · Opera · Vivaldi *(Chromium only)*
Firefox is not supported.

**Supported sites:**
YouTube (+ Shorts) · YouTube Music · Twitch · Netflix · Prime Video · Vimeo · Dailymotion · Plex · Emby · Jellyfin
· any site with an HTML5 `<video>` or `<audio>` tag

**Installation:**

1. Open your browser's extensions page:
   - Brave: `brave://extensions`
   - Chrome: `chrome://extensions`
   - Edge: `edge://extensions`
2. Enable **Developer mode** (toggle in the top right)
3. Click **"Load unpacked"**
4. Select the `SysViewExtension\` folder from the **WE project folder**
   *(right-click the wallpaper → "Open folder")*

**Verify:** Play a YouTube video, then open
[http://127.0.0.1:5001/v1/media](http://127.0.0.1:5001/v1/media) — title and artist should appear.

---

## Detailed Features

### Hardware Monitoring

- Refresh every **750 ms**
- **EMA smoothing α=0.60** on CPU, GPU, RAM, VRAM (t₉₀ ≈ 1.9 s) → smooth transitions without jumps
- **EMA smoothing α=0.97** on network (very reactive to spikes)
- CSS bar transition: 0.75 s
- Temperature colors: `< warn_threshold` → accent / `≥ warn_threshold` → orange / `≥ crit_threshold` → red
- Temperature and load colored **independently**
- Monitoring temperatures always displayed as whole numbers (°C/°F) — decimal only applies to weather

Data sources:

| Metric | Primary source | Fallback |
|--------|----------------|----------|
| CPU temp | LHM (ID 66) | — |
| CPU usage | LHM (ID 73) | psutil |
| GPU temp | LHM (ID 187) | — |
| GPU usage | LHM (ID 193) | — |
| VRAM used | LHM (ID 208) | — |
| VRAM total | LHM (ID 210) | — |
| RAM % | LHM (ID 120) | psutil |
| Network WiFi | LHM (ID 513/514) RawValue B/s | — |
| Network Ethernet | LHM (ID 468/469) RawValue B/s | — |
| Network Auto | LHM WiFi + Ethernet sum | psutil |
| Disks | psutil (C: to H:) | — |

> **Auto network mode**: sums WiFi + Ethernet from LHM. Falls back to psutil if LHM is offline.

### Weather · Pollen · Air Quality

- Source: **Open-Meteo** via **Aether** (free, no API key required)
- Automatic model selection based on location (Météo-France AROME for France)
- Automatic geocoding: enter a city name, the bridge fetches the GPS coordinates
- Configurable refresh interval from **1 to 15 minutes** in WE
- Immediate refresh on city name change
- Automatic retry with exponential backoff on failure
- Runtime config persisted in `runtime_config.json` (survives bridge restart)

| Indicator | Display | Levels |
|-----------|---------|--------|
| Temperature | °C/°F value (optional decimal) | — |
| Rain probability | % (direct Open-Meteo best_match) | 0–100 % |
| Wind | km/h | — |
| Pollen | Colored label + value in grains/m³ | 🟢 None · 🟢 Low · 🟡 Moderate · 🟠 High · 🔴 Very High |
| AQI Europe | Index 0–100 + label | 🟢 Good · 🟡 Fair · 🟠 Moderate · 🔴 Poor · 🔴 Very Poor |

> **Pollen**: sum of grass + birch + alder + ragweed in grains/m³ (Copernicus CAMS via Aether).  
> **Rain probability**: the AROME model does not provide this variable — the bridge makes a lightweight direct call to Open-Meteo using the `best_match` model.

### Media Panel

- **Chrome Extension only**: YouTube (+ Shorts), Twitch, Netflix, Prime Video, any HTML5 video/audio site
- Source priority: a paused tab cannot override an already-active source — but is accepted when the bar is idle (empty title)
- 30-second idle delay: protects against false positives during seek or buffering
- Bridge-side interpolation: playback position is continuously recalculated (no seeking jumps)
- Updates even when the browser is minimized or in the background, as long as media is playing

### Audio Visualizer

- Powered by **`wallpaperRegisterAudioListener`** from Wallpaper Engine
- Reacts to **all PC audio output** (music, games, videos, etc.)
- 24 bars mapping frequency bins 0–63 (left channel LR spectrum)
- **Asymmetric EMA**: rise α=0.80 (fast) / fall α=0.18 (smooth)
- Fixed purple color, independent of the accent colors set in WE
- Bars rest at minimum when no audio is playing

---

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/v1/health` | Bridge status (uptime, version) |
| GET | `/v1/perf` | CPU · GPU · RAM · VRAM · Network · Disks |
| GET | `/v1/weather` | Weather · AQI · Pollen · Rain prob. (Aether/Open-Meteo cache) |
| GET | `/v1/media` | Current playback (title · artist · position · thumbnail) |
| GET | `/v1/status` | Full diagnostic (modules · endpoints · extension) |
| POST | `/v1/config` | Receives configuration from WE (city · iface · interval) |
| POST | `/v1/media` | Receives data from Chrome extension |
| GET | `/docs` | Interactive FastAPI documentation (Swagger UI) |

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| `modules.lhm = "offline"` in /v1/status | LHM not running, or not run as admin, or Remote Web Server disabled |
| CPU/GPU/temp shows `—` | LHM → Options → Remote Web Server → check port 8086 |
| Network at 0 in WiFi/Ethernet mode | Check that LHM lists the network sensors in its UI |
| Network Auto = 0 | LHM offline → automatic fallback to psutil |
| Weather stuck at `"pending"` | Normal for the first few seconds — auto-retry in progress |
| Rain probability empty (`—`) | Aether still starting — wait for the first refresh (1–3 min) |
| City not found | Enter only the city name without country code (e.g. `PARIS` not `PARIS, FR`) |
| Wrong interval after restart | Check that `runtime_config.json` exists in `API V3\` |
| Empty media panel | Install the Chrome extension and play a video in your browser |
| Extension: `active = false` | Extension not installed or video is in a background tab |
| Wallpaper not fetching any data | WE → Settings → General → enable "Allow network access" |
| Bridge won't start | Read `API V3\logs\sysview.log` |
| Python not detected | Install from python.org *(not the Microsoft Store)* |
| port 5001 already in use | Run `stop.bat` then relaunch `install.bat` |
| Aether unreachable (:8001) | Rerun `install.bat` or check the logs |

---

## Uninstall

- **LHM:** close the application, delete its folder
- **Bridge:** `API V3\uninstall.bat` (stops bridge + removes Python packages + `aether\` folder + startup shortcut)
- **Extension:** browser extensions page → SysView Media Bridge → Remove
- **Wallpaper Engine:** right-click SysView → Delete

---

## Technologies

### Frontend — `SysView.html`

| Technology | Usage |
|------------|-------|
| **Vanilla HTML/CSS/JS** | No framework — compatible with WE's embedded Chromium renderer |
| **CSS Custom Properties** | Variables `--ac` `--ac2` `--bg` `--tx` `--pa` `--sz` modified in real time by WE |
| **CSS transform + transition** | Bar animations for monitoring metrics and audio visualizer |
| **`wallpaperPropertyListener`** | WE API — receives user property changes |
| **`wallpaperRegisterAudioListener`** | WE API — receives 128 frequency bins at ~30 fps |
| **EMA (Exponential Moving Average)** | Hardware metric smoothing — α=0.60 / α=0.97 network |
| **fetch() + setInterval** | Periodic polling of bridge endpoints (750ms perf, 500ms media) |
| **Debounce 300ms / 500ms** | Groups POST /v1/config calls and city search requests |

### Backend — `SysViewBridge.pyw`

| Technology | Usage |
|------------|-------|
| **FastAPI** | ASGI framework — routing, validation, auto Swagger docs |
| **Uvicorn** | ASGI server — asyncio event loop, connection handling |
| **slowapi** | Rate limiting — 350/min on polling endpoints (perf · weather · media · health), 60/min on status · config |
| **psutil** | CPU %, RAM %, disks, network (fallback when LHM is absent) |
| **requests** | HTTP to LHM (8085), Aether (8000), Open-Meteo Geocoding, Open-Meteo Forecast |
| **threading** | 3 daemon threads: hardware\_loop, disk\_loop, weather\_loop |
| **concurrent.futures** | Isolates `psutil.disk_partitions()` in a dedicated thread with a 5 s timeout |
| **threading.Lock** | `perf_lock` `weather_lock` `media_lock` `runtime_lock` — thread-safe access |
| **threading.Event** | `_weather_event` — immediately wakes weather\_loop when city/interval changes |
| **json + pathlib** | Runtime config persistence (`runtime_config.json`) |
| **RotatingFileHandler** | Rotating log 10 MB × 5 files in `logs/sysview.log` |
| **CORS + Private Network** | Required headers for WE's Chromium renderer |

### Aether — Open-Meteo proxy

| Concept | Detail |
|---------|--------|
| **FastAPI :8001** | Open-Meteo proxy with web admin interface |
| **Auto model** | Automatically selects the best model for the coordinates (AROME for France) |
| **POST /api/config** | Receives city + lat/lon from the bridge — syncs Aether's web display |
| **GET /api/live_data** | Aggregates current weather + air quality + pollen |
| **config.json** | City, coordinates, weather model, parameters — persisted to disk |

### Hardware — LibreHardwareMonitor

| Concept | Detail |
|---------|--------|
| **Local HTTP API** | `GET /data.json` → full JSON sensor tree |
| **Stable IDs** | Each sensor has a numeric ID that is stable across sessions |
| **RawValue vs Value** | `RawValue` = raw bytes/s (network) — `Value` is auto-scaled by LHM (KB/MB/GB) which causes reading errors |
| **Admin rights** | Required to access kernel-level sensors (PMU, SMBus, WMI) |
| **port 8086** | Configurable in Options → Remote Web Server |

### Chrome Extension — `SysViewExtension/`

| File | Role |
|------|------|
| **manifest.json** | MV3, permissions `<all_urls>` + `http://127.0.0.1:5001/*` |
| **content.js** | Injected into all pages — detects `<video>`, `MediaSession API`, extracts title/artist/thumbnail |
| **background.js** | Service worker — receives messages from content.js, POSTs to `/v1/media` |

### Data Flow

```
1. HARDWARE (750ms)
   LHM /data.json → _lhm_parse() → IDs 66/73/120/187/193/208/210/468/469/513/514
   psutil          → CPU% / RAM / net_io_counters / disk_usage
                    ↓ interface selection (auto/eth/wifi)
                    ↓ EMA α=0.60 (α=0.97 network)
                    → PERF dict (thread-safe)
                    ← GET /v1/perf (HTML, 750ms)
                    → renderBars() + threshold colors

2. WEATHER (configurable 1–15 min)
   Bridge → Aether /api/live_data
     → Open-Meteo (Météo-France AROME for FR): temp / precip / wind / code
     → Open-Meteo (CAMS): European AQI / pollen grains/m³
   Bridge → Open-Meteo Geocoding API (city name → lat/lon + normalized name)
   Bridge → Open-Meteo Forecast API (rain probability % — fallback when AROME null)
   Bridge → Aether POST /api/config (lat/lon + city name → syncs Aether web UI)
   → runtime_config.json (interval + city + lat/lon persisted to disk)
   → WEATHER dict
   ← GET /v1/weather (HTML)
   → renderWeather()

3. MEDIA — Chrome Extension
   content.js (1s) → background.js → POST /v1/media
   → MEDIA dict
   Priority: new title accepted only if playing=True

4. MEDIA DISPLAY
   HTML pollMediaBridge (500ms) ← GET /v1/media
   bridge returns interpolated position (time.time() - last_update)
   → renderMediaProgress() with CSS transition 1s linear

5. CONFIGURATION
   WE applyUserProperties → debounce 300ms → POST /v1/config
   → RUNTIME dict → hardware_loop / weather_loop read on each cycle
   → runtime_config.json (auto-saved on every change)

6. AUDIO VISUALIZER
   WE wallpaperRegisterAudioListener → audioArray[128] at ~30fps
   → bins 0-63 mapped to 24 bars (peak max per group)
   → asymmetric EMA: rise α=0.80 / fall α=0.18
   → CSS transform scaleY(0.04 … 0.90)
```

---

*SysView V6 — Windows 10 / 11 x64*
*GitHub: [github.com/Mrtt555/sysview-wallpaper-engine](https://github.com/Mrtt555/sysview-wallpaper-engine)*
