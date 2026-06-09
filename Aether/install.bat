@echo off
setlocal enabledelayedexpansion
title API Meteo - Installation

echo ============================================
echo   API Meteo ^& Environnement - Installateur
echo   Auteur : Mrtt555 (Astralcodes)  ^|  Version 1.1
echo ============================================
echo.

:: Verifier Python
python --version >nul 2>&1
if errorlevel 1 (
    echo [ERREUR] Python n'est pas installe ou pas dans le PATH.
    echo Telechargez Python sur https://www.python.org/downloads/
    pause
    exit /b 1
)
for /f "tokens=*" %%v in ('python --version 2^>^&1') do set PYVER=%%v
echo [OK] %PYVER% detecte.

:: Creer l'environnement virtuel
echo.
echo [1/3] Creation de l'environnement virtuel (.venv)...
if exist .venv (
    echo      Dossier .venv existant, on le conserve.
) else (
    python -m venv .venv
    if errorlevel 1 (
        echo [ERREUR] Impossible de creer l'environnement virtuel.
        pause
        exit /b 1
    )
    echo      [OK] Environnement virtuel cree.
)

:: Activer et installer les dependances
echo.
echo [2/3] Installation des dependances Python...
call .venv\Scripts\activate.bat
python -m pip install --upgrade pip --quiet
pip install -r requirements.txt --quiet
if errorlevel 1 (
    echo [ERREUR] L'installation des dependances a echoue.
    pause
    exit /b 1
)
echo      [OK] Toutes les dependances sont installees.

:: Creer le script de lancement
echo.
echo [3/3] Creation du script de lancement (launch.bat)...
(
    echo @echo off
    echo title API Meteo - Serveur
    echo call .venv\Scripts\activate.bat
    echo echo Demarrage du serveur sur http://localhost:8000
    echo echo Appuyez sur Ctrl+C pour arreter.
    echo echo.
    echo uvicorn main:app --host 0.0.0.0 --port 8000 --reload
) > launch.bat
echo      [OK] launch.bat cree.

echo.
echo ============================================
echo   Installation terminee avec succes !
echo ============================================
echo.
echo   Pour demarrer le serveur : launch.bat
echo   Interface web             : http://localhost:8000
echo   Documentation Swagger     : http://localhost:8000/docs
echo.

set /p LAUNCH="Lancer le serveur maintenant ? (o/n) : "
if /i "!LAUNCH!"=="o" (
    call launch.bat
)

endlocal
