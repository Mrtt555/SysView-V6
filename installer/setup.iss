; =========================================================
;  SysView V6 -- Installeur Inno Setup  (installation complete automatique)
;
;  Pages :
;    1. Bienvenue
;    2. Dossier Wallpaper Engine  (auto-detecte via registre Steam)
;    3. Ville meteo               (geocodage Open-Meteo automatique)
;    4. Recapitulatif
;    5. Installation              (log + barre de progression)
;    6. Termine
;
;  Etapes d'installation :
;    A. Geocodage de la ville (Open-Meteo)
;    B. Telechargement wallpaper (git clone ou ZIP GitHub)
;    C. SysViewManager.exe (GitHub Releases ou compilation dotnet)
;    D. runtime_config.json -> %AppData%\SysViewManager\
;    E. Tache planifiee ONLOGON / HIGHEST + lancement immediat
;
;  Architecture V6 :
;    SysViewManager.exe  (C# .NET 8, admin, single-file self-contained)
;    Donnees : %AppData%\SysViewManager\
;      Hardware.json   (CPU/GPU/RAM/reseau, maj. 500 ms)
;      Weather.json    (meteo + qualite air, maj. periodique)
;      runtime_config.json  (configuration persistee)
; =========================================================

#define AppName       "SysView V6"
#define AppVersion    "1.0"
#define AppURL        "https://github.com/Mrtt555/SysView-V6"
#define CloneURL      "https://github.com/Mrtt555/SysView-V6.git"
#define ReleaseExeURL "https://github.com/Mrtt555/SysView-V6/releases/latest/download/SysViewManager.exe"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Mrtt555
AppPublisherURL={#AppURL}
DefaultDirName={autopf}\SysView V6
DisableDirPage=yes
CreateUninstallRegKey=no
Uninstallable=no
OutputBaseFilename=SysViewV6_Setup
OutputDir=Output
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
; Admin requis pour LHM (capteurs temp.) et tache planifiee /rl HIGHEST
PrivilegesRequired=admin
LanguageDetectionMethod=none

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
fr.WelcomeLabel1=Bienvenue dans l'installeur de SysView V6
fr.WelcomeLabel2=Ce programme va :%n%n  · Telecharger le wallpaper SysView V6 depuis GitHub%n  · Installer SysViewManager.exe (pre-compile depuis GitHub Releases)%n  · Configurer la meteo pour votre ville (geocodage automatique)%n  · Creer la tache planifiee au demarrage (admin)%n  · Lancer SysViewManager immediatement%n%nAucune dependance Python ou autre runtime n'est necessaire.
fr.FinishedLabel=SysView V6 est installe et en cours d'execution.%n%nSysViewManager tourne dans la barre systeme.%n%nAPI locale :%n  http://127.0.0.1:5001/v1/status%n%nDonnees :%n  %%AppData%%\SysViewManager\Hardware.json%n  %%AppData%%\SysViewManager\Weather.json%n%nExtension Chrome (optionnel) :%n  1. Ouvrez  chrome://extensions%n  2. Activez "Mode developpeur"%n  3. Cliquez "Charger l'extension non empaquetee"%n  4. Selectionnez le dossier  SysViewExtension\%n%nOuvrez Wallpaper Engine et selectionnez SysView V6.

[CustomMessages]
fr.DirPageTitle=Dossier Wallpaper Engine
fr.DirPageDesc=Dossier myprojects de Wallpaper Engine
fr.DirPageHint=Le sous-dossier "SysView V6" sera cree automatiquement.
fr.CityPageTitle=Configuration meteo
fr.CityPageDesc=Entrez votre ville pour activer la meteo en temps reel.
fr.CityPageHint=La ville sera geocodee automatiquement via Open-Meteo (lat/lon stockes dans runtime_config.json).
fr.CityLabel=Ville (ex : Paris, Lyon, Bordeaux, Lille) :
fr.InstPageTitle=Installation en cours
fr.InstPageDesc=Veuillez patienter, l'installation est entierement automatique...

[Files]
; Rien a embarquer -- tout vient de GitHub (wallpaper + exe)

[Code]

// ── API Windows ───────────────────────────────────────────────────────────────

function SendMessage(hWnd, Msg, wParam, lParam: LongInt): LongInt;
  external 'SendMessageW@user32.dll stdcall';

// ── StringReplace absent du Pascal Inno Setup -- implementation manuelle ──────

function StrReplace(const S, OldStr, NewStr: String): String;
var I: Integer; R: String;
begin
  R := S;
  I := Pos(OldStr, R);
  while I > 0 do begin
    R := Copy(R, 1, I - 1) + NewStr + Copy(R, I + Length(OldStr), MaxInt);
    I := Pos(OldStr, R);
  end;
  Result := R;
end;

// =========================================================
//  CONSTANTES ET VARIABLES GLOBALES
// =========================================================

const
  // Chemin par defaut si Steam/registre introuvable
  DEFAULT_MYPROJECTS = 'C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\projects\myprojects';
  CLONE_URL          = '{#CloneURL}';
  RELEASE_EXE_URL    = '{#ReleaseExeURL}';
  BRIDGE_PORT        = 5001;

var
  // Pages wizard
  PageDir     : TInputDirWizardPage;
  PageCity    : TInputQueryWizardPage;
  PageInstall : TWizardPage;

  // Widgets page installation
  InstMemo    : TMemo;
  InstStatus  : TLabel;
  ProgBar     : TNewProgressBar;

  // Etat global
  LogFilePath : String;
  InstallOK   : Boolean;

  // Chemins
  gDest       : String;    // myprojects\SysView V6
  gMgrExe     : String;    // SysViewManager.exe (dans gDest)

  // Ville / geocodage
  gCityInput  : String;    // saisie brute utilisateur
  gCityName   : String;    // nom resolu par l'API
  gLatStr     : String;    // latitude  en string (ex: "48.8566") -- evite FloatToStr locale
  gLonStr     : String;    // longitude en string
  gGeoOK      : Boolean;   // geocodage reussi


// =========================================================
//  HELPERS GENERAUX
// =========================================================

// Ajoute une ligne horodatee au memo et au fichier log
procedure AppendLog(const S: String);
var T: String;
begin
  T := '[' + GetDateTimeString('hh:nn:ss', #0, #0) + '] ' + S;
  if Assigned(InstMemo) then begin
    InstMemo.Lines.Add(T);
    // Scroll automatique en bas
    SendMessage(InstMemo.Handle, $00B7 {EM_SCROLLCARET}, 0, 0);
    WizardForm.Refresh;
  end;
  if LogFilePath <> '' then
    SaveStringToFile(LogFilePath, T + #13#10, True);
end;

// Met a jour le label de statut et loggue
procedure SetStatus(const S: String);
begin
  if Assigned(InstStatus) then begin
    InstStatus.Caption := S;
    WizardForm.Refresh;
  end;
  AppendLog(S);
end;

// Met a jour la barre de progression (0..100)
procedure SetProgress(Pct: Integer);
begin
  if Assigned(ProgBar) then begin
    ProgBar.Position := Pct;
    WizardForm.Refresh;
  end;
end;

// Execute un processus et loggue sa sortie
function ExecAndLog(const Exe, Params: String; AllowRC: Integer): Boolean;
var
  OutFile : String;
  RC      : Integer;
  Lines   : TStringList;
  I       : Integer;
begin
  OutFile := ExpandConstant('{tmp}\sv_exec.log');
  DeleteFile(OutFile);
  Exec(ExpandConstant('{cmd}'),
    '/c "' + Exe + ' ' + Params + ' >> "' + OutFile + '" 2>&1"',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  Lines := TStringList.Create;
  try
    if FileExists(OutFile) then begin
      Lines.LoadFromFile(OutFile);
      for I := 0 to Lines.Count - 1 do
        if Trim(Lines[I]) <> '' then AppendLog('  ' + Lines[I]);
    end;
  finally Lines.Free; end;
  Result := (RC = 0) or (RC = AllowRC);
end;

// Execute un script PowerShell (loggue stdout+stderr dans LogFilePath)
function ExecPS(const PSCmd: String): Boolean;
var RC: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/c powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + PSCmd + '"'
    + ' >> "' + LogFilePath + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  Result := (RC = 0);
end;

// Cherche un executable dans PATH, retourne son chemin ou ''
function FindInPath(const ExeName: String): String;
var RC: Integer; OutFile: String; Lines: TStringList;
begin
  Result  := '';
  OutFile := ExpandConstant('{tmp}\sv_where.log');
  DeleteFile(OutFile);
  Exec(ExpandConstant('{cmd}'),
    '/c where ' + ExeName + ' > "' + OutFile + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  if RC = 0 then begin
    Lines := TStringList.Create;
    try
      Lines.LoadFromFile(OutFile);
      if Lines.Count > 0 then Result := Trim(Lines[0]);
    finally Lines.Free; end;
  end;
end;

// Teste si un port TCP est en ecoute (LISTENING)
function PortListening(Port: Integer): Boolean;
var RC: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/c netstat -ano | findstr ":' + IntToStr(Port) + ' " | findstr "LISTENING" > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  Result := (RC = 0);
end;

// Tue le processus occupant un port
procedure KillPort(Port: Integer);
var RC: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/c for /f "tokens=5" %p in (''netstat -ano 2^>nul ^| findstr ":' +
    IntToStr(Port) + ' " ^| findstr "LISTENING"'') do taskkill /PID %p /F /T > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
end;

// Lit le chemin myprojects depuis le registre Steam
function DetectWEMyProjects: String;
var SteamPath: String;
begin
  Result := DEFAULT_MYPROJECTS;
  if RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', SteamPath)
     and (SteamPath <> '') then
  begin
    // Steam stocke le chemin avec des slashes /  -> convertir en \
    SteamPath := StrReplace(SteamPath, '/', '\');
    Result    := SteamPath + '\steamapps\common\wallpaper_engine\projects\myprojects';
  end;
end;


// =========================================================
//  GEOCODAGE -- Open-Meteo Geocoding API
// =========================================================

// Geocode la ville gCityInput, remplit gCityName / gLatStr / gLonStr / gGeoOK
procedure DoGeocode;
var
  CityFile : String;
  OutFile  : String;
  PSCmd    : String;
  RC       : Integer;
  Lines    : TStringList;
  Line     : String;
  P1, P2   : Integer;
begin
  gGeoOK    := False;
  gCityName := gCityInput;
  gLatStr   := '50.73';
  gLonStr   := '3.13';

  if Trim(gCityInput) = '' then Exit;

  // Ecrire la ville dans un fichier temp pour eviter les pb de quoting
  CityFile := ExpandConstant('{tmp}\sv_city.txt');
  OutFile  := ExpandConstant('{tmp}\sv_geo.txt');
  SaveStringToFile(CityFile, gCityInput, False);
  DeleteFile(OutFile);

  PSCmd :=
    '[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
    '$ProgressPreference=''SilentlyContinue'';' +
    'try{' +
      '$city=[Uri]::EscapeDataString((Get-Content ''' + CityFile + ''' -Raw -Encoding UTF8).Trim());' +
      '$url="https://geocoding-api.open-meteo.com/v1/search?name=$city&count=1&language=fr&format=json";' +
      '$j=(Invoke-WebRequest -Uri $url -UseBasicParsing -EA Stop).Content|ConvertFrom-Json;' +
      '$x=$j.results[0];' +
      '$line="$($x.latitude)|$($x.longitude)|$($x.name)";' +
      '[IO.File]::WriteAllText(''' + OutFile + ''',$line,[Text.Encoding]::UTF8)' +
    '}catch{[IO.File]::WriteAllText(''' + OutFile + ''',"",[Text.Encoding]::UTF8)}';

  Exec(ExpandConstant('{cmd}'),
    '/c powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + PSCmd + '"',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  if not FileExists(OutFile) then begin
    AppendLog('[AVERT] Geocodage : pas de reponse pour "' + gCityInput + '" -- valeurs par defaut utilisees.');
    Exit;
  end;

  Lines := TStringList.Create;
  try
    Lines.LoadFromFile(OutFile);
    if Lines.Count > 0 then Line := Trim(Lines[0]);
  finally Lines.Free; end;

  if Line = '' then begin
    AppendLog('[AVERT] Geocodage : ville introuvable -- valeurs par defaut utilisees.');
    Exit;
  end;

  // Parsing "lat|lon|name"
  P1 := Pos('|', Line);
  if P1 = 0 then begin
    AppendLog('[AVERT] Geocodage : format inattendu : ' + Line);
    Exit;
  end;
  P2 := Pos('|', Copy(Line, P1 + 1, Length(Line)));
  if P2 = 0 then begin
    AppendLog('[AVERT] Geocodage : format inattendu (2) : ' + Line);
    Exit;
  end;
  P2 := P1 + P2;

  gLatStr   := StrReplace(Trim(Copy(Line, 1,      P1 - 1)),      ',', '.');
  gLonStr   := StrReplace(Trim(Copy(Line, P1 + 1, P2 - P1 - 1)), ',', '.');
  gCityName := Trim(Copy(Line, P2 + 1, Length(Line)));

  if (gLatStr <> '') and (gLonStr <> '') and (gCityName <> '') then begin
    gGeoOK := True;
    AppendLog('[OK] Geocodage : ' + gCityName + ' (' + gLatStr + ', ' + gLonStr + ')');
  end else
    AppendLog('[AVERT] Geocodage : donnees incompletes : ' + Line);
end;


// =========================================================
//  ECRITURE runtime_config.json -> %AppData%\SysViewManager\
// =========================================================

procedure WriteRuntimeConfig;
var
  AppDataDir : String;
  ConfigPath : String;
  Json       : String;
begin
  AppDataDir := ExpandConstant('{userappdata}') + '\SysViewManager';
  ConfigPath := AppDataDir + '\runtime_config.json';

  // Creer le dossier si necessaire (double securite avec ExecPS dans StepStart)
  if not DirExists(AppDataDir) then
    CreateDir(AppDataDir);

  // Si le fichier existe deja ET le geocodage a echoue : ne pas ecraser
  if FileExists(ConfigPath) and not gGeoOK then begin
    AppendLog('[INFO] runtime_config.json deja present et geocodage non disponible -- conserve.');
    Exit;
  end;

  // Valeurs a ecrire : geocodees ou par defaut (Halluin)
  if not gGeoOK then begin
    gLatStr   := '50.73';
    gLonStr   := '3.13';
    gCityName := 'HALLUIN';
  end;

  Json :=
    '{' + #13#10 +
    '  "lat": '                  + gLatStr    + ',' + #13#10 +
    '  "lon": '                  + gLonStr    + ',' + #13#10 +
    '  "city": "'                + gCityName  + '",' + #13#10 +
    '  "weather_interval_min": 10,' + #13#10 +
    '  "network_iface": "auto",' + #13#10 +
    '  "weather_model": "best_match"' + #13#10 +
    '}';

  SaveStringToFile(ConfigPath, Json, False);
  AppendLog('[OK] runtime_config.json -> ' + ConfigPath);
end;


// =========================================================
//  ETAPE A -- GEOCODAGE + INIT LOG
// =========================================================

procedure StepGeocode;
begin
  SetStatus('Geocodage de "' + gCityInput + '"...');
  DoGeocode;
  if gGeoOK then
    SetStatus('Ville : ' + gCityName + ' (' + gLatStr + ', ' + gLonStr + ')')
  else
    SetStatus('Geocodage echoue -- valeurs par defaut.');
end;


// =========================================================
//  ETAPE B -- TELECHARGEMENT WALLPAPER (sources + HTML)
// =========================================================

function StepDownload: Boolean;
var
  TmpDir, SrcDir, ZipFile : String;
  RC, RoboCRC             : Integer;
  Lines                   : TStringList;
begin
  Result := False;
  TmpDir  := ExpandConstant('{tmp}\sv_clone');
  ZipFile := ExpandConstant('{tmp}\sv_master.zip');
  SrcDir  := '';

  // Nettoyage prealable
  ExecPS('Remove-Item ''' + TmpDir + ''' -Recurse -Force -EA SilentlyContinue');
  if FileExists(ZipFile) then DeleteFile(ZipFile);

  // ── Essai 1 : git clone (rapide si git disponible) ────────────────────────
  SetStatus('[1/3] Telechargement via git clone...');
  if FindInPath('git') <> '' then begin
    Exec(ExpandConstant('{cmd}'),
      '/c git clone --depth 1 --branch master "' + CLONE_URL + '" "' + TmpDir + '\SysView-V6-master"'
      + ' >> "' + LogFilePath + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RC);
    if RC = 0 then SrcDir := TmpDir + '\SysView-V6-master';
  end;

  // ── Essai 2 : archive ZIP via HTTPS ───────────────────────────────────────
  if SrcDir = '' then begin
    SetStatus('[1/3] git absent -- telechargement archive ZIP...');
    ExecPS(
      '[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      '$ProgressPreference=''SilentlyContinue'';' +
      'Invoke-WebRequest ''https://github.com/Mrtt555/SysView-V6/archive/refs/heads/master.zip''' +
      ' -OutFile ''' + ZipFile + ''' -UseBasicParsing -EA Stop');

    if FileExists(ZipFile) then begin
      SetStatus('[1/3] Extraction de l''archive...');
      ExecPS('Expand-Archive ''' + ZipFile + ''' -DestinationPath ''' + TmpDir + ''' -Force');
      DeleteFile(ZipFile);
      Lines := TStringList.Create;
      try
        Exec(ExpandConstant('{cmd}'),
          '/c dir /b /ad "' + TmpDir + '" > "' + ExpandConstant('{tmp}\sv_dir.log') + '" 2>&1',
          '', SW_HIDE, ewWaitUntilTerminated, RC);
        Lines.LoadFromFile(ExpandConstant('{tmp}\sv_dir.log'));
        if Lines.Count > 0 then SrcDir := TmpDir + '\' + Trim(Lines[0]);
      finally Lines.Free; end;
    end;
  end;

  if SrcDir = '' then begin
    AppendLog('[ERREUR] Telechargement echoue -- verifiez la connexion Internet.');
    Exit;
  end;

  // ── Copie vers gDest ──────────────────────────────────────────────────────
  SetStatus('[1/3] Copie vers ' + gDest + '...');
  if DirExists(gDest) then begin
    // Mise a jour (ne pas ecraser runtime_config.json)
    Exec(ExpandConstant('{cmd}'),
      '/c robocopy "' + SrcDir + '" "' + gDest + '" /E /IS /IT /PURGE' +
      ' /XF "runtime_config.json" /XD "logs"' +
      ' >> "' + LogFilePath + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RoboCRC);
    Result := RoboCRC < 8; // robocopy retourne 0-7 pour succes
  end else
    Result := ExecPS('Move-Item ''' + SrcDir + ''' ''' + gDest + '''');

  if Result then begin
    ExecPS('Remove-Item ''' + TmpDir + ''' -Recurse -Force -EA SilentlyContinue');
    AppendLog('[OK] Wallpaper installe dans ' + gDest);
  end;
end;


// =========================================================
//  ETAPE C -- SYSVIEWMANAGER.EXE (pre-compile ou compilation)
// =========================================================

function StepGetExe: Boolean;
var
  DlExe   : String;
  DotNet  : String;
  MgrProj : String;
  PubExe  : String;
  VerOut  : String;
  MajVer, RC : Integer;
  Lines   : TStringList;
begin
  Result  := False;
  gMgrExe := gDest + '\SysViewManager.exe';

  // ── Essai 1 : exe pre-compile sur GitHub Releases ─────────────────────────
  SetStatus('[2/3] Telechargement SysViewManager.exe (release pre-compile)...');
  DlExe := ExpandConstant('{tmp}\SysViewManager_dl.exe');
  DeleteFile(DlExe);

  ExecPS(
    '[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
    '$ProgressPreference=''SilentlyContinue'';' +
    'Invoke-WebRequest ''' + RELEASE_EXE_URL + ''' -OutFile ''' + DlExe + ''' -UseBasicParsing -EA Stop');

  if FileExists(DlExe) then begin
    CopyFile(DlExe, gMgrExe, True);
    DeleteFile(DlExe);
    AppendLog('[OK] SysViewManager.exe telecharge depuis GitHub Releases.');
    Result := True;
    Exit;
  end;

  AppendLog('[INFO] Aucun release GitHub disponible -- compilation depuis les sources...');

  // ── Fallback : dotnet publish ──────────────────────────────────────────────
  MgrProj := gDest + '\SysViewManager\SysViewManager.csproj';
  PubExe  := gDest + '\SysViewManager\bin\Release\net8.0-windows\win-x64\publish\SysViewManager.exe';

  if not FileExists(MgrProj) then begin
    AppendLog('[ERREUR] Projet introuvable : ' + MgrProj);
    Exit;
  end;

  // Verifier dotnet >= 8
  DotNet := FindInPath('dotnet');
  if DotNet <> '' then begin
    Lines := TStringList.Create;
    try
      Exec(ExpandConstant('{cmd}'),
        '/c dotnet --version > "' + ExpandConstant('{tmp}\sv_dotnet.log') + '" 2>&1',
        '', SW_HIDE, ewWaitUntilTerminated, RC);
      Lines.LoadFromFile(ExpandConstant('{tmp}\sv_dotnet.log'));
      if Lines.Count > 0 then begin
        VerOut := Trim(Lines[0]);
        MajVer := StrToIntDef(Copy(VerOut, 1, Pos('.', VerOut) - 1), 0);
        if MajVer < 8 then begin
          AppendLog('[INFO] dotnet ' + VerOut + ' detecte -- version < 8, SDK 8 necessaire.');
          DotNet := '';
        end;
      end;
    finally Lines.Free; end;
  end;

  // Installer .NET 8 SDK si absent
  if DotNet = '' then begin
    SetStatus('[2/3] SDK .NET 8 absent -- telechargement (~500 Mo)...');
    AppendLog('[INFO] Installation du SDK .NET 8 via dotnet-install.ps1...');
    ExecPS(
      '[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      '$ProgressPreference=''SilentlyContinue'';' +
      '$f=''' + ExpandConstant('{tmp}') + '\dotnet-install.ps1'';' +
      'Invoke-WebRequest ''https://dot.net/v1/dotnet-install.ps1'' -OutFile $f -UseBasicParsing -EA Stop;' +
      '& $f -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet" -NoPath');
    DotNet := GetEnv('USERPROFILE') + '\.dotnet\dotnet.exe';
    if not FileExists(DotNet) then begin
      AppendLog('[ERREUR] Installation SDK .NET 8 echouee.');
      Exit;
    end;
    AppendLog('[OK] SDK .NET 8 installe dans ' + GetEnv('USERPROFILE') + '\.dotnet\');
  end;

  SetStatus('[2/3] Compilation SysViewManager (peut prendre ~2 min)...');
  Exec(ExpandConstant('{cmd}'),
    '/c "' + DotNet + '" publish "' + MgrProj + '"' +
    ' -c Release -r win-x64 --self-contained true' +
    ' -p:PublishSingleFile=true' +
    ' -p:IncludeNativeLibrariesForSelfExtract=true' +
    ' -p:DebugType=none --nologo -v minimal >> "' + LogFilePath + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  if not FileExists(PubExe) then begin
    AppendLog('[ERREUR] Compilation echouee -- consultez le log : ' + LogFilePath);
    Exit;
  end;

  CopyFile(PubExe, gMgrExe, True);
  AppendLog('[OK] SysViewManager.exe compile et copie dans ' + gDest);
  Result := True;
end;


// =========================================================
//  ETAPE D+E -- RUNTIME_CONFIG + TACHE PLANIFIEE + LANCEMENT
// =========================================================

function StepConfigAndStart: Boolean;
var
  RC, I : Integer;
begin
  Result := False;

  // ── Dossier %AppData%\SysViewManager\ ─────────────────────────────────────
  SetStatus('[3/3] Creation du dossier de donnees...');
  ExecPS(
    '$d="$env:APPDATA\SysViewManager";' +
    'if(!(Test-Path $d)){New-Item -ItemType Directory -Path $d -Force | Out-Null}');
  AppendLog('[OK] %AppData%\SysViewManager\ pret.');

  // ── runtime_config.json ───────────────────────────────────────────────────
  SetStatus('[3/3] Ecriture de la configuration meteo...');
  WriteRuntimeConfig;
  SetProgress(82);

  // ── Tache planifiee ONLOGON / HIGHEST ────────────────────────────────────
  SetStatus('[3/3] Configuration du demarrage automatique...');

  // Supprimer l'ancienne tache si elle existe
  Exec(ExpandConstant('{cmd}'),
    '/c schtasks /delete /tn "SysViewManager" /f > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  // Creer la nouvelle tache
  Exec(ExpandConstant('{cmd}'),
    '/c schtasks /create /tn "SysViewManager"' +
    ' /tr "\"' + gMgrExe + '\""' +
    ' /sc ONLOGON /rl HIGHEST /f >> "' + LogFilePath + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  if RC = 0 then
    AppendLog('[OK] Tache planifiee "SysViewManager" : ONLOGON / HIGHEST.')
  else begin
    // Fallback : raccourci dans Startup sans elevation
    AppendLog('[AVERT] Tache planifiee impossible (RC=' + IntToStr(RC) + ').');
    AppendLog('        Fallback : raccourci Startup (sans elevation UAC).');
    SaveStringToFile(
      ExpandConstant('{userstartup}') + '\SysViewManager.bat',
      '@echo off' + #13#10 + 'start "" "' + gMgrExe + '"' + #13#10,
      False);
  end;

  SetProgress(88);

  // ── Lancer SysViewManager immediatement ──────────────────────────────────
  SetStatus('[3/3] Lancement de SysViewManager...');
  KillPort(BRIDGE_PORT);
  Sleep(500);

  if RC = 0 then
    // Via la tache planifiee (deja admin, pas de pop-up UAC)
    Exec(ExpandConstant('{cmd}'),
      '/c schtasks /run /tn "SysViewManager" > nul 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RC)
  else
    // Direct (l'installeur tourne deja en admin)
    Exec(gMgrExe, '', gDest, SW_HIDE, ewNoWait, RC);

  SetProgress(93);

  // ── Attente port 5001 (max 30 s) ─────────────────────────────────────────
  AppendLog('Attente du Bridge (port ' + IntToStr(BRIDGE_PORT) + ')...');
  I := 0;
  while (I < 15) and not PortListening(BRIDGE_PORT) do begin
    Sleep(2000);
    I := I + 1;
  end;

  if PortListening(BRIDGE_PORT) then
    AppendLog('[OK] Bridge actif -> http://127.0.0.1:' + IntToStr(BRIDGE_PORT) + '/v1/status')
  else
    AppendLog('[INFO] Bridge non detecte sur le port -- SysViewManager demarre peut-etre en arriere-plan.');

  Result := True;
end;


// =========================================================
//  PROCEDURE PRINCIPALE D'INSTALLATION
// =========================================================

procedure DoInstall;
var LogDir: String;
begin
  InstallOK := False;
  gCityInput := Trim(PageCity.Values[0]);
  gDest      := PageDir.Values[0] + '\SysView V6';
  LogDir     := gDest + '\logs';

  // Preparer dossiers et log
  if not DirExists(gDest)  then CreateDir(gDest);
  if not DirExists(LogDir) then CreateDir(LogDir);
  LogFilePath := LogDir + '\setup.log';
  SaveStringToFile(LogFilePath,
    '================================================' + #13#10 +
    'SysView V6 -- Setup  [' + GetDateTimeString('dd/mm/yyyy hh:nn:ss', #0, #0) + ']' + #13#10 +
    '================================================' + #13#10 +
    'Dossier : ' + gDest + #13#10 +
    'Ville   : ' + gCityInput + #13#10 +
    '================================================' + #13#10,
    False);

  AppendLog('Dossier cible : ' + gDest);
  SetProgress(0);

  // ── Etape A : geocodage ───────────────────────────────────────────────────
  StepGeocode;
  SetProgress(8);

  // ── Etape B : wallpaper ───────────────────────────────────────────────────
  SetStatus('[1/3] Telechargement du wallpaper...');
  if not StepDownload then begin
    MsgBox(
      'Echec du telechargement du wallpaper.' + #13#10 +
      'Verifiez votre connexion Internet.' + #13#10 + #13#10 +
      'Log : ' + LogFilePath,
      mbError, MB_OK);
    Exit;
  end;
  SetProgress(45);

  // ── Etape C : exe ────────────────────────────────────────────────────────
  SetStatus('[2/3] Obtention de SysViewManager.exe...');
  if not StepGetExe then begin
    MsgBox(
      'Impossible d''obtenir SysViewManager.exe.' + #13#10 + #13#10 +
      'Log : ' + LogFilePath,
      mbError, MB_OK);
    Exit;
  end;
  SetProgress(78);

  // ── Etapes D+E : config + demarrage ──────────────────────────────────────
  SetStatus('[3/3] Configuration et lancement...');
  if not StepConfigAndStart then begin
    MsgBox(
      'Erreur lors du demarrage de SysViewManager.' + #13#10 +
      'Log : ' + LogFilePath,
      mbError, MB_OK);
    Exit;
  end;
  SetProgress(100);

  AppendLog('');
  AppendLog('================================================');
  AppendLog('Installation terminee avec succes !');
  AppendLog('Log complet : ' + LogFilePath);
  AppendLog('================================================');

  InstallOK := True;
  SetStatus('Installation terminee !');
end;


// =========================================================
//  WIZARD EVENTS
// =========================================================

procedure InitializeWizard;
var WEPath: String;
begin
  // ── Page 1 : dossier Wallpaper Engine ────────────────────────────────────
  PageDir := CreateInputDirPage(wpWelcome,
    CustomMessage('DirPageTitle'),
    CustomMessage('DirPageDesc'),
    CustomMessage('DirPageHint'),
    False, '');
  PageDir.Add('');
  WEPath := DetectWEMyProjects;
  PageDir.Values[0] := WEPath;

  // ── Page 2 : ville ───────────────────────────────────────────────────────
  PageCity := CreateInputQueryPage(PageDir.ID,
    CustomMessage('CityPageTitle'),
    CustomMessage('CityPageDesc'),
    CustomMessage('CityPageHint'));
  PageCity.Add(CustomMessage('CityLabel'), False);

  // ── Page 3 : installation ─────────────────────────────────────────────────
  PageInstall := CreateCustomPage(PageCity.ID,
    CustomMessage('InstPageTitle'),
    CustomMessage('InstPageDesc'));

  // Statut (ligne de texte en gras)
  InstStatus              := TLabel.Create(WizardForm);
  InstStatus.Parent        := PageInstall.Surface;
  InstStatus.Left          := 0;
  InstStatus.Top           := 0;
  InstStatus.Width         := PageInstall.SurfaceWidth;
  InstStatus.Height        := 20;
  InstStatus.Caption       := 'En attente...';
  InstStatus.Font.Style    := [fsBold];

  // Memo de log (theme sombre Consolas)
  InstMemo              := TMemo.Create(WizardForm);
  InstMemo.Parent        := PageInstall.Surface;
  InstMemo.Left          := 0;
  InstMemo.Top           := 26;
  InstMemo.Width         := PageInstall.SurfaceWidth;
  InstMemo.Height        := PageInstall.SurfaceHeight - 50;
  InstMemo.ScrollBars    := ssVertical;
  InstMemo.ReadOnly      := True;
  InstMemo.Font.Name     := 'Consolas';
  InstMemo.Font.Size     := 8;
  InstMemo.Color         := $1E1E1E;
  InstMemo.Font.Color    := $D4D4D4;

  // Barre de progression
  ProgBar            := TNewProgressBar.Create(WizardForm);
  ProgBar.Parent      := PageInstall.Surface;
  ProgBar.Left        := 0;
  ProgBar.Top         := PageInstall.SurfaceHeight - 20;
  ProgBar.Width       := PageInstall.SurfaceWidth;
  ProgBar.Height      := 16;
  ProgBar.Min         := 0;
  ProgBar.Max         := 100;
  ProgBar.Position    := 0;
end;

// Validation par page et declenchement de l'installation
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  // ── Validation : ville obligatoire ───────────────────────────────────────
  if CurPageID = PageCity.ID then begin
    if Trim(PageCity.Values[0]) = '' then begin
      MsgBox(
        'Veuillez entrer le nom de votre ville pour activer la meteo.' + #13#10 +
        'Exemple : Paris, Lyon, Bordeaux...',
        mbInformation, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  // ── Lancement de l'installation ───────────────────────────────────────────
  if CurPageID = PageInstall.ID then begin
    WizardForm.NextButton.Enabled := False;
    WizardForm.BackButton.Enabled := False;
    try
      DoInstall;
    finally
      WizardForm.NextButton.Enabled := True;
    end;
    Result := InstallOK;
    if not InstallOK then
      WizardForm.BackButton.Enabled := True;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
end;

// Recapitulatif affiche avant l'installation
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var CityStr: String;
begin
  CityStr := Trim(PageCity.Values[0]);
  if CityStr = '' then CityStr := '(non renseignee)';
  Result :=
    'Dossier wallpaper :' + NewLine +
    Space + PageDir.Values[0] + '\SysView V6' + NewLine + NewLine +
    'Donnees utilisateur :' + NewLine +
    Space + '%AppData%\SysViewManager\' + NewLine + NewLine +
    'Ville meteo :' + NewLine +
    Space + CityStr + '  (geocodage Open-Meteo automatique)' + NewLine + NewLine +
    'Operations :' + NewLine +
    Space + 'A. Geocodage de la ville (lat/lon)' + NewLine +
    Space + 'B. Telechargement wallpaper SysView V6 (GitHub)' + NewLine +
    Space + 'C. SysViewManager.exe (GitHub Releases ou compilation)' + NewLine +
    Space + 'D. runtime_config.json -> %AppData%\SysViewManager\' + NewLine +
    Space + 'E. Tache planifiee ONLOGON / HIGHEST + lancement' + NewLine + NewLine +
    'Aucune dependance Python requise.';
end;

procedure DeinitializeSetup;
begin
end;
