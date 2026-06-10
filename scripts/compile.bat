@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

:: ============================================================
::  SysView V6 - compile.bat
::  1. Incremente le patch dans version.txt
::  2. Compile via publish.ps1
::  3. Push source -> branche SysViewManager
::  4. Tag vX.Y.Z -> GitHub Actions cree le Release + exe
:: ============================================================

set "VF=%~dp0version.txt"
if not exist "%VF%" echo 6.5.0>"%VF%"
set /p OLD_VERSION=<"%VF%"

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command ^
    "$v=(gc '%VF%').Trim().Split('.'); [int]$v[2]++; $nv=$v -join '.'; $nv | sc '%VF%'; $nv"`) do (
    set "NEW_VERSION=%%v"
)

if "!NEW_VERSION!"=="" (
    echo  ERREUR : impossible de lire version.txt
    pause & exit /b 1
)

echo.
echo  ================================================
echo   SysView V6  Build v!NEW_VERSION!
echo   (precedent : v%OLD_VERSION%)
echo  ================================================
echo.

:: -- 1. Compilation locale ------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" -Version "!NEW_VERSION!" -Kill

if %ERRORLEVEL% neq 0 (
    echo.
    echo  ECHEC - version.txt remis a v%OLD_VERSION%
    echo %OLD_VERSION%>"%VF%"
    pause & exit /b 1
)

echo.
echo  Build OK  -  v!NEW_VERSION!
echo.

:: -- 2. Push source -> branche SysViewManager -----------------
echo  [1/2] Push source GitHub (SysViewManager)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0push-backend.ps1" -Message "build: v!NEW_VERSION!"

if %ERRORLEVEL% neq 0 (
    echo  ERREUR push-backend - arret.
    pause & exit /b 1
)

:: -- 3. Tag vX.Y.Z -> declenche GitHub Actions + Release ------
echo.
echo  [2/2] Tag v!NEW_VERSION! -> GitHub Actions (Release + exe)...
cd /d "%~dp0.."
git tag "v!NEW_VERSION!" 2>nul
if %ERRORLEVEL% neq 0 (
    echo  Tag v!NEW_VERSION! existe deja - suppression et recreation...
    git tag -d "v!NEW_VERSION!" >nul 2>nul
    git push origin --delete "v!NEW_VERSION!" >nul 2>nul
    git tag "v!NEW_VERSION!"
)
git push origin "v!NEW_VERSION!"

if %ERRORLEVEL% neq 0 (
    echo  ERREUR push tag - verifiez votre connexion.
    pause & exit /b 1
)

echo.
echo  ================================================
echo   v!NEW_VERSION! - Release en cours sur GitHub Actions
echo   https://github.com/Mrtt555/SysView-V6/releases
echo  ================================================
echo.
pause