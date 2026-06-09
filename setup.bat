@echo off
setlocal enabledelayedexpansion

:: =========================================================
:: LOG -- ecrit dans %TEMP% pendant l'execution,
::        copie dans !_DEST!\logs\setup.log a la fin.
::        (le dossier !_DEST!\ est efface a l'etape 1/6,
::         le fichier temporaire survit.)
:: =========================================================
set "_LOGFILE=%TEMP%\sysview_setup.log"
(
  echo [%DATE% %TIME:~0,8%] ================================================
  echo [%DATE% %TIME:~0,8%] SysView V6 -- Setup
  echo [%DATE% %TIME:~0,8%] ================================================
) > "%_LOGFILE%"

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
set "_API=!_DEST!\API"
set "_AETHER=!_DEST!\Aether"
set "_HW_DIR=!_DEST!\SysViewHardware"

echo.
echo  ^>^>^> SysView V6 : !_DEST!
echo.
call :log "Chemin cible : !_DEST!"
pause

:: Verifier que le dossier parent SysView existe
if not exist "!_BASE!\" (
    call :log "[ERREUR] Dossier introuvable : !_BASE!"
    echo.
    echo  Conseil : dans Wallpaper Engine, faites un clic droit
    echo  sur un wallpaper -^> "Ouvrir le dossier" pour trouver
    echo  le bon chemin myprojects.
    echo.
    pause & exit /b 1
)

if exist "!_DEST!\" (
    echo  [AVERT] SysView V6 est deja installe.
    echo  Les fichiers seront mis a jour ^(runtime_config.json conserve, Aether retelecharge^).
    echo.
    set /p "_OK=  Continuer ? [O / N] : "
    if /i "!_OK:~0,1!" NEQ "O" (
        call :log "[INFO] Installation annulee par l'utilisateur."
        pause
        exit /b 0
    )
    echo.
)

:: 1/6 -- SysView V6 (GitHub ou local)
:: =========================================================
call :log "[1/6] Installation / mise a jour de SysView V6..."
echo ---------------------------------------------------------

:: -- Detecter si setup.bat tourne depuis !_DEST! --------
set "_SETUP_DIR=%~dp0"
if "!_SETUP_DIR:~-1!"=="\" set "_SETUP_DIR=!_SETUP_DIR:~0,-1!"

if /i "!_SETUP_DIR!"=="!_DEST!" (
    rem Le script tourne depuis le dossier cible : fichiers deja presents, on saute le telechargement.
    call :log "[1/6] Script lance depuis le dossier cible -- telechargement ignore."
    echo  ^(fichiers locaux deja a jour -- telechargement GitHub ignore^)
    goto :step2
)

:: -- Telechargement depuis GitHub (repo public ou git disponible) --
set "_ZIP=%TEMP%\sysview_setup.zip"
set "_TMP=%TEMP%\sysview_setup_tmp"

if exist "!_TMP!" powershell -NoProfile -Command "Remove-Item '!_TMP!' -Recurse -Force -ErrorAction SilentlyContinue"
if exist "!_ZIP!" del "!_ZIP!" >nul 2>&1

echo  Connexion a GitHub...
rem Essai 1 : git clone (utilise les credentials Windows, fonctionne sur repo prive)
where git >nul 2>&1
if not errorlevel 1 (
    git clone --depth 1 --branch master "https://github.com/Mrtt555/SysView-V6.git" "!_TMP!\SysView-V6-master" >> "%_LOGFILE%" 2>&1
    if errorlevel 1 (
        call :log "[WARN] git clone echoue -- tentative Invoke-WebRequest..."
        if exist "!_TMP!" powershell -NoProfile -Command "Remove-Item '!_TMP!' -Recurse -Force -ErrorAction SilentlyContinue"
        goto :dl_web
    )
    goto :dl_done
)

:dl_web
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $ProgressPreference='SilentlyContinue'; Invoke-WebRequest 'https://github.com/Mrtt555/SysView-V6/archive/refs/heads/master.zip' -OutFile '!_ZIP!' -UseBasicParsing -ErrorAction Stop" >> "%_LOGFILE%" 2>&1
if errorlevel 1 (
    call :log "[ERREUR] Telechargement echoue -- details dans : %TEMP%\sysview_setup.log"
    pause & exit /b 1
)
echo  Extraction...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive '!_ZIP!' -DestinationPath '!_TMP!' -Force" >> "%_LOGFILE%" 2>&1
if errorlevel 1 (
    call :log "[ERREUR] Extraction echouee."
    pause & exit /b 1
)
del "!_ZIP!" >nul 2>&1

:dl_done
set "_SRC="
for /d %%D in ("!_TMP!\*") do if not defined _SRC set "_SRC=%%D"
if not defined _SRC (
    call :log "[ERREUR] Archive vide ou structure inattendue."
    pause & exit /b 1
)

:: Sauvegarder API\runtime_config.json
set "_BCK=%TEMP%\sysview_bck"
if exist "!_BCK!" powershell -NoProfile -Command "Remove-Item '!_BCK!' -Recurse -Force -ErrorAction SilentlyContinue"
mkdir "!_BCK!" >nul 2>&1
if exist "!_API!\runtime_config.json" copy /y "!_API!\runtime_config.json" "!_BCK!\runtime_config.json" >nul 2>&1

:: Remplacer / installer (robocopy sur place ou Move-Item si nouvelle install)
if exist "!_DEST!\" (
    robocopy "!_SRC!" "!_DEST!" /E /IS /IT /PURGE /XF "runtime_config.json" "setup.bat" /XD "logs" >nul 2>&1
    if !errorlevel! GEQ 8 (
        call :log "[ERREUR] Mise a jour echouee (robocopy code !errorlevel!)."
        pause & exit /b 1
    )
) else (
    powershell -NoProfile -Command "Move-Item '!_SRC!' '!_DEST!'"
    if errorlevel 1 (
        call :log "[ERREUR] Installation initiale : deplacement echoue."
        pause & exit /b 1
    )
)
if not exist "!_DEST!\SysView.html" (
    call :log "[ERREUR] SysView.html introuvable apres extraction."
    pause & exit /b 1
)
powershell -NoProfile -Command "Remove-Item '!_TMP!' -Recurse -Force -ErrorAction SilentlyContinue"

:: Restaurer runtime_config.json
if exist "!_BCK!\runtime_config.json" copy /y "!_BCK!\runtime_config.json" "!_API!\runtime_config.json" >nul 2>&1
powershell -NoProfile -Command "Remove-Item '!_BCK!' -Recurse -Force -ErrorAction SilentlyContinue"

call :log "[OK] SysView V6 installe : !_DEST!"
echo.

:step2
:: =========================================================
:: 2/6 -- SYSVIEWHARDWARE (capteurs C# via LibreHardwareMonitorLib)
:: =========================================================
call :log "[2/6] Compilation de SysViewHardware..."
echo ---------------------------------------------------------

set "_HW_EXE=!_HW_DIR!\bin\Release\net8.0-windows\win-x64\publish\SysViewHardware.exe"
set "_HW_PROJ=!_HW_DIR!\SysViewHardware.csproj"

if exist "!_HW_EXE!" (
    call :log "[INFO] SysViewHardware.exe deja present -- compilation ignoree."
    goto :hw_startup
)

:: Verifier si .NET 8+ SDK est disponible
set "_DOTNET="
for /f "tokens=*" %%D in ('where dotnet 2^>nul') do if not defined _DOTNET set "_DOTNET=%%D"

if defined _DOTNET (
    set "_SDK_OK=0"
    for /f "tokens=1 delims=." %%V in ('dotnet --version 2^>nul') do (set /a "_SDKVER=%%V" >nul & if !_SDKVER! GEQ 8 set "_SDK_OK=1")
    if not "!_SDK_OK!"=="1" set "_DOTNET="
)

if not defined _DOTNET (
    echo  SDK .NET 8 introuvable -- installation ^(~200 Mo, quelques minutes^)...
    >> "%_LOGFILE%" echo [%TIME:~0,8%] SDK .NET 8 absent -- telechargement...
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "& { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $ProgressPreference='SilentlyContinue'; $f='%TEMP%\dotnet-install.ps1'; Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $f -UseBasicParsing -ErrorAction Stop; & $f -Channel 8.0 -InstallDir '$env:USERPROFILE\.dotnet' }" >> "%_LOGFILE%" 2>&1
    if errorlevel 1 (
        call :log "[ERREUR] Installation SDK .NET 8 echouee."
        pause & exit /b 1
    )
    set "_DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
)

if not exist "!_HW_PROJ!" (
    call :log "[ERREUR] Projet introuvable : !_HW_PROJ!"
    goto :fail
)

echo  Compilation en cours ^(premiere fois ~2-3 min -- NuGet + build^)...
"!_DOTNET!" publish "!_HW_PROJ!" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none --nologo -v quiet >> "%_LOGFILE%" 2>&1
if errorlevel 1 (
    call :log "[ERREUR] Compilation SysViewHardware echouee."
    echo  Verifiez que le projet est intact : !_HW_PROJ!
    pause & exit /b 1
)

if not exist "!_HW_EXE!" (
    call :log "[ERREUR] SysViewHardware.exe introuvable apres compilation."
    pause & exit /b 1
)
call :log "[OK] SysViewHardware.exe compile (autonome)."

:hw_startup
:: --- Demarrage automatique avec privileges admin via Task Scheduler ---
echo  Demarrage automatique ^(admin^) : configuration...
schtasks /query /tn "SysViewHardware" >nul 2>&1
if not errorlevel 1 (
    call :log "[INFO] Tache planifiee SysViewHardware deja presente."
    goto :hw_launch
)
schtasks /create /tn "SysViewHardware" /tr "\"!_HW_EXE!\"" /sc ONLOGON /rl HIGHEST /f >nul 2>&1
if not errorlevel 1 (
    call :log "[OK] Tache planifiee : SysViewHardware demarre au login avec droits admin."
    goto :hw_launch
)
:: Fallback sans droits admin
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "SysViewHardware" /t REG_SZ /d "\"!_HW_EXE!\"" /f >nul 2>&1
call :log "[AVERT] Tache planifiee impossible (droits insuffisants) -- demarrage simple configure."
echo  Pour les capteurs : clic droit "Executer en tant qu'administrateur".

:hw_launch
echo  Lancement de SysViewHardware ^(sans UAC via tache planifiee^)...
schtasks /run /tn "SysViewHardware" >nul 2>&1
if errorlevel 1 (
    rem Tache planifiee indisponible -- fallback avec UAC
    echo  ^(tache planifiee indisponible -- UAC necessaire^)
    powershell -NoProfile -Command "Start-Process '!_HW_EXE!' -Verb RunAs"
)
call :log "[OK] SysViewHardware lance (port 8086)."
echo.

:: 3/6 -- PYTHON
:: =========================================================
call :log "[3/6] Verification de Python..."
echo ---------------------------------------------------------
python --version >nul 2>&1
if not errorlevel 1 goto :have_python

echo  Python introuvable -- telechargement automatique...
echo  (quelques minutes selon votre connexion)
>> "%_LOGFILE%" echo [%TIME:~0,8%] Python absent -- telechargement...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $ProgressPreference='SilentlyContinue'; $r=(Invoke-WebRequest 'https://www.python.org/downloads/' -UseBasicParsing -ErrorAction Stop).Content; $v=([regex]'Download Python (\d+\.\d+\.\d+)').Match($r).Groups[1].Value; if(!$v){throw 'Version introuvable'}; Write-Host('  -> Python '+$v); $f=$env:TEMP+'\pysetup.exe'; Invoke-WebRequest('https://www.python.org/ftp/python/'+$v+'/python-'+$v+'-amd64.exe') -OutFile $f -UseBasicParsing -ErrorAction Stop; Start-Process -Wait $f '/quiet InstallAllUsers=0 PrependPath=1 Include_test=0'; Remove-Item $f -ErrorAction SilentlyContinue }" >> "%_LOGFILE%" 2>&1
if errorlevel 1 (
    echo.
    call :log "[ERREUR] Installation Python echouee."
    echo  Installez manuellement depuis https://python.org
    echo  IMPORTANT : ne pas utiliser la version Microsoft Store.
    echo.
    pause & exit /b 1
)
call :log "[OK] Python installe."
echo.
for /f "tokens=2*" %%A in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "_NP=%%B"
if defined _NP set "PATH=!_NP!;%PATH%"
set "_NP="

:have_python
set "_PY="
for /f "tokens=*" %%P in ('where python 2^>nul') do if not defined _PY set "_PY=%%P"
if not defined _PY (
    call :log "[ERREUR] python.exe introuvable dans PATH."
    pause & exit /b 1
)
set "_PYW=!_PY:python.exe=pythonw.exe!"
if not exist "!_PYW!" set "_PYW=!_PY!"
call :log "[INFO] Python  : !_PY!"
call :log "[INFO] Pythonw : !_PYW!"
echo.

:: =========================================================
:: 4/6 -- PAQUETS PYTHON (BRIDGE + AETHER)
:: =========================================================
call :log "[4/6] Installation des paquets Python (bridge + Aether)..."
echo ---------------------------------------------------------
echo  Mise a jour de pip...
"!_PY!" -m pip install --upgrade --quiet pip >> "%_LOGFILE%" 2>&1
echo  Installation des paquets...
"!_PY!" -m pip install --quiet fastapi "uvicorn[standard]" requests psutil slowapi httpx python-multipart "pydantic>=2.7.0" >> "%_LOGFILE%" 2>&1
if errorlevel 1 (
    call :log "[ERREUR] pip install a echoue."
    pause & exit /b 1
)
"!_PY!" -c "import fastapi, uvicorn, requests, psutil, slowapi, httpx, multipart, pydantic; print('[OK] fastapi uvicorn requests psutil slowapi httpx python-multipart pydantic -- OK')" >> "%_LOGFILE%" 2>&1
if errorlevel 1 (
    call :log "[ERREUR] Verification des paquets echouee."
    pause & exit /b 1
)
call :log "[OK] fastapi uvicorn requests psutil slowapi httpx python-multipart pydantic -- OK"
echo.

:: =========================================================
:: 5/6 -- AETHER (proxy Open-Meteo)
:: =========================================================
call :log "[5/6] Installation d'Aether (proxy Open-Meteo)..."
echo ---------------------------------------------------------
if not exist "!_AETHER!\main.py" (
    echo  Telechargement d'Aether depuis GitHub...
    set "_AZ=%TEMP%\aether_dl.zip"
    set "_AT=%TEMP%\aether_tmp"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $ProgressPreference='SilentlyContinue'; Invoke-WebRequest 'https://github.com/Mrtt555/Aether/archive/refs/heads/main.zip' -OutFile '!_AZ!' -UseBasicParsing -ErrorAction Stop" >> "%_LOGFILE%" 2>&1
    if errorlevel 1 (
        call :log "[ERREUR] Telechargement Aether echoue -- details dans : %TEMP%\sysview_setup.log"
        pause & exit /b 1
    )
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive '!_AZ!' -DestinationPath '!_AT!' -Force; $sub=(Get-ChildItem '!_AT!' -Directory | Select-Object -First 1).FullName; Move-Item $sub '!_AETHER!'; Remove-Item '!_AZ!','!_AT!' -Recurse -Force -ErrorAction SilentlyContinue" >> "%_LOGFILE%" 2>&1
    if not exist "!_AETHER!\main.py" (
        call :log "[ERREUR] Installation Aether echouee -- main.py introuvable."
        pause & exit /b 1
    )
    call :log "[OK] Aether telecharge."
) else (
    call :log "[INFO] Aether deja present -- paquets mis a jour uniquement."
)
"!_PY!" -m pip install --quiet -r "!_AETHER!\requirements.txt" >> "%_LOGFILE%" 2>&1
if errorlevel 1 (
    call :log "[ERREUR] Paquets Aether echec."
    pause & exit /b 1
)
call :log "[OK] Aether pret -- interface sur http://127.0.0.1:8001"
echo.

:: =========================================================
:: 6/6 -- DEMARRAGE AUTOMATIQUE + LANCEMENT BRIDGE
:: =========================================================
call :log "[6/6] Demarrage automatique + lancement bridge..."
echo ---------------------------------------------------------

:: Raccourci Startup Windows pour le bridge
set "_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SysViewBridge.bat"
>"!_SHORTCUT!" echo @echo off
>>"!_SHORTCUT!" echo start "" "!_PYW!" "!_API!\SysViewBridge.pyw"
if exist "!_SHORTCUT!" (
    call :log "[OK] Bridge : demarrage automatique au login Windows configure."
) else (
    call :log "[AVERT] Raccourci de demarrage bridge non cree."
)
echo.

:: Liberer les ports 5001 et 8001 si occupes
echo  Verification des ports 5001 et 8001...
set "_PORT_BUSY=0"

if exist "!_API!\bridge.pid" (
    for /f "usebackq tokens=*" %%i in ("!_API!\bridge.pid") do taskkill /PID %%i /F /T >nul 2>&1
    del "!_API!\bridge.pid" >nul 2>&1
)
for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr ":5001 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" (
        call :log "[INFO] port 5001 occupe (PID %%P) -- arret..."
        taskkill /PID %%P /F /T >nul 2>&1
        set "_PORT_BUSY=1"
    )
)
for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr ":8001 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" (
        call :log "[INFO] port 8001 occupe (PID %%P) -- arret..."
        taskkill /PID %%P /F /T >nul 2>&1
        set "_PORT_BUSY=1"
    )
)
if "!_PORT_BUSY!"=="1" (
    call :log "[OK] Ports liberes."
    ping -n 3 127.0.0.1 >nul
) else (
    call :log "[OK] Ports 5001 et 8001 disponibles."
)
echo.

:: Lancer le bridge (demarre Aether en sous-processus)
echo  Lancement du bridge + Aether...
start "" "!_PYW!" "!_API!\SysViewBridge.pyw"

echo  Attente du demarrage...
set "_WAIT=0"
:wait_loop
ping -n 3 127.0.0.1 >nul
set /a "_WAIT=!_WAIT!+3"
if exist "!_API!\bridge.pid" goto :started
if !_WAIT! geq 15 goto :timeout
goto :wait_loop

:timeout
call :log "[AVERT] Bridge non detecte -- verifiez !_API!\logs\sysview.log"
goto :launch_done

:started
call :log "[OK] Bridge demarre (port 5001)."

set "_AW=0"
:aether_wait
ping -n 4 127.0.0.1 >nul
set /a "_AW=!_AW!+4"
powershell -NoProfile -Command "try{$r=(Invoke-WebRequest 'http://127.0.0.1:8001' -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop).StatusCode; if($r -eq 200){exit 0}else{exit 1}}catch{exit 1}" >nul 2>&1
if not errorlevel 1 (
    call :log "[OK] Aether demarre (port 8001)."
    goto :launch_done
)
if !_AW! geq 20 (
    call :log "[AVERT] Aether ne repond pas encore (demarre en arriere-plan)."
    goto :launch_done
)
goto :aether_wait

:launch_done
echo.

:: =========================================================
:: COPIE DU LOG DANS !_DEST!\logs\setup.log
:: =========================================================
>> "%_LOGFILE%" echo [%TIME:~0,8%] ================================================
>> "%_LOGFILE%" echo [%TIME:~0,8%] Setup termine.
>> "%_LOGFILE%" echo [%TIME:~0,8%] ================================================
if not exist "!_DEST!\logs" mkdir "!_DEST!\logs"
copy /y "%_LOGFILE%" "!_DEST!\logs\setup.log" >nul 2>&1
echo  Log : !_DEST!\logs\setup.log

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
echo    SysView V6      : !_DEST!
echo    Bridge          : !_API!\SysViewBridge.pyw
echo    Aether          : !_AETHER!
echo    SysViewHardware : !_HW_EXE!
echo.
echo  PROCHAINE ETAPE -- Ouvrez Wallpaper Engine :
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
goto :eof

:: =========================================================
:: SOUS-ROUTINES
:: =========================================================

:log
echo %~1
>> "%_LOGFILE%" echo [%TIME:~0,8%] %~1
goto :eof

:fail
echo.
>> "%_LOGFILE%" echo [%TIME:~0,8%] [ECHEC] Setup interrompu.
if not exist "!_DEST!\logs" mkdir "!_DEST!\logs" >nul 2>&1
copy /y "%_LOGFILE%" "!_DEST!\logs\setup.log" >nul 2>&1
endlocal
pause
exit /b 1
