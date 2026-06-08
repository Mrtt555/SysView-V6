@echo off
setlocal
cd /d "%~dp0"
title SysView Bridge - Installation

echo.
echo ============================================
echo  SysView Bridge v5 - Installation
echo ============================================
echo.

:: --- Python check / auto-install ---
python --version >nul 2>&1
if not errorlevel 1 goto :have_python

echo Python not found - downloading latest version...
echo (this may take a minute)
echo --------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $r=(Invoke-WebRequest 'https://www.python.org/downloads/' -UseBasicParsing -ErrorAction Stop).Content; $v=([regex]'Download Python (\d+\.\d+\.\d+)').Match($r).Groups[1].Value; if(-not $v){Write-Host '[ERROR] Could not detect Python version from python.org'; exit 1}; Write-Host ('  -> Python '+$v); $f=$env:TEMP+'\pysetup.exe'; Invoke-WebRequest ('https://www.python.org/ftp/python/'+$v+'/python-'+$v+'-amd64.exe') -OutFile $f -UseBasicParsing -ErrorAction Stop; Start-Process -Wait -FilePath $f -ArgumentList '/quiet InstallAllUsers=0 PrependPath=1 Include_test=0'; Remove-Item $f -ErrorAction SilentlyContinue }"
if errorlevel 1 (
    echo.
    echo [ERROR] Python installation failed.
    echo Download it manually from https://python.org
    echo.
    pause
    exit /b 1
)
echo [OK] Python installed.
echo.
echo Reloading PATH...
for /f "tokens=2*" %%A in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "NEWPATH=%%B"
if defined NEWPATH set "PATH=%NEWPATH%;%PATH%"
set "NEWPATH="

:have_python

:: --- Locate exact python.exe / pythonw.exe ---
for /f "tokens=*" %%P in ('where python 2^>nul') do if not defined _PY set "_PY=%%P"
if not defined _PY (
    echo [ERROR] python.exe not found in PATH.
    pause
    exit /b 1
)
set "_PYW=%_PY:python.exe=pythonw.exe%"
if not exist "%_PYW%" set "_PYW=%_PY%"
echo [INFO] Python  : %_PY%
echo [INFO] Pythonw : %_PYW%
echo.

:: --- Step 1 : Python packages (bridge) ---
echo [1/4] Installing Python packages...
echo --------------------------------------------
"%_PY%" -m pip install fastapi "uvicorn[standard]" requests psutil slowapi
if errorlevel 1 (
    echo.
    echo [ERROR] pip install failed.
    echo.
    pause
    exit /b 1
)
echo.
echo Verifying packages...
"%_PY%" -c "import fastapi, uvicorn, requests, psutil, slowapi; print('[OK] All packages OK')"
if errorlevel 1 (
    echo.
    echo [ERROR] Package verification failed - see error above.
    echo.
    pause
    exit /b 1
)
echo.

:: --- Step 2 : Aether (proxy Open-Meteo multi-modeles) ---
echo [2/4] Setting up Aether weather service...
echo --------------------------------------------
set "_AETHER_DIR=%~dp0aether"
if not exist "%_AETHER_DIR%\main.py" (
    echo   Downloading Aether from GitHub...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $z='%TEMP%\aether.zip'; Invoke-WebRequest 'https://github.com/Mrtt555/Aether/archive/refs/heads/main.zip' -OutFile $z -UseBasicParsing -ErrorAction Stop; Expand-Archive $z -DestinationPath '%TEMP%\aether_tmp' -Force; if(Test-Path '%_AETHER_DIR%'){Remove-Item '%_AETHER_DIR%' -Recurse -Force}; Move-Item '%TEMP%\aether_tmp\Aether-main' '%_AETHER_DIR%'; Remove-Item $z -ErrorAction SilentlyContinue; Remove-Item '%TEMP%\aether_tmp' -Recurse -Force -ErrorAction SilentlyContinue }"
    if not exist "%_AETHER_DIR%\main.py" (
        echo.
        echo [ERROR] Aether download failed. Check internet connection.
        echo.
        pause
        exit /b 1
    )
    echo [OK] Aether downloaded.
) else (
    echo [INFO] Aether already present - skipping download.
)
echo   Installing Aether packages...
"%_PY%" -m pip install -r "%_AETHER_DIR%\requirements.txt" --quiet
if errorlevel 1 (
    echo.
    echo [ERROR] Aether packages install failed.
    echo.
    pause
    exit /b 1
)
echo [OK] Aether ready.
echo [INFO] Config UI will be available at http://127.0.0.1:8001 once bridge starts.
echo.

:: --- Step 3 : Startup shortcut ---
echo [3/4] Creating startup shortcut...
echo --------------------------------------------
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SysViewBridge.bat"
>"%STARTUP%" echo @echo off
>>"%STARTUP%" echo start "" "%_PYW%" "%~dp0SysViewBridge.pyw"
if exist "%STARTUP%" (
    echo [OK] Auto-start configured.
) else (
    echo [WARN] Could not create startup shortcut.
)
echo.

:: --- Step 4 : Start bridge (Aether demarre automatiquement avec lui) ---
echo [4/4] Starting bridge...
echo --------------------------------------------

:: Stop any existing instance first
if not exist "bridge.pid" goto :do_start
for /f "usebackq tokens=*" %%i in ("bridge.pid") do taskkill /PID %%i /F >nul 2>&1
del "bridge.pid" >nul 2>&1
echo [INFO] Stopped previous instance.
ping -n 3 127.0.0.1 >nul

:do_start
del "bridge.pid" >nul 2>&1
start "" "%_PYW%" "%~dp0SysViewBridge.pyw"
echo [INFO] Waiting for bridge to initialize...
ping -n 8 127.0.0.1 >nul
if exist "bridge.pid" goto :bridge_ok
echo [WARN] Bridge did not start. Check: logs\sysview.log
goto :bridge_checked
:bridge_ok
echo [OK] Bridge started successfully (port 5001).
:bridge_checked
echo.

set "_PY=" & set "_PYW="

echo ============================================
echo  Done.
echo  Bridge will auto-start on every Windows login.
echo  Use stop.bat / uninstall.bat to manage it.
echo ============================================
echo.
echo  Test endpoints:
echo.
echo    http://127.0.0.1:5001/v1/health
echo    http://127.0.0.1:5001/v1/perf
echo    http://127.0.0.1:5001/v1/weather
echo    http://127.0.0.1:5001/v1/media
echo    http://127.0.0.1:5001/v1/status
echo    http://127.0.0.1:5001/docs
echo.
endlocal
pause
