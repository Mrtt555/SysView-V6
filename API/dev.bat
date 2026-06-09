@echo off
setlocal enabledelayedexpansion
title SysView V6 -- Dev Build ^& Run

:: ============================================================
:: Auto-elevation : relance en admin si necessaire
:: Passage par $env:_SELF pour eviter les problemes de quotes
:: avec les espaces dans le chemin du projet.
:: ============================================================
net session >nul 2>&1
if errorlevel 1 (
    echo  Elevation necessaire ^(popup UAC^)...
    set "_SELF=%~f0"
    powershell -NoProfile -Command "Start-Process $env:_SELF -Verb RunAs"
    exit /b
)

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

for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr ":5001 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" taskkill /PID %%P /F /T >nul 2>&1
)
for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr ":8086 " ^| findstr "LISTENING"') do (
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
    goto :fail
)

:: ============================================================
:: 3. Localiser Python
:: ============================================================
set "_PY="
for /f "tokens=*" %%P in ('where python 2^>nul') do if not defined _PY set "_PY=%%P"
if not defined _PY (
    echo [ERREUR] python.exe introuvable -- lancez setup.bat une premiere fois.
    goto :fail
)
set "_PYW=!_PY:python.exe=pythonw.exe!"
if not exist "!_PYW!" set "_PYW=!_PY!"

:: ============================================================
:: 4. Publish SysViewHardware -- exe unique, self-contained
:: ============================================================
echo [BUILD] Publication SysViewHardware ^(exe unique^)...
set "_PROJ=%~dp0..\SysViewHardware\SysViewHardware.csproj"
if not exist "!_PROJ!" (
    echo [ERREUR] Projet introuvable : !_PROJ!
    goto :fail
)
"!_DOTNET!" publish "!_PROJ!" -c Release -r win-x64 --nologo -v minimal -p:DebugType=none
if errorlevel 1 (
    echo.
    echo [ERREUR] Publish echoue -- voir les erreurs ci-dessus.
    goto :fail
)
echo.

:: ============================================================
:: 5. Lancer SysViewHardware (deja admin -- pas de UAC)
:: ============================================================
set "_EXE=%~dp0..\SysViewHardware\bin\Release\net8.0-windows\win-x64\publish\SysViewHardware.exe"
if not exist "!_EXE!" (
    echo [ERREUR] Exe introuvable : !_EXE!
    goto :fail
)
echo [LAUNCH] SysViewHardware...
start "" "!_EXE!"
echo [OK] SysViewHardware -- port 8086.
echo.

:: ============================================================
:: 6. Lancer le Bridge (qui demarre Aether en interne)
:: ============================================================
echo [LAUNCH] Bridge + Aether...
start "" "!_PYW!" "%~dp0SysViewBridge.pyw"

set "_W=0"
:wait_bridge
ping -n 3 127.0.0.1 >nul
set /a "_W=!_W!+3"
if exist "%~dp0bridge.pid" goto :bridge_ok
if !_W! geq 18 (
    echo [AVERT] Bridge lent a demarrer -- verifier logs\sysview.log
    goto :done
)
goto :wait_bridge

:bridge_ok
echo [OK] Bridge -- port 5001.

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
exit /b 0

:fail
echo.
endlocal
pause
exit /b 1
