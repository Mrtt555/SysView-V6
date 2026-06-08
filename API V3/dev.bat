@echo off
setlocal enabledelayedexpansion
title SysViewHardware -- Dev Build ^& Run

echo.
echo  =========================================================
echo   SysViewHardware -- Build rapide pour les tests
echo  =========================================================
echo.

:: ============================================================
:: 1. Tuer l'instance en cours (published + dev, meme nom)
:: ============================================================
taskkill /IM SysViewHardware.exe /F >nul 2>&1
if not errorlevel 1 (
    echo [OK] Instance precedente arretee.
    ping -n 2 127.0.0.1 >nul
) else (
    echo [INFO] Aucune instance en cours.
)
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
    echo [ERREUR] dotnet.exe introuvable.
    echo  Lancez setup.bat une premiere fois pour l'installer.
    pause ^& exit /b 1
)
echo [INFO] dotnet : !_DOTNET!

:: ============================================================
:: 3. Build Debug (rapide -- pas de single-file packing)
:: ============================================================
echo.
echo [BUILD] Compilation en cours...
set "_PROJ=%~dp0SysViewHardware\SysViewHardware.csproj"

"!_DOTNET!" build "!_PROJ!" -c Debug -r win-x64 --nologo -v minimal
if errorlevel 1 (
    echo.
    echo [ERREUR] Build echoue -- voir les erreurs ci-dessus.
    pause ^& exit /b 1
)
echo.

:: ============================================================
:: 4. Lancer avec elevation admin
:: ============================================================
set "_EXE=%~dp0SysViewHardware\bin\Debug\net8.0-windows\win-x64\SysViewHardware.exe"
if not exist "!_EXE!" (
    echo [ERREUR] Exe introuvable apres build : !_EXE!
    pause ^& exit /b 1
)

echo [LAUNCH] Demarrage (fenetre UAC attendue)...
powershell -NoProfile -Command "Start-Process '!_EXE!' -Verb RunAs"
echo.
echo [OK] SysViewHardware demarre sur le port 8086.
echo.
echo  Endpoints de test :
echo    http://127.0.0.1:8086/data.json
echo    http://127.0.0.1:8086/health
echo.
endlocal
pause