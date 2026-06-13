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
powershell -NoProfile -Command "$v=(Get-Content $env:VF_ENV).Trim().Split('.');$p=[int]$v[2]+1;if($p -ge 10){$v[1]=[string]([int]$v[1]+1);$v[2]='0'}else{$v[2]=[string]$p};$nv=$v -join '.';Set-Content -NoNewline -Path $env:VF_ENV -Value $nv;Set-Content -NoNewline -Path $env:TF_ENV -Value $nv"
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

:: -- Copie exe a la racine ------------------------------------
set "EXE_SRC=%~dp0..\SysViewManager\bin\Release\net8.0-windows10.0.17763.0\win-x64\publish\SysViewManager.exe"
set "EXE_DST=%~dp0..\SysViewManager.exe"
if exist "!EXE_SRC!" (
    copy /Y "!EXE_SRC!" "!EXE_DST!" >nul
    echo  SysViewManager.exe copie a la racine.
) else (
    echo  ATTENTION : exe introuvable, copie ignoree.
)
echo.

:: -- 1b. Compilation setup Inno Setup -------------------------
set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "!ISCC!" (
    echo  Compilation SysViewV6_Setup.exe...
    "!ISCC!" /Q "/DAppVersion=!NEW_VERSION!" "%~dp0..\installer\setup.iss"
    if !ERRORLEVEL! neq 0 (
        echo  ATTENTION : Inno Setup a echoue - setup non genere.
    ) else (
        echo  SysViewV6_Setup.exe compile -^> installer\Output\
    )
) else (
    echo  Inno Setup absent - setup compile par GitHub Actions lors du release.
)
echo.

:: -- 2. Commit + push master -----------------------------------
echo  [1/2] Commit + push master...
git add "SysViewManager.exe" "SysView V6\src" "SysView V6\SysView.html" "SysView V6\manifest.json" "SysView V6\project.json" "SysView V6\preview.gif" "SysViewManager" "installer" "scripts" "README.md"
git commit -m "build: v!NEW_VERSION!"
git push origin master

if %ERRORLEVEL% neq 0 (
    echo  ERREUR git push master - remise version a v!OLD_VERSION!
    echo !OLD_VERSION!>"%VF%"
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
echo   GitHub Actions compile et uploade :
echo     - SysViewManager.exe
echo     - SysViewV6_Setup.exe
echo   https://github.com/Mrtt555/SysView-V6/releases
echo  ================================================
echo.
pause
