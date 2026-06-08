@echo off
setlocal enabledelayedexpansion
title SysView V6 -- Dev Build ^& Run

echo.
echo  =========================================================
echo   SysView V6 -- Dev Build ^& Run
echo  =========================================================
echo.

:: ============================================================
:: 1. Tuer les instances en cours
:: ============================================================
echo [STOP] Arret des services...
taskkill /IM SysViewHardware.exe /F >nul 2>&1
if not errorlevel 1 echo  SysViewHardware arrete.

if exist "%~dp0bridge.pid" (
    for /f "usebackq tokens=*" %%i in ("%~dp0bridge.pid") do taskkill /PID %%i /F /T >nul 2>&1
    del "%~dp0bridge.pid" >nul 2>&1
    echo  Bridge arrete.
)

for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr /r "127\.0\.0\.1:5001 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" taskkill /PID %%P /F /T >nul 2>&1
)
for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr /r "127\.0\.0\.1:8086 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" taskkill /PID %%P /F /T >nul 2>&1
)
ping -n 2 127.0.0.1 >nul
echo.

:: ============================================================
:: 2. Localiser dotnet
:: ============================================================
set "_DOTNET="
for /f "tokens=*" %%D in ('where dotnet 2^>nul') do if not defined _DOTNET set "_DOTNET=%%D"
if not defined _DOTNET (
    if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "_DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
)
if not defined _DOTNET (
    echo [ERREUR] dotnet.exe introuvable -- lancez setup.bat une premiere fois.
    pause ^& exit /b 1
)

:: ============================================================
:: 3. Localiser Python
:: ============================================================
set "_PY="
for /f "tokens=*" %%P in ('where python 2^>nul') do if not defined _PY set "_PY=%%P"
if not defined _PY (
    echo [ERREUR] python.exe introuvable -- lancez setup.bat une premiere fois.
    pause ^& exit /b 1
)
set "_PYW=!_PY:python.exe=pythonw.exe!"
if not exist "!_PYW!" set "_PYW=!_PY!"

:: ============================================================
:: 4. Build Debug SysViewHardware (incremental -- rapide)
:: ============================================================
echo [BUILD] Compilation SysViewHardware ^(Debug^)...
set "_PROJ=%~dp0SysViewHardware\SysViewHardware.csproj"
"!_DOTNET!" build "!_PROJ!" -c Debug -r win-x64 --nologo -v minimal
if errorlevel 1 (
    echo.
    echo [ERREUR] Build echoue -- voir les erreurs ci-dessus.
    pause ^& exit /b 1
)
echo.

:: ============================================================
:: 5. Lancer SysViewHardware avec elevation admin
:: ============================================================
set "_EXE=%~dp0SysViewHardware\bin\Debug\net8.0-windows\win-x64\SysViewHardware.exe"
if not exist "!_EXE!" (
    echo [ERREUR] Exe introuvable apres build : !_EXE!
    pause ^& exit /b 1
)
echo [LAUNCH] SysViewHardware ^(fenetre UAC attendue^)...
powershell -NoProfile -Command "Start-Process '!_EXE!' -Verb RunAs"
echo [OK] SysViewHardware demarre -- port 8086.
echo.

:: ============================================================
:: 6. Lancer le bridge (demarre Aether en sous-processus)
:: ============================================================
echo [LAUNCH] Bridge + Aether...
start "" "!_PYW!" "%~dp0SysViewBridge.pyw"

set "_W=0"
:wait_bridge
ping -n 3 127.0.0.1 >nul
set /a "_W=!_W!+3"
if exist "%~dp0bridge.pid" goto :bridge_ok
if !_W! geq 15 (
    echo [AVERT] Bridge lent a demarrer -- verifier logs\sysview.log
    goto :done
)
goto :wait_bridge

:bridge_ok
echo [OK] Bridge demarre -- port 5001.

:done
echo.
echo  =========================================================
echo   Tous les services sont lances.
echo  =========================================================
echo.
echo    http://127.0.0.1:8086/data.json   ^(SysViewHardware^)
echo    http://127.0.0.1:5001/v1/status   ^(Bridge^)
echo    http://127.0.0.1:8001             ^(Aether^)
echo.
endlocal
pause
