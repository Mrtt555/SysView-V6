@echo off
setlocal enabledelayedexpansion
title SysView V6 -- Installation complete

echo.
echo  =========================================================
echo   SysView V6 -- Installation complete  (tout-en-un)
echo  =========================================================
echo.
echo  Ce script va faire EN UNE SEULE FOIS :
echo    1. Telecharger SysView V6 depuis GitHub
echo    2. Compiler SysViewHardware (capteurs C# passifs, port 8086)
echo       + SDK .NET 8 telecharge si absent + demarrage admin auto
echo    3. Verifier / installer Python automatiquement
echo    4. Installer les paquets Python du bridge
echo    5. Telecharger et installer Aether (proxy meteo)
echo    6. Configurer le demarrage automatique Windows
echo    7. Lancer SysViewHardware + bridge + Aether
echo.
echo  Aucune autre action ne sera necessaire.
echo.

:: =========================================================
:: CHEMINS D'INSTALLATION
:: =========================================================

:: --- SysView ---
set "_DEFAULT=C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\projects\myprojects"
echo  [SysView V6] Dossier myprojects de Wallpaper Engine :
echo    Defaut : %_DEFAULT%
echo.
set "_BASE="
set /p "_BASE=  Chemin SysView [Entree = defaut] : "
if not defined _BASE set "_BASE=%_DEFAULT%"
if "!_BASE:~-1!"=="\" set "_BASE=!_BASE:~0,-1!"

set "_DEST=!_BASE!\SysView V6"
set "_APIV3=!_DEST!\API V3"
set "_AETHER=!_APIV3!\aether"

echo.
echo  >>> SysView V6 : !_DEST!
echo.
pause

:: Verifier que le dossier parent SysView existe
if not exist "!_BASE!\" (
    echo [ERREUR] Dossier introuvable : !_BASE!
    echo.
    echo  Conseil : dans Wallpaper Engine, faites un clic droit
    echo  sur un wallpaper -^> "Ouvrir le dossier" pour trouver
    echo  le bon chemin myprojects.
    echo.
    pause ^& exit /b 1
)

if exist "!_DEST!\" (
    echo  [AVERT] SysView V6 est deja installe.
    echo  Les fichiers seront mis a jour (aether et config conserves).
    echo.
    set /p "_OK=  Continuer ? (O / N) : "
    if /i not "!_OK!"=="O" (
        echo Installation annulee.
        pause ^& exit /b 0
    )
    echo.
)

:: 1/6 � SysView V6 (GitHub)
:: =========================================================
echo [1/6] Telechargement de SysView V6 depuis GitHub...
echo ---------------------------------------------------------
set "_ZIP=%TEMP%\sysview_setup.zip"
set "_TMP=%TEMP%\sysview_setup_tmp"

if exist "%_TMP%" powershell -NoProfile -Command "Remove-Item '%_TMP%' -Recurse -Force -ErrorAction SilentlyContinue"
if exist "%_ZIP%" del "%_ZIP%" >nul 2>&1

echo  Connexion a GitHub...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://github.com/Mrtt555/sysview-wallpaper-engine/archive/refs/heads/main.zip' -OutFile '%_ZIP%' -UseBasicParsing -ErrorAction Stop"
if errorlevel 1 (
    echo [ERREUR] Telechargement echoue. Verifiez votre connexion.
    pause & exit /b 1
)
echo  Extraction...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive '%_ZIP%' -DestinationPath '%_TMP%' -Force"
if errorlevel 1 (
    echo [ERREUR] Extraction echouee.
    pause & exit /b 1
)
del "%_ZIP%" >nul 2>&1

set "_SRC="
for /d %%D in ("%_TMP%\*") do if not defined _SRC set "_SRC=%%D"
if not defined _SRC (
    echo [ERREUR] Archive vide ou structure inattendue.
    pause & exit /b 1
)

:: Sauvegarder aether\ et runtime_config.json
set "_BCK=%TEMP%\sysview_bck"
if exist "%_BCK%" powershell -NoProfile -Command "Remove-Item '%_BCK%' -Recurse -Force -ErrorAction SilentlyContinue"
mkdir "%_BCK%" >nul 2>&1
if exist "!_AETHER!\" (
    echo  Sauvegarde d'Aether...
    powershell -NoProfile -Command "Copy-Item '!_AETHER!' '%_BCK%\aether' -Recurse -Force"
)
if exist "!_APIV3!\runtime_config.json" copy /y "!_APIV3!\runtime_config.json" "%_BCK%\runtime_config.json" >nul 2>&1

:: Remplacer / installer
if exist "!_DEST!\" powershell -NoProfile -Command "Remove-Item '!_DEST!' -Recurse -Force"
powershell -NoProfile -Command "Move-Item '!_SRC!' '!_DEST!'"
if not exist "!_DEST!\SysView.html" (
    echo [ERREUR] SysView.html introuvable apres extraction.
    pause & exit /b 1
)
powershell -NoProfile -Command "Remove-Item '%_TMP%' -Recurse -Force -ErrorAction SilentlyContinue"

:: Restaurer aether\ et runtime_config.json
if exist "%_BCK%\aether\" powershell -NoProfile -Command "Copy-Item '%_BCK%\aether' '!_AETHER!' -Recurse -Force"
if exist "%_BCK%\runtime_config.json" copy /y "%_BCK%\runtime_config.json" "!_APIV3!\runtime_config.json" >nul 2>&1
powershell -NoProfile -Command "Remove-Item '%_BCK%' -Recurse -Force -ErrorAction SilentlyContinue"

echo [OK] SysView V6 installe : !_DEST!
echo.


:: =========================================================
:: 2/6 -- SYSVIEWHARDWARE (capteurs C# via LibreHardwareMonitorLib)
:: =========================================================
echo [2/6] Compilation de SysViewHardware...
echo ---------------------------------------------------------

set "_HW_EXE=!_APIV3!\SysViewHardware.exe"
set "_HW_PROJ=!_APIV3!\SysViewHardware\SysViewHardware.csproj"

if exist "!_HW_EXE!" (
    echo [INFO] SysViewHardware.exe deja present -- compilation ignoree.
    goto :hw_startup
)

:: Verifier si .NET 8+ SDK est disponible
set "_DOTNET="
for /f "tokens=*" %%D in ('where dotnet 2^>nul') do if not defined _DOTNET set "_DOTNET=%%D"

if defined _DOTNET (
    set "_SDK_OK=0"
    for /f "tokens=1 delims=." %%V in ('dotnet --version 2^>nul') do if %%V GEQ 8 set "_SDK_OK=1"
    if not "!_SDK_OK!"=="1" set "_DOTNET="
)

if not defined _DOTNET (
    echo  SDK .NET 8 introuvable -- installation ^(~200 Mo, quelques minutes^)...
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "& { $f='%TEMP%\dotnet-install.ps1'; Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $f -UseBasicParsing; & $f -Channel 8.0 -InstallDir '$env:USERPROFILE\.dotnet' }"
    if errorlevel 1 (
        echo [ERREUR] Installation SDK .NET 8 echouee.
        pause ^& exit /b 1
    )
    set "_DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
)

echo  Compilation en cours ^(premiere fois ~2-3 min -- NuGet + build^)...
"!_DOTNET!" publish "!_HW_PROJ!" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "!_APIV3!" --nologo -v quiet
if errorlevel 1 (
    echo [ERREUR] Compilation SysViewHardware echouee.
    echo  Verifiez que le projet est intact : !_HW_PROJ!
    pause ^& exit /b 1
)

if not exist "!_HW_EXE!" (
    echo [ERREUR] SysViewHardware.exe introuvable apres compilation.
    pause ^& exit /b 1
)
echo [OK] SysViewHardware.exe compile ^(~50 Mo, autonome^).

:hw_startup
:: --- Demarrage automatique avec privileges admin via Task Scheduler ---
echo  Demarrage automatique ^(admin^) : configuration...
schtasks /query /tn "SysViewHardware" >nul 2>&1
if not errorlevel 1 (
    echo [INFO] Tache planifiee SysViewHardware deja presente.
    goto :hw_launch
)
schtasks /create /tn "SysViewHardware" /tr "\"!_HW_EXE!\"" /sc ONLOGON /rl HIGHEST /f >nul 2>&1
if not errorlevel 1 (
    echo [OK] Tache planifiee : SysViewHardware demarre au login avec droits admin.
    goto :hw_launch
)
:: Fallback sans droits admin
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "SysViewHardware" /t REG_SZ /d "\"!_HW_EXE!\"" /f >nul 2>&1
echo [AVERT] Tache planifiee impossible ^(droits insuffisants^).
echo  Demarrage simple configure. Pour les capteurs : clic droit "Executer en tant qu'administrateur".

:hw_launch
echo  Lancement de SysViewHardware ^(confirmation UAC attendue^)...
powershell -NoProfile -Command "Start-Process '!_HW_EXE!' -Verb RunAs"
echo [OK] SysViewHardware lance ^(port 8086^).
echo.

:: 3/6 � PYTHON
:: =========================================================
echo [3/6] Verification de Python...
echo ---------------------------------------------------------
python --version >nul 2>&1
if not errorlevel 1 goto :have_python

echo  Python introuvable � telechargement automatique...
echo  (quelques minutes selon votre connexion)
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $r=(Invoke-WebRequest 'https://www.python.org/downloads/' -UseBasicParsing).Content; $v=([regex]'Download Python (\d+\.\d+\.\d+)').Match($r).Groups[1].Value; if(!$v){throw 'Version introuvable'}; Write-Host('  -> Python '+$v); $f=$env:TEMP+'\pysetup.exe'; Invoke-WebRequest('https://www.python.org/ftp/python/'+$v+'/python-'+$v+'-amd64.exe') -OutFile $f -UseBasicParsing; Start-Process -Wait $f '/quiet InstallAllUsers=0 PrependPath=1 Include_test=0'; Remove-Item $f -ErrorAction SilentlyContinue }"
if errorlevel 1 (
    echo.
    echo [ERREUR] Installation Python echouee.
    echo  Installez manuellement depuis https://python.org
    echo  IMPORTANT : ne pas utiliser la version Microsoft Store.
    echo.
    pause & exit /b 1
)
echo [OK] Python installe.
echo.
for /f "tokens=2*" %%A in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "_NP=%%B"
if defined _NP set "PATH=!_NP!;%PATH%"
set "_NP="

:have_python
set "_PY="
for /f "tokens=*" %%P in ('where python 2^>nul') do if not defined _PY set "_PY=%%P"
if not defined _PY (
    echo [ERREUR] python.exe introuvable dans PATH.
    pause & exit /b 1
)
set "_PYW=!_PY:python.exe=pythonw.exe!"
if not exist "!_PYW!" set "_PYW=!_PY!"
echo [INFO] Python  : !_PY!
echo [INFO] Pythonw : !_PYW!
echo.

:: =========================================================
:: 4/6 � PAQUETS PYTHON (BRIDGE)
:: =========================================================
echo [4/6] Installation des paquets Python (bridge)...
echo ---------------------------------------------------------
echo  Mise a jour de pip...
"!_PY!" -m pip install --upgrade --quiet pip
echo  Installation des paquets du bridge...
"!_PY!" -m pip install --quiet fastapi "uvicorn[standard]" requests psutil slowapi
if errorlevel 1 (
    echo [ERREUR] pip install a echoue.
    pause & exit /b 1
)
"!_PY!" -c "import fastapi, uvicorn, requests, psutil, slowapi; print('[OK] fastapi uvicorn requests psutil slowapi � OK')"
if errorlevel 1 (
    echo [ERREUR] Verification des paquets echouee.
    pause & exit /b 1
)
echo.

:: =========================================================
:: 5/6 � AETHER (proxy Open-Meteo)
:: =========================================================
echo [5/6] Installation d'Aether (proxy Open-Meteo)...
echo ---------------------------------------------------------
if not exist "!_AETHER!\main.py" (
    echo  Telechargement d'Aether depuis GitHub...
    set "_AZ=%TEMP%\aether_dl.zip"
    set "_AT=%TEMP%\aether_tmp"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://github.com/Mrtt555/Aether/archive/refs/heads/main.zip' -OutFile '%_AZ%' -UseBasicParsing -ErrorAction Stop"
    if errorlevel 1 (
        echo [ERREUR] Telechargement Aether echoue.
        pause & exit /b 1
    )
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive '%_AZ%' -DestinationPath '%_AT%' -Force; Move-Item '%_AT%\Aether-main' '!_AETHER!'; Remove-Item '%_AZ%','%_AT%' -Recurse -Force -ErrorAction SilentlyContinue"
    if not exist "!_AETHER!\main.py" (
        echo [ERREUR] Installation Aether echouee � main.py introuvable.
        pause & exit /b 1
    )
    echo [OK] Aether telecharge.
) else (
    echo [INFO] Aether deja present � paquets mis a jour uniquement.
)
"!_PY!" -m pip install --quiet -r "!_AETHER!\requirements.txt"
if errorlevel 1 (
    echo [ERREUR] Paquets Aether echec.
    pause & exit /b 1
)
echo [OK] Aether pret � interface sur http://127.0.0.1:8001
echo.

:: =========================================================
:: 6/6 � DEMARRAGE AUTOMATIQUE + LANCEMENT BRIDGE
:: =========================================================
echo [6/6] Demarrage automatique + lancement bridge...
echo ---------------------------------------------------------

:: Raccourci Startup Windows pour le bridge
set "_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SysViewBridge.bat"
>"!_SHORTCUT!" echo @echo off
>>"!_SHORTCUT!" echo start "" "!_PYW!" "!_APIV3!\SysViewBridge.pyw"
if exist "!_SHORTCUT!" (
    echo [OK] Bridge : demarrage automatique au login Windows configure.
) else (
    echo [AVERT] Raccourci de demarrage bridge non cree.
)
echo.

:: Liberer les ports 5001 et 8001 si occupes
echo  Verification des ports 5001 et 8001...
set "_PORT_BUSY=0"

if exist "!_APIV3!\bridge.pid" (
    for /f "usebackq tokens=*" %%i in ("!_APIV3!\bridge.pid") do taskkill /PID %%i /F /T >nul 2>&1
    del "!_APIV3!\bridge.pid" >nul 2>&1
)
for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr /r " 0\.0\.0\.0:5001 \|127\.0\.0\.1:5001 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" (
        echo [INFO] port 5001 occupe ^(PID %%P^) � arret...
        taskkill /PID %%P /F /T >nul 2>&1
        set "_PORT_BUSY=1"
    )
)
for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr /r " 0\.0\.0\.0:8001 \|127\.0\.0\.1:8001 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" (
        echo [INFO] port 8001 occupe ^(PID %%P^) � arret...
        taskkill /PID %%P /F /T >nul 2>&1
        set "_PORT_BUSY=1"
    )
)
if "!_PORT_BUSY!"=="1" (
    echo [OK] Ports liberes.
    ping -n 3 127.0.0.1 >nul
) else (
    echo [OK] ports 5001 et 8001 disponibles.
)
echo.

:: Lancer le bridge (demarre Aether en sous-processus)
echo  Lancement du bridge + Aether...
start "" "!_PYW!" "!_APIV3!\SysViewBridge.pyw"

echo  Attente du demarrage...
set "_WAIT=0"
:wait_loop
ping -n 3 127.0.0.1 >nul
set /a "_WAIT=!_WAIT!+3"
if exist "!_APIV3!\bridge.pid" goto :started
if !_WAIT! geq 15 goto :timeout
goto :wait_loop

:timeout
echo [AVERT] Bridge non detecte.
echo  Verifiez : !_APIV3!\logs\sysview.log
goto :launch_done

:started
echo [OK] Bridge demarre ^(port 5001^).

set "_AW=0"
:aether_wait
ping -n 4 127.0.0.1 >nul
set /a "_AW=!_AW!+4"
powershell -NoProfile -Command "try{$r=(Invoke-WebRequest 'http://127.0.0.1:8001' -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop).StatusCode; if($r -eq 200){exit 0}else{exit 1}}catch{exit 1}" >nul 2>&1
if not errorlevel 1 (
    echo [OK] Aether demarre ^(port 8001^).
    goto :launch_done
)
if !_AW! geq 20 (
    echo [AVERT] Aether ne repond pas encore ^(demarre en arriere-plan^).
    goto :launch_done
)
goto :aether_wait

:launch_done
echo.

:: =========================================================
:: FIN
:: =========================================================
set "_PY=" & set "_PYW="
echo.
echo  =========================================================
echo   Tout est installe et en cours d'execution !
echo  =========================================================
echo.
echo  Dossiers installes :
echo    SysView V6  : !_DEST!
echo    SysViewHardware : !_APIV3!\SysViewHardware.exe
echo.
echo  PROCHAINE ETAPE � Ouvrez Wallpaper Engine :
echo    - En bas de la bibliotheque : "Parcourir"
echo    - Selectionnez : !_DEST!\SysView.html
echo    - Dans Personnaliser : entrez votre ville
echo.
echo  Endpoints actifs :
echo    http://127.0.0.1:5001/v1/status   (diagnostic bridge)
echo    http://127.0.0.1:5001/v1/weather  (meteo)
echo    http://127.0.0.1:8001             (Aether - config meteo)
echo    http://127.0.0.1:8086/data.json   (SysViewHardware - capteurs)
echo.
echo  Demarrage automatique au login Windows :
echo    - Bridge + Aether : raccourci Startup
echo    - SysViewHardware : tache planifiee (avec droits admin)
echo.
endlocal
pause
