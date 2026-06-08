@echo off
setlocal
cd /d "%~dp0"
title SysView Bridge - Stop
echo.
echo ============================================
echo  SysView Bridge v5 - Stop
echo ============================================
echo.

if not exist "bridge.pid" goto :not_running

for /f "usebackq tokens=*" %%i in ("bridge.pid") do set "BRIDGE_PID=%%i"
echo Stopping bridge (PID %BRIDGE_PID%)...
taskkill /PID %BRIDGE_PID% /F /T >nul 2>&1
if errorlevel 1 goto :already_stopped
echo [OK] Bridge stopped.
del "bridge.pid" >nul 2>&1
goto :done

:already_stopped
echo [WARN] Process %BRIDGE_PID% was already stopped.
del "bridge.pid" >nul 2>&1
goto :done

:not_running
echo [INFO] Bridge is not running.

:done
echo.
endlocal
pause
