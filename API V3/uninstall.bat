@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo   SysView Bridge - Uninstall
echo ============================================
echo.

:: 1/4 - Stop bridge + Aether
echo [1/4] Stopping bridge and Aether...
if not exist "%~dp0bridge.pid" goto :skip_kill
set /p _PID=<"%~dp0bridge.pid"
if not defined _PID goto :skip_kill
taskkill /PID %_PID% /F /T >nul 2>&1
del "%~dp0bridge.pid" >nul 2>&1
echo [OK] Bridge stopped.

:skip_kill
echo.

:: 2/4 - pip uninstall bridge
echo [2/4] Removing Python packages (bridge)...
python -m pip uninstall -y fastapi "uvicorn[standard]" requests psutil slowapi
echo.

:: 3/4 - Aether packages + folder
echo [3/4] Removing Aether...
if not exist "%~dp0aether\requirements.txt" goto :skip_aether_pkg
python -m pip uninstall -y httpx pydantic python-multipart

:skip_aether_pkg
if not exist "%~dp0aether" goto :skip_aether_dir
powershell -NoProfile -Command "Remove-Item '%~dp0aether' -Recurse -Force -ErrorAction SilentlyContinue"
echo [OK] Aether folder deleted.

:skip_aether_dir
echo.

:: 4/4 - Startup shortcut
echo [4/4] Removing startup shortcut...
set "_S=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SysViewBridge.bat"
if not exist "%_S%" goto :skip_shortcut
del "%_S%"
echo [OK] Startup shortcut removed.

:skip_shortcut
echo.
echo ============================================
echo   Uninstall complete. Python not modified.
echo ============================================
echo.
endlocal
pause
