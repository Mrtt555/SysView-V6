; =========================================================
;  SysView V6 -- Installeur Inno Setup
;  Telechargement + compilation + services en une seule passe
; =========================================================

#define AppName    "SysView V6"
#define AppVersion "1.0"
#define AppURL     "https://github.com/Mrtt555/SysView-V6"
#define CloneURL   "https://github.com/Mrtt555/SysView-V6.git"
#define AetherZipURL   "https://github.com/Mrtt555/Aether/archive/refs/heads/main.zip"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Mrtt555
AppPublisherURL={#AppURL}
; Requis par IS6 -- la vraie selection se fait via TInputDirWizardPage dans [Code]
DefaultDirName={autopf}\SysView V6
DisableDirPage=yes
; Pas de desinstalleur : plugin Wallpaper Engine, pas une appli Windows
CreateUninstallRegKey=no
Uninstallable=no
; L'EXE cree
OutputBaseFilename=SysViewV6_Setup
OutputDir=Output
; Compression maximale
Compression=lzma2/ultra64
SolidCompression=yes
; UI moderne
WizardStyle=modern
WizardSizePercent=120
; Pas besoin d'admin par defaut -- on demande si necessaire
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Langue : pas de dialog de selection (une seule langue)
LanguageDetectionMethod=none

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
; Surcharge des messages integres de la page de bienvenue
fr.WelcomeLabel1=Bienvenue dans l'installeur de SysView V6
fr.WelcomeLabel2=Ce programme va :%n%n  - Telecharger SysView V6 depuis GitHub%n  - Compiler SysViewHardware (.NET 8)%n  - Verifier / installer Python%n  - Installer les paquets Python%n  - Telecharger Aether (proxy meteo)%n  - Configurer le demarrage automatique%n  - Lancer tous les services%n%nAucune autre action ne sera necessaire.
fr.FinishedLabel=SysView V6 est installe et en cours d'execution.%n%nEndpoints actifs :%n  http://127.0.0.1:5001/v1/status%n  http://127.0.0.1:8001%n  http://127.0.0.1:8086/data.json%n%nOuvrez Wallpaper Engine, selectionnez SysView.html et entrez votre ville.

[CustomMessages]
; Messages utilises via CustomMessage() dans le code Pascal
fr.DirPageTitle=Dossier d'installation
fr.DirPageDesc=Dossier myprojects de Wallpaper Engine (SysView V6 sera cree a l'interieur)
fr.InstPageTitle=Installation en cours
fr.InstPageDesc=Veuillez patienter...

[Files]
; Rien a embarquer -- tout vient de GitHub a l'installation

[Code]

// Declaration explicite de SendMessage (non expose nativement en IS6 Pascal)
function SendMessage(hWnd: LongInt; Msg: LongInt; wParam: LongInt; lParam: LongInt): LongInt;
  external 'SendMessageW@user32.dll stdcall';

// =========================================================
//  CONSTANTES ET VARIABLES GLOBALES
// =========================================================

const
  DEFAULT_MYPROJECTS = 'C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\projects\myprojects';
  CLONE_URL          = '{#CloneURL}';
  AETHER_ZIP_URL     = '{#AetherZipURL}';
  BRIDGE_PORT        = 5001;
  AETHER_PORT        = 8001;
  HW_PORT            = 8086;

var
  // Pages wizard
  PageDir     : TInputDirWizardPage;
  PageInstall : TWizardPage;

  // Widgets de la page installation
  InstMemo   : TMemo;
  InstStatus : TLabel;

  // Log vers fichier
  LogFilePath : String;

  // Resultat global
  InstallOK : Boolean;

  // Chemins calcules
  gDest   : String;  // myprojects\SysView V6
  gAPI    : String;  // gDest\API
  gAether : String;  // gDest\Aether
  gHWExe  : String;  // .exe publie
  gPy     : String;  // python.exe
  gPyW    : String;  // pythonw.exe


// =========================================================
//  HELPERS LOGGING
// =========================================================

procedure AppendLog(const S: String);
begin
  if Assigned(InstMemo) then begin
    InstMemo.Lines.Add('[' + FormatDateTime('hh:nn:ss', Now) + '] ' + S);
    SendMessage(InstMemo.Handle, $00B7 {EM_SCROLLCARET}, 0, 0);
    Application.ProcessMessages;
  end;
  if LogFilePath <> '' then
    SaveStringToFile(LogFilePath, '[' + FormatDateTime('hh:nn:ss', Now) + '] ' + S + #13#10, True);
end;

procedure SetStatus(const S: String);
begin
  if Assigned(InstStatus) then begin
    InstStatus.Caption := S;
    Application.ProcessMessages;
  end;
  AppendLog(S);
end;


// =========================================================
//  EXECUTION DE COMMANDES
// =========================================================

// Lance un exe et redirige stdout+stderr vers le log
function ExecStep(const Exe, Params: String; AllowCode: Integer): Boolean;
var
  OutFile : String;
  Cmd     : String;
  RC      : Integer;
  Lines   : TStringList;
  I       : Integer;
begin
  OutFile := ExpandConstant('{tmp}\sv_out.log');
  DeleteFile(OutFile);

  // Passe par cmd /c pour capturer stdout+stderr
  Cmd := '/c "' + Exe + ' ' + Params + ' >> "' + OutFile + '" 2>&1"';
  Exec(ExpandConstant('{cmd}'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, RC);

  // Ajouter la sortie au memo
  Lines := TStringList.Create;
  try
    if FileExists(OutFile) then begin
      Lines.LoadFromFile(OutFile);
      for I := 0 to Lines.Count - 1 do
        if Trim(Lines[I]) <> '' then
          AppendLog('  ' + Lines[I]);
    end;
  finally
    Lines.Free;
  end;

  Result := (RC = 0) or (RC = AllowCode);
end;

// Lance une commande PowerShell
function ExecPS(const PSCmd: String): Boolean;
begin
  Result := ExecStep('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "' + PSCmd + '"', 0);
end;

// Cherche un exe dans PATH, retourne chemin complet ou ''
function FindInPath(const ExeName: String): String;
var
  RC : Integer;
  OutFile : String;
  Lines : TStringList;
begin
  Result := '';
  OutFile := ExpandConstant('{tmp}\sv_where.log');
  DeleteFile(OutFile);
  Exec(ExpandConstant('{cmd}'), '/c where ' + ExeName + ' > "' + OutFile + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  if RC = 0 then begin
    Lines := TStringList.Create;
    try
      Lines.LoadFromFile(OutFile);
      if Lines.Count > 0 then Result := Trim(Lines[0]);
    finally
      Lines.Free;
    end;
  end;
end;

// Lecture d'une cle de registre (chaine)
function RegReadStrSafe(RootKey: Integer; const SubKey, Name: String): String;
begin
  if not RegQueryStringValue(RootKey, SubKey, Name, Result) then
    Result := '';
end;

// Test de port ouvert via PowerShell
function PortListening(Port: Integer): Boolean;
var
  RC: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/c netstat -ano | findstr ":' + IntToStr(Port) + ' " | findstr "LISTENING" > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  Result := RC = 0;
end;

// Kill d'un port
procedure KillPort(Port: Integer);
var
  RC: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/c for /f "tokens=5" %p in (''netstat -ano 2^>nul ^| findstr ":' +
    IntToStr(Port) + ' " ^| findstr "LISTENING"'') do taskkill /PID %p /F /T > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
end;


// =========================================================
//  ETAPE 1 -- TELECHARGEMENT SYSVIEW V6
// =========================================================

function StepDownload: Boolean;
var
  TmpDir, SrcDir, ZipFile : String;
  RC, RoboCRC             : Integer;
  Lines                   : TStringList;
  I                       : Integer;
  GitExe                  : String;
begin
  Result := False;
  TmpDir  := ExpandConstant('{tmp}\sv_clone');
  ZipFile := ExpandConstant('{tmp}\sv_master.zip');
  SrcDir  := '';

  // Nettoyer les residus
  if DirExists(TmpDir)  then ExecPS('Remove-Item ''' + TmpDir  + ''' -Recurse -Force -EA SilentlyContinue');
  if FileExists(ZipFile) then DeleteFile(ZipFile);

  // -- Essai 1 : git clone --
  SetStatus('[1/6] Telechargement via git clone...');
  GitExe := FindInPath('git');
  if GitExe <> '' then begin
    Exec(ExpandConstant('{cmd}'),
      '/c git clone --depth 1 --branch master "' + CLONE_URL + '" "' + TmpDir + '\SysView-V6-master"'
      + ' >> "' + LogFilePath + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RC);
    if RC = 0 then SrcDir := TmpDir + '\SysView-V6-master';
  end;

  // -- Essai 2 : Invoke-WebRequest --
  if SrcDir = '' then begin
    SetStatus('[1/6] git absent -- telechargement HTTPS...');
    ExecPS('[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      '$ProgressPreference=''SilentlyContinue'';' +
      'Invoke-WebRequest ''' + 'https://github.com/Mrtt555/SysView-V6/archive/refs/heads/master.zip' +
      ''' -OutFile ''' + ZipFile + ''' -UseBasicParsing -ErrorAction Stop');
    if FileExists(ZipFile) then begin
      SetStatus('[1/6] Extraction...');
      ExecPS('Expand-Archive ''' + ZipFile + ''' -DestinationPath ''' + TmpDir + ''' -Force');
      DeleteFile(ZipFile);
      // Trouver le sous-dossier extrait
      Lines := TStringList.Create;
      try
        Exec(ExpandConstant('{cmd}'),
          '/c dir /b /ad "' + TmpDir + '" > "' + ExpandConstant('{tmp}\sv_dir.log') + '" 2>&1',
          '', SW_HIDE, ewWaitUntilTerminated, RC);
        Lines.LoadFromFile(ExpandConstant('{tmp}\sv_dir.log'));
        if Lines.Count > 0 then SrcDir := TmpDir + '\' + Trim(Lines[0]);
      finally
        Lines.Free;
      end;
    end;
  end;

  if SrcDir = '' then begin
    AppendLog('[ERREUR] Telechargement echoue (git clone + Invoke-WebRequest).');
    Exit;
  end;

  // -- Copier / mettre a jour vers _DEST --
  SetStatus('[1/6] Copie des fichiers vers ' + gDest + '...');
  if DirExists(gDest) then begin
    // Mise a jour en place : robocopy (codes 0-7 = OK)
    Exec(ExpandConstant('{cmd}'),
      '/c robocopy "' + SrcDir + '" "' + gDest + '" /E /IS /IT /PURGE /XF "runtime_config.json" /XD "logs"'
      + ' >> "' + LogFilePath + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RoboCRC);
    Result := RoboCRC < 8;
    if not Result then
      AppendLog('[ERREUR] robocopy code ' + IntToStr(RoboCRC));
  end else begin
    // Premiere install : deplacement
    Result := ExecPS('Move-Item ''' + SrcDir + ''' ''' + gDest + '''');
    if not Result then
      AppendLog('[ERREUR] Impossible de deplacer les fichiers vers ' + gDest);
  end;

  if Result then begin
    ExecPS('Remove-Item ''' + TmpDir + ''' -Recurse -Force -EA SilentlyContinue');
    AppendLog('[OK] SysView V6 installe : ' + gDest);
  end;
end;


// =========================================================
//  ETAPE 2 -- COMPILATION SYSVIEWHARDWARE
// =========================================================

function StepCompile: Boolean;
var
  HWProj, DotNet, VerOut : String;
  MajVer, RC             : Integer;
  Lines                  : TStringList;
begin
  Result := False;
  HWProj := gDest + '\SysViewHardware\SysViewHardware.csproj';
  gHWExe := gDest + '\SysViewHardware\bin\Release\net8.0-windows\win-x64\publish\SysViewHardware.exe';

  // Deja compile ?
  if FileExists(gHWExe) then begin
    AppendLog('[2/6] SysViewHardware.exe deja present -- compilation ignoree.');
    Result := True;
    Exit;
  end;

  if not FileExists(HWProj) then begin
    AppendLog('[ERREUR] Projet introuvable : ' + HWProj);
    Exit;
  end;

  // Chercher dotnet
  DotNet := FindInPath('dotnet');
  if DotNet <> '' then begin
    // Verifier version >= 8
    Lines := TStringList.Create;
    try
      Exec(ExpandConstant('{cmd}'),
        '/c dotnet --version > "' + ExpandConstant('{tmp}\sv_dotnet.log') + '" 2>&1',
        '', SW_HIDE, ewWaitUntilTerminated, RC);
      Lines.LoadFromFile(ExpandConstant('{tmp}\sv_dotnet.log'));
      if Lines.Count > 0 then begin
        VerOut := Trim(Lines[0]);
        MajVer := StrToIntDef(Copy(VerOut, 1, Pos('.', VerOut) - 1), 0);
        if MajVer < 8 then DotNet := '';
      end;
    finally
      Lines.Free;
    end;
  end;

  // Installer SDK si absent
  if DotNet = '' then begin
    SetStatus('[2/6] SDK .NET 8 absent -- telechargement (~200 Mo)...');
    AppendLog('[2/6] Installation SDK .NET 8...');
    ExecPS('[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      '$ProgressPreference=''SilentlyContinue'';' +
      '$f=''' + ExpandConstant('{tmp}') + '\dotnet-install.ps1'';' +
      'Invoke-WebRequest ''https://dot.net/v1/dotnet-install.ps1'' -OutFile $f -UseBasicParsing;' +
      '& $f -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"');
    DotNet := ExpandConstant('{userdocs}') + '\..\' + '.dotnet\dotnet.exe';
    // chemin propre
    DotNet := GetEnv('USERPROFILE') + '\.dotnet\dotnet.exe';
    if not FileExists(DotNet) then begin
      AppendLog('[ERREUR] Installation SDK .NET 8 echouee.');
      Exit;
    end;
  end;

  SetStatus('[2/6] Compilation SysViewHardware (~2 min)...');
  AppendLog('[2/6] Compilation en cours (premiere fois : NuGet + build)...');

  Exec(ExpandConstant('{cmd}'),
    '/c "' + DotNet + '" publish "' + HWProj + '"' +
    ' -c Release -r win-x64 --self-contained true' +
    ' -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true' +
    ' -p:DebugType=none --nologo -v minimal' +
    ' >> "' + LogFilePath + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  Result := FileExists(gHWExe);
  if Result then
    AppendLog('[OK] SysViewHardware.exe compile.')
  else
    AppendLog('[ERREUR] Compilation echouee -- voir ' + LogFilePath);
end;


// =========================================================
//  ETAPE 3 -- PYTHON
// =========================================================

function StepPython: Boolean;
var
  RC        : Integer;
  Lines     : TStringList;
  PyDir     : String;
  InstDir   : String;
  PSExe     : String;
begin
  Result := False;
  gPy  := '';
  gPyW := '';

  SetStatus('[3/6] Verification de Python...');

  // Chercher python dans PATH
  gPy := FindInPath('python');
  if gPy = '' then gPy := FindInPath('python3');

  // Chercher dans les emplacements connus si non trouve
  if gPy = '' then begin
    // Registry : HKCU d'abord
    InstDir := RegReadStrSafe(HKCU, 'Software\Python\PythonCore', '');
    // Essai chemins standards
    PyDir := GetEnv('LOCALAPPDATA') + '\Programs\Python';
    Lines := TStringList.Create;
    try
      Exec(ExpandConstant('{cmd}'),
        '/c dir /b /ad "' + PyDir + '" 2>nul > "' + ExpandConstant('{tmp}\sv_pydir.log') + '"',
        '', SW_HIDE, ewWaitUntilTerminated, RC);
      if FileExists(ExpandConstant('{tmp}\sv_pydir.log')) then begin
        Lines.LoadFromFile(ExpandConstant('{tmp}\sv_pydir.log'));
        if Lines.Count > 0 then begin
          gPy := PyDir + '\' + Trim(Lines[Lines.Count - 1]) + '\python.exe';
          if not FileExists(gPy) then gPy := '';
        end;
      end;
    finally
      Lines.Free;
    end;
  end;

  // Installer Python si toujours absent
  if gPy = '' then begin
    SetStatus('[3/6] Python absent -- telechargement (~25 Mo)...');
    AppendLog('[3/6] Installation de Python 3.12...');

    PSExe := ExpandConstant('{tmp}\python_installer.exe');
    ExecPS('[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      '$ProgressPreference=''SilentlyContinue'';' +
      'Invoke-WebRequest ''https://www.python.org/ftp/python/3.12.9/python-3.12.9-amd64.exe''' +
      ' -OutFile ''' + PSExe + ''' -UseBasicParsing');

    if FileExists(PSExe) then begin
      Exec(PSExe, '/quiet InstallAllUsers=0 PrependPath=1 Include_launcher=0',
        '', SW_HIDE, ewWaitUntilTerminated, RC);
      DeleteFile(PSExe);
    end;

    // Recharger PATH depuis le registre
    Exec(ExpandConstant('{cmd}'),
      '/c for /f "tokens=2*" %a in (''reg query "HKCU\Environment" /v PATH 2^>nul'') do' +
      ' setx PATH "%b;%PATH%" > nul 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RC);

    gPy := FindInPath('python');
    if gPy = '' then begin
      AppendLog('[ERREUR] python.exe introuvable apres installation.');
      Exit;
    end;
    AppendLog('[OK] Python installe.');
  end;

  // Deriver pythonw
  gPyW := gPy;
  if Copy(gPy, Length(gPy) - 9, 10) = 'python.exe' then begin
    gPyW := Copy(gPy, 1, Length(gPy) - 10) + 'pythonw.exe';
    if not FileExists(gPyW) then gPyW := gPy;
  end;

  AppendLog('[INFO] Python  : ' + gPy);
  AppendLog('[INFO] Pythonw : ' + gPyW);

  Result := True;
end;


// =========================================================
//  ETAPE 4 -- PAQUETS BRIDGE
// =========================================================

function StepPip: Boolean;
begin
  Result := False;
  SetStatus('[4/6] Mise a jour de pip...');
  ExecStep(gPy, '-m pip install --upgrade pip', 0);

  SetStatus('[4/6] Installation des paquets bridge...');
  Result := ExecStep(gPy,
    '-m pip install fastapi "uvicorn[standard]" requests psutil slowapi httpx python-multipart "pydantic>=2.7.0"',
    0);

  if Result then
    AppendLog('[OK] Paquets bridge installes.')
  else
    AppendLog('[ERREUR] pip install echoue -- voir ' + LogFilePath);
end;


// =========================================================
//  ETAPE 5 -- AETHER
// =========================================================

function StepAether: Boolean;
var
  ZipFile, TmpDir : String;
  Lines           : TStringList;
  SubDir          : String;
  RC              : Integer;
begin
  Result := False;

  if FileExists(gAether + '\main.py') then begin
    AppendLog('[5/6] Aether deja present -- paquets mis a jour uniquement.');
  end else begin
    SetStatus('[5/6] Telechargement d''Aether...');
    ZipFile := ExpandConstant('{tmp}\aether.zip');
    TmpDir  := ExpandConstant('{tmp}\aether_tmp');
    SubDir  := '';

    // Repo public -- Invoke-WebRequest direct
    AppendLog('[5/6] Telechargement Aether via Invoke-WebRequest...');
    ExecPS('[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      '$ProgressPreference=''SilentlyContinue'';' +
      'Invoke-WebRequest ''' + AETHER_ZIP_URL + ''' -OutFile ''' + ZipFile + ''' -UseBasicParsing -EA Stop');

    if not FileExists(ZipFile) then begin
      AppendLog('[ERREUR] Telechargement Aether echoue.');
      Exit;
    end;

    ExecPS('Expand-Archive ''' + ZipFile + ''' -DestinationPath ''' + TmpDir + ''' -Force');
    DeleteFile(ZipFile);

    Lines := TStringList.Create;
    try
      Exec(ExpandConstant('{cmd}'),
        '/c dir /b /ad "' + TmpDir + '" > "' + ExpandConstant('{tmp}\sv_atdir.log') + '" 2>&1',
        '', SW_HIDE, ewWaitUntilTerminated, RC);
      Lines.LoadFromFile(ExpandConstant('{tmp}\sv_atdir.log'));
      if Lines.Count > 0 then SubDir := TmpDir + '\' + Trim(Lines[0]);
    finally
      Lines.Free;
    end;

    if SubDir = '' then begin
      AppendLog('[ERREUR] Archive Aether vide ou inattendue.');
      Exit;
    end;

    ExecPS('Move-Item ''' + SubDir + ''' ''' + gAether + '''');
    ExecPS('Remove-Item ''' + TmpDir + ''' -Recurse -Force -EA SilentlyContinue');

    if not FileExists(gAether + '\main.py') then begin
      AppendLog('[ERREUR] Aether : main.py introuvable apres extraction.');
      Exit;
    end;
    AppendLog('[OK] Aether telecharge.');
  end;

  // Installer requirements
  if FileExists(gAether + '\requirements.txt') then begin
    SetStatus('[5/6] Installation des paquets Aether...');
    ExecStep(gPy, '-m pip install -r "' + gAether + '\requirements.txt"', 0);
  end;

  AppendLog('[OK] Aether pret -- http://127.0.0.1:' + IntToStr(AETHER_PORT));
  Result := True;
end;


// =========================================================
//  ETAPE 6 -- DEMARRAGE AUTOMATIQUE + LANCEMENT
// =========================================================

function StepStart: Boolean;
var
  Shortcut  : String;
  BridgePYW : String;
  RC, I     : Integer;
  PID       : String;
  PIDLines  : TStringList;
begin
  Result := False;
  BridgePYW := gAPI + '\SysViewBridge.pyw';

  // ---- SysViewHardware : tache planifiee (avec droits admin si possible) ----
  SetStatus('[6/6] Configuration demarrage automatique...');
  AppendLog('[2/6] Demarrage automatique SysViewHardware...');

  Exec(ExpandConstant('{cmd}'),
    '/c schtasks /delete /tn "SysViewHardware" /f > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  Exec(ExpandConstant('{cmd}'),
    '/c schtasks /create /tn "SysViewHardware"'
    + ' /tr "\"' + gHWExe + '\""'
    + ' /sc ONLOGON /rl HIGHEST /f >> "' + LogFilePath + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  if RC <> 0 then
    AppendLog('[AVERT] Tache planifiee impossible -- lancement normal (sans UAC eleve).');

  // ---- Lancer SysViewHardware ----
  AppendLog('[2/6] Lancement de SysViewHardware...');
  KillPort(HW_PORT);

  if RC = 0 then
    Exec(ExpandConstant('{cmd}'),
      '/c schtasks /run /tn "SysViewHardware" > nul 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RC)
  else
    Exec(gHWExe, '', gDest, SW_HIDE, ewNoWait, RC);

  // Attendre port 8086 (max 30s)
  I := 0;
  while (I < 15) and not PortListening(HW_PORT) do begin
    Sleep(2000);
    I := I + 1;
  end;
  if PortListening(HW_PORT) then
    AppendLog('[OK] SysViewHardware actif (port ' + IntToStr(HW_PORT) + ').')
  else
    AppendLog('[AVERT] SysViewHardware ne repond pas encore.');

  // ---- Raccourci Startup pour bridge ----
  Shortcut := GetShellFolder('userstartup') + '\SysViewBridge.bat';
  SaveStringToFile(Shortcut, '@echo off' + #13#10 +
    'start "" "' + gPyW + '" "' + BridgePYW + '"' + #13#10, False);
  AppendLog('[OK] Raccourci demarrage bridge configure.');

  // ---- Liberer les ports bridge/Aether ----
  SetStatus('[6/6] Liberation des ports...');
  if FileExists(gAPI + '\bridge.pid') then begin
    // Lire PID
    PIDLines := TStringList.Create;
    try
      PIDLines.LoadFromFile(gAPI + '\bridge.pid');
      if PIDLines.Count > 0 then PID := Trim(PIDLines[0]) else PID := '';
    finally
      PIDLines.Free;
    end;
    if PID <> '' then
      Exec(ExpandConstant('{cmd}'),
        '/c taskkill /PID ' + PID + ' /F /T > nul 2>&1',
        '', SW_HIDE, ewWaitUntilTerminated, RC);
    DeleteFile(gAPI + '\bridge.pid');
  end;
  KillPort(BRIDGE_PORT);
  KillPort(AETHER_PORT);
  Sleep(1000);

  // ---- Lancer bridge (demarre Aether en sous-process) ----
  SetStatus('[6/6] Lancement bridge + Aether...');
  AppendLog('[6/6] Lancement du bridge...');
  Exec(gPyW, '"' + BridgePYW + '"', gAPI, SW_HIDE, ewNoWait, RC);

  // Attendre port 5001 (max 15s)
  I := 0;
  while (I < 8) and not PortListening(BRIDGE_PORT) do begin
    Sleep(2000);
    I := I + 1;
  end;
  if PortListening(BRIDGE_PORT) then
    AppendLog('[OK] Bridge demarre (port ' + IntToStr(BRIDGE_PORT) + ').')
  else
    AppendLog('[AVERT] Bridge non detecte -- verifiez ' + gAPI + '\logs\sysview.log');

  // Attendre Aether (max 20s)
  I := 0;
  while (I < 10) and not PortListening(AETHER_PORT) do begin
    Sleep(2000);
    I := I + 1;
  end;
  if PortListening(AETHER_PORT) then
    AppendLog('[OK] Aether demarre (port ' + IntToStr(AETHER_PORT) + ').')
  else
    AppendLog('[AVERT] Aether ne repond pas encore.');

  Result := True;
end;


// =========================================================
//  PROCEDURE PRINCIPALE D'INSTALLATION
// =========================================================

procedure DoInstall;
var
  LogDir : String;
begin
  InstallOK := False;

  // Chemins
  gDest   := PageDir.Values[0] + '\SysView V6';
  gAPI    := gDest + '\API';
  gAether := gDest + '\Aether';

  // Log file
  LogDir := gDest + '\logs';
  if not DirExists(LogDir) then CreateDir(LogDir);
  LogFilePath := LogDir + '\setup.log';
  SaveStringToFile(LogFilePath,
    '================================================' + #13#10 +
    'SysView V6 -- Setup  [' + FormatDateTime('dd/mm/yyyy hh:nn:ss', Now) + ']' + #13#10 +
    '================================================' + #13#10, False);

  AppendLog('Dossier cible : ' + gDest);

  // Etapes
  SetStatus('[1/6] Telechargement de SysView V6...');
  if not StepDownload then begin
    MsgBox('Echec du telechargement.' + #13#10 + 'Verifiez la connexion et que git est installe.', mbError, MB_OK);
    Exit;
  end;

  SetStatus('[2/6] Compilation de SysViewHardware...');
  if not StepCompile then begin
    MsgBox('Echec de la compilation SysViewHardware.' + #13#10 + 'Verifiez le log : ' + LogFilePath, mbError, MB_OK);
    Exit;
  end;

  SetStatus('[3/6] Verification de Python...');
  if not StepPython then begin
    MsgBox('Python introuvable et installation echouee.', mbError, MB_OK);
    Exit;
  end;

  SetStatus('[4/6] Installation des paquets Python...');
  if not StepPip then begin
    MsgBox('pip install echoue.' + #13#10 + 'Voir : ' + LogFilePath, mbError, MB_OK);
    Exit;
  end;

  SetStatus('[5/6] Installation d''Aether...');
  if not StepAether then begin
    MsgBox('Installation Aether echouee.' + #13#10 + 'Voir : ' + LogFilePath, mbError, MB_OK);
    Exit;
  end;

  SetStatus('[6/6] Demarrage des services...');
  StepStart;

  AppendLog('================================================');
  AppendLog('Setup termine. Log : ' + LogFilePath);
  AppendLog('================================================');

  InstallOK := True;
  SetStatus('Installation terminee !');
end;


// =========================================================
//  WIZARD EVENTS
// =========================================================

procedure InitializeWizard;
begin
  // --- Page de selection du dossier ---
  PageDir := CreateInputDirPage(wpWelcome,
    CustomMessage('DirPageTitle'),
    CustomMessage('DirPageDesc'),
    'Le sous-dossier "SysView V6" sera cree automatiquement.',
    False, '');
  PageDir.Add('');
  PageDir.Values[0] := DEFAULT_MYPROJECTS;

  // --- Page d'installation avec log ---
  PageInstall := CreateCustomPage(PageDir.ID,
    CustomMessage('InstPageTitle'),
    CustomMessage('InstPageDesc'));

  // Label de statut (SurfaceWidth/Height = dimensions IS6 standard)
  InstStatus := TLabel.Create(WizardForm);
  InstStatus.Parent  := PageInstall.Surface;
  InstStatus.Left    := 0;
  InstStatus.Top     := 0;
  InstStatus.Width   := PageInstall.SurfaceWidth;
  InstStatus.Height  := 20;
  InstStatus.Caption := 'Preparation...';
  InstStatus.Font.Style := [fsBold];

  // Memo de log
  InstMemo := TMemo.Create(WizardForm);
  InstMemo.Parent     := PageInstall.Surface;
  InstMemo.Left       := 0;
  InstMemo.Top        := 28;
  InstMemo.Width      := PageInstall.SurfaceWidth;
  InstMemo.Height     := PageInstall.SurfaceHeight - 28;
  InstMemo.ScrollBars := ssVertical;
  InstMemo.ReadOnly   := True;
  InstMemo.Font.Name  := 'Consolas';
  InstMemo.Font.Size  := 8;
  InstMemo.Color      := $1E1E1E;
  InstMemo.Font.Color := $D4D4D4;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = PageInstall.ID then begin
    WizardForm.NextButton.Enabled := False;
    WizardForm.BackButton.Enabled := False;
    try
      DoInstall;
    finally
      WizardForm.NextButton.Enabled := True;
    end;
    if not InstallOK then
      Result := False;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
end;

// Page de fin : message personnalise selon succes/echec
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := 'Dossier d''installation :' + NewLine +
            Space + PageDir.Values[0] + '\SysView V6' + NewLine + NewLine +
            'Operations :' + NewLine +
            Space + '1. Telechargement SysView V6 depuis GitHub' + NewLine +
            Space + '2. Compilation SysViewHardware (.NET 8)' + NewLine +
            Space + '3. Verification Python' + NewLine +
            Space + '4. Installation paquets bridge' + NewLine +
            Space + '5. Installation Aether' + NewLine +
            Space + '6. Lancement des services';
end;

procedure DeinitializeSetup;
begin
  // Rien a nettoyer
end;
