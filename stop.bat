@echo off
setlocal enabledelayedexpansion

:: =========================================================
::  SysView V6 -- Arret des services
:: =========================================================

set "_DEST=%~dp0"
if "!_DEST:~-1!"=="\" set "_DEST=!_DEST:~0,-1!"
set "_API=!_DEST!\API"
set "_LOGFILE=!_DEST!\logs\stop.log"

if not exist "!_DEST!\logs\" mkdir "!_DEST!\logs"

(
    echo ================================================
    echo SysView V6 -- Arret  [%DATE% %TIME%]
    echo ================================================
) > "!_LOGFILE!"

echo.
echo  =========================================================
echo   SysView V6 -- Arret des services
echo  =========================================================
echo.

:: ---------------------------------------------------------
:: 1. SysViewHardware  (port 8086 -- exe nomme)
:: ---------------------------------------------------------
echo  [1/3] Arret de SysViewHardware...
set "_HW_STOPPED=0"

tasklist /FI "IMAGENAME eq SysViewHardware.exe" 2>nul | findstr /i "SysViewHardware.exe" >nul
if not errorlevel 1 (
    taskkill /IM SysViewHardware.exe /F /T >> "!_LOGFILE!" 2>&1
    if not errorlevel 1 (
        set "_HW_STOPPED=1"
        echo [%TIME:~0,8%] [OK] SysViewHardware.exe arrete. >> "!_LOGFILE!"
    )
)

rem Fallback : tuer par port 8086 si le nom de processus ne correspond pas
if "!_HW_STOPPED!"=="0" (
    for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr ":8086 " ^| findstr "LISTENING"') do (
        if not "%%P"=="0" (
            taskkill /PID %%P /F /T >> "!_LOGFILE!" 2>&1
            echo [%TIME:~0,8%] [OK] SysViewHardware arrete via PID %%P (port 8086). >> "!_LOGFILE!"
            set "_HW_STOPPED=1"
        )
    )
)

if "!_HW_STOPPED!"=="1" (
    echo      [OK] SysViewHardware arrete.
) else (
    echo      [--] SysViewHardware non detecte (deja arrete^).
    echo [%TIME:~0,8%] [INFO] SysViewHardware non detecte -- deja arrete. >> "!_LOGFILE!"
)

:: ---------------------------------------------------------
:: 2. Bridge  (port 5001 -- SysViewBridge.pyw via pythonw)
:: ---------------------------------------------------------
echo  [2/3] Arret du bridge...
set "_BR_STOPPED=0"

rem Essai 1 : via bridge.pid
if exist "!_API!\bridge.pid" (
    for /f "usebackq tokens=* delims=" %%i in ("!_API!\bridge.pid") do (
        if not "%%i"=="" (
            taskkill /PID %%i /F /T >> "!_LOGFILE!" 2>&1
            echo [%TIME:~0,8%] [OK] Bridge arrete via PID %%i (bridge.pid). >> "!_LOGFILE!"
            set "_BR_STOPPED=1"
        )
    )
    del "!_API!\bridge.pid" >nul 2>&1
)

rem Essai 2 : via port 5001
for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr ":5001 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" (
        taskkill /PID %%P /F /T >> "!_LOGFILE!" 2>&1
        echo [%TIME:~0,8%] [OK] Bridge arrete via PID %%P (port 5001). >> "!_LOGFILE!"
        set "_BR_STOPPED=1"
    )
)

if "!_BR_STOPPED!"=="1" (
    echo      [OK] Bridge arrete.
) else (
    echo      [--] Bridge non detecte (deja arrete^).
    echo [%TIME:~0,8%] [INFO] Bridge non detecte -- deja arrete. >> "!_LOGFILE!"
)

:: ---------------------------------------------------------
:: 3. Aether  (port 8001 -- sous-processus Python uvicorn)
:: ---------------------------------------------------------
echo  [3/3] Arret d'Aether...
set "_AT_STOPPED=0"

for /f "tokens=5" %%P in ('netstat -ano 2^>nul ^| findstr ":8001 " ^| findstr "LISTENING"') do (
    if not "%%P"=="0" (
        taskkill /PID %%P /F /T >> "!_LOGFILE!" 2>&1
        echo [%TIME:~0,8%] [OK] Aether arrete via PID %%P (port 8001). >> "!_LOGFILE!"
        set "_AT_STOPPED=1"
    )
)

if "!_AT_STOPPED!"=="1" (
    echo      [OK] Aether arrete.
) else (
    echo      [--] Aether non detecte (deja arrete^).
    echo [%TIME:~0,8%] [INFO] Aether non detecte -- deja arrete. >> "!_LOGFILE!"
)

:: ---------------------------------------------------------
:: Bilan
:: ---------------------------------------------------------
echo.
echo  =========================================================
echo   Services arretes. Log : !_LOGFILE!
echo  =========================================================
echo.
echo [%TIME:~0,8%] Arret termine. >> "!_LOGFILE!"

pause
endlocal
