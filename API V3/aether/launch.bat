@echo off
title API Meteo - Serveur
call .venv\Scripts\activate.bat
echo Demarrage du serveur sur http://localhost:8000
echo Appuyez sur Ctrl+C pour arreter.
echo.
.venv\Scripts\uvicorn.exe main:app --host 0.0.0.0 --port 8000 --reload
pause