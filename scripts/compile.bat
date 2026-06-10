@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0.."

set "VF=%~dp0version.txt"
if not exist "%VF%" echo 6.5.0>"%VF%"
set /p OLD_VERSION=<"%VF%"

:: -- Increment via env var (evite le bug (x86) dans le chemin) --
set TF=%TEMP%\sysview_newver.tmp
set VF_ENV=%VF%
set TF_ENV=%TF%
powershell -NoProfile -Command "$v=(Get-Content $env:VF_ENV).Trim().Split('.');$v[2]=[string]([int]$v[2]+1);$nv=$v -join '.';Set-Content -NoNewline -Path $env:VF_ENV -Value $nv;Set-Content -NoNewline -Path $env:TF_ENV -Value $nv"
set /p NEW_VERSION=<"%TF%"
del "%TF%" >nul 2>nul

if "!NEW_VERSION!"=="" (
    echo  ERREUR : version.txt illisible
    pause & exit /b 1
)

echo.
echo  ================================================
echo   SysView V6  Build v!NEW_VERSION!
echo   (precedent : v%OLD_VERSION%)
echo  ================================================
echo.

:: -- 1. Compilation -------------------------------------------
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

:: -- 2. Commit + push master -----------------------------------
echo  [1/2] Commit + push master...
git add -A
git commit -m "build: v!NEW_VERSION!"
git push origin master

if %ERRORLEVEL% neq 0 (
    echo  ERREUR git push master.
    pause & exit /b 1
)

:: -- 3. Tag -> GitHub Actions Release -------------------------
echo.
echo  [2/2] Tag v!NEW_VERSION!...
git tag "v!NEW_VERSION!" 2>nul
if %ERRORLEVEL% neq 0 (
    git tag -d "v!NEW_VERSION!" >nul 2>nul
    git push origin --delete "v!NEW_VERSION!" >nul 2>nul
    git tag "v!NEW_VERSION!"
)
git push origin "v!NEW_VERSION!"

echo.
echo  ================================================
echo   v!NEW_VERSION! - Release en cours sur GitHub
echo   https://github.com/Mrtt555/SysView-V6/releases
echo  ================================================
echo.
pause
