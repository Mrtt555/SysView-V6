; =========================================================
;  SysView V6 -- Installeur Inno Setup
;  3 etapes : Telechargement → Exe (pre-built ou compile) → Lancement
;
;  Architecture V6 :
;    SysViewManager.exe  (C# .NET 8, admin, single-file)
;      → LHM, Bridge (port 5001), Météo (Open-Meteo direct)
;    Données : %AppData%\SysViewManager\
;      Hardware.json, Weather.json, runtime_config.json
;
;  Plus besoin de Python, pip, Aether, SysViewHardware séparé.
; =========================================================

#define AppName      "SysView V6"
#define AppVersion   "1.0"
#define AppURL       "https://github.com/Mrtt555/SysView-V6"
#define CloneURL     "https://github.com/Mrtt555/SysView-V6.git"
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
; Admin requis — SysViewManager a besoin des droits pour LHM / tache planifiee
PrivilegesRequired=admin
LanguageDetectionMethod=none

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
fr.WelcomeLabel1=Bienvenue dans l'installeur de SysView V6
fr.WelcomeLabel2=Ce programme va :%n%n  - Telecharger le wallpaper SysView V6 depuis GitHub%n  - Installer SysViewManager.exe (pre-compile depuis GitHub Releases)%n  - Configurer le demarrage automatique au login (tache planifiee admin)%n  - Lancer SysViewManager%n%nAucune dependance Python ou autre runtime n'est necessaire.
fr.FinishedLabel=SysView V6 est installe et en cours d'execution.%n%nEndpoints actifs :%n  http://127.0.0.1:5001/v1/status%n%nDonnees :%n  %%AppData%%\SysViewManager\Hardware.json%n  %%AppData%%\SysViewManager\Weather.json%n%nOuvrez Wallpaper Engine, selectionnez SysView.html et entrez votre ville.

[CustomMessages]
fr.DirPageTitle=Dossier du wallpaper
fr.DirPageDesc=Dossier myprojects de Wallpaper Engine (SysView V6 sera cree a l'interieur)
fr.InstPageTitle=Installation en cours
fr.InstPageDesc=Veuillez patienter...

[Files]
; Rien a embarquer -- tout vient de GitHub

[Code]

function SendMessage(hWnd: LongInt; Msg: LongInt; wParam: LongInt; lParam: LongInt): LongInt;
  external 'SendMessageW@user32.dll stdcall';

// =========================================================
//  CONSTANTES ET VARIABLES GLOBALES
// =========================================================

const
  DEFAULT_MYPROJECTS  = 'C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\projects\myprojects';
  CLONE_URL           = '{#CloneURL}';
  RELEASE_EXE_URL     = '{#ReleaseExeURL}';
  BRIDGE_PORT         = 5001;

var
  PageDir     : TInputDirWizardPage;
  PageInstall : TWizardPage;
  InstMemo    : TMemo;
  InstStatus  : TLabel;
  LogFilePath : String;
  InstallOK   : Boolean;

  gDest    : String;  // myprojects\SysView V6  (wallpaper + sources)
  gMgrExe  : String;  // SysViewManager.exe (chemin final)


// =========================================================
//  HELPERS
// =========================================================

procedure AppendLog(const S: String);
begin
  if Assigned(InstMemo) then begin
    InstMemo.Lines.Add('[' + GetDateTimeString('hh:nn:ss', #0, #0) + '] ' + S);
    SendMessage(InstMemo.Handle, $00B7, 0, 0);
    WizardForm.Refresh;
  end;
  if LogFilePath <> '' then
    SaveStringToFile(LogFilePath, '[' + GetDateTimeString('hh:nn:ss', #0, #0) + '] ' + S + #13#10, True);
end;

procedure SetStatus(const S: String);
begin
  if Assigned(InstStatus) then begin
    InstStatus.Caption := S;
    WizardForm.Refresh;
  end;
  AppendLog(S);
end;

function ExecStep(const Exe, Params: String; AllowCode: Integer): Boolean;
var
  OutFile : String;
  RC      : Integer;
  Lines   : TStringList;
  I       : Integer;
begin
  OutFile := ExpandConstant('{tmp}\sv_out.log');
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
  Result := (RC = 0) or (RC = AllowCode);
end;

function ExecPS(const PSCmd: String): Boolean;
begin
  Result := ExecStep('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "' + PSCmd + '"', 0);
end;

function FindInPath(const ExeName: String): String;
var
  RC : Integer; OutFile : String; Lines : TStringList;
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
    finally Lines.Free; end;
  end;
end;

function PortListening(Port: Integer): Boolean;
var RC: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/c netstat -ano | findstr ":' + IntToStr(Port) + ' " | findstr "LISTENING" > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  Result := RC = 0;
end;

procedure KillPort(Port: Integer);
var RC: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/c for /f "tokens=5" %p in (''netstat -ano 2^>nul ^| findstr ":' +
    IntToStr(Port) + ' " ^| findstr "LISTENING"'') do taskkill /PID %p /F /T > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
end;


// =========================================================
//  ETAPE 1 -- TELECHARGEMENT DU WALLPAPER (sources + HTML)
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

  if DirExists(TmpDir)   then ExecPS('Remove-Item ''' + TmpDir  + ''' -Recurse -Force -EA SilentlyContinue');
  if FileExists(ZipFile) then DeleteFile(ZipFile);

  // Essai 1 : git clone
  SetStatus('[1/3] Telechargement via git clone...');
  if FindInPath('git') <> '' then begin
    Exec(ExpandConstant('{cmd}'),
      '/c git clone --depth 1 --branch master "' + CLONE_URL + '" "' + TmpDir + '\SysView-V6-master"'
      + ' >> "' + LogFilePath + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RC);
    if RC = 0 then SrcDir := TmpDir + '\SysView-V6-master';
  end;

  // Essai 2 : ZIP HTTPS
  if SrcDir = '' then begin
    SetStatus('[1/3] git absent -- telechargement HTTPS...');
    ExecPS('[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      '$ProgressPreference=''SilentlyContinue'';' +
      'Invoke-WebRequest ''https://github.com/Mrtt555/SysView-V6/archive/refs/heads/master.zip''' +
      ' -OutFile ''' + ZipFile + ''' -UseBasicParsing -ErrorAction Stop');
    if FileExists(ZipFile) then begin
      SetStatus('[1/3] Extraction...');
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
    AppendLog('[ERREUR] Telechargement echoue.');
    Exit;
  end;

  // Copier vers gDest (XF runtime_config.json = ne pas ecraser la config user)
  SetStatus('[1/3] Copie vers ' + gDest + '...');
  if DirExists(gDest) then begin
    Exec(ExpandConstant('{cmd}'),
      '/c robocopy "' + SrcDir + '" "' + gDest + '" /E /IS /IT /PURGE' +
      ' /XF "runtime_config.json" /XD "logs"' +
      ' >> "' + LogFilePath + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RoboCRC);
    Result := RoboCRC < 8;
  end else begin
    Result := ExecPS('Move-Item ''' + SrcDir + ''' ''' + gDest + '''');
  end;

  if Result then begin
    ExecPS('Remove-Item ''' + TmpDir + ''' -Recurse -Force -EA SilentlyContinue');
    AppendLog('[OK] Wallpaper installe : ' + gDest);
  end;
end;


// =========================================================
//  ETAPE 2 -- SYSVIEWMANAGER.EXE (pre-built ou compile)
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
  Result := False;

  // Chemin final de l'exe dans le dossier wallpaper
  gMgrExe := gDest + '\SysViewManager.exe';

  // --- Essai 1 : telecharger l'exe pre-compile depuis GitHub Releases ---
  SetStatus('[2/3] Telechargement SysViewManager.exe (release)...');
  DlExe := ExpandConstant('{tmp}\SysViewManager_dl.exe');
  DeleteFile(DlExe);

  ExecPS('[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
    '$ProgressPreference=''SilentlyContinue'';' +
    'Invoke-WebRequest ''' + RELEASE_EXE_URL + ''' -OutFile ''' + DlExe + ''' -UseBasicParsing -EA Stop');

  if FileExists(DlExe) then begin
    FileCopy(DlExe, gMgrExe, True);
    DeleteFile(DlExe);
    AppendLog('[OK] SysViewManager.exe telecharge depuis GitHub Releases.');
    Result := True;
    Exit;
  end;

  AppendLog('[INFO] Release introuvable -- compilation depuis les sources...');

  // --- Fallback : dotnet publish ---
  MgrProj := gDest + '\SysViewManager\SysViewManager.csproj';
  PubExe  := gDest + '\SysViewManager\bin\Release\net8.0-windows\win-x64\publish\SysViewManager.exe';

  if not FileExists(MgrProj) then begin
    AppendLog('[ERREUR] Projet introuvable : ' + MgrProj);
    Exit;
  end;

  // Chercher dotnet >= 8
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
        if MajVer < 8 then DotNet := '';
      end;
    finally Lines.Free; end;
  end;

  if DotNet = '' then begin
    SetStatus('[2/3] SDK .NET 8 absent -- telechargement...');
    ExecPS('[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      '$ProgressPreference=''SilentlyContinue'';' +
      '$f=''' + ExpandConstant('{tmp}') + '\dotnet-install.ps1'';' +
      'Invoke-WebRequest ''https://dot.net/v1/dotnet-install.ps1'' -OutFile $f -UseBasicParsing;' +
      '& $f -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"');
    DotNet := GetEnv('USERPROFILE') + '\.dotnet\dotnet.exe';
    if not FileExists(DotNet) then begin
      AppendLog('[ERREUR] Installation SDK .NET 8 echouee.');
      Exit;
    end;
  end;

  SetStatus('[2/3] Compilation SysViewManager (~2 min)...');
  Exec(ExpandConstant('{cmd}'),
    '/c "' + DotNet + '" publish "' + MgrProj + '"' +
    ' -c Release -r win-x64 --self-contained true' +
    ' -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true' +
    ' -p:DebugType=none --nologo -v minimal >> "' + LogFilePath + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  if not FileExists(PubExe) then begin
    AppendLog('[ERREUR] Compilation echouee -- voir ' + LogFilePath);
    Exit;
  end;

  // Copier l'exe compile au chemin final
  FileCopy(PubExe, gMgrExe, True);
  AppendLog('[OK] SysViewManager.exe compile et copie.');
  Result := True;
end;


// =========================================================
//  ETAPE 3 -- DEMARRAGE AUTOMATIQUE + LANCEMENT
// =========================================================

function StepStart: Boolean;
var
  RC, I : Integer;
begin
  Result := False;
  SetStatus('[3/3] Configuration demarrage automatique...');

  // Creer dossier donnees %AppData%\SysViewManager\
  ExecPS('$d="$env:APPDATA\SysViewManager"; if(!(Test-Path $d)){New-Item -ItemType Directory -Path $d -Force | Out-Null}');
  AppendLog('[OK] Dossier %AppData%\SysViewManager\ cree.');

  // Tache planifiee ONLOGON HIGHEST (lancement admin au demarrage)
  Exec(ExpandConstant('{cmd}'),
    '/c schtasks /delete /tn "SysViewManager" /f > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  Exec(ExpandConstant('{cmd}'),
    '/c schtasks /create /tn "SysViewManager"'
    + ' /tr "\"' + gMgrExe + '\""'
    + ' /sc ONLOGON /rl HIGHEST /f >> "' + LogFilePath + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, RC);

  if RC = 0 then
    AppendLog('[OK] Tache planifiee SysViewManager configuree (ONLOGON / HIGHEST).')
  else begin
    AppendLog('[AVERT] Tache planifiee impossible -- raccourci Startup (sans UAC eleve).');
    SaveStringToFile(
      ExpandConstant('{userstartup}') + '\SysViewManager.bat',
      '@echo off' + #13#10 + 'start "" "' + gMgrExe + '"' + #13#10,
      False);
  end;

  // Liberer le port 5001 si deja utilise
  KillPort(BRIDGE_PORT);
  Sleep(500);

  // Lancer SysViewManager
  SetStatus('[3/3] Lancement de SysViewManager...');
  if RC = 0 then
    Exec(ExpandConstant('{cmd}'),
      '/c schtasks /run /tn "SysViewManager" > nul 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, RC)
  else
    Exec(gMgrExe, '', gDest, SW_HIDE, ewNoWait, RC);

  // Attendre port 5001 (max 30s)
  I := 0;
  while (I < 15) and not PortListening(BRIDGE_PORT) do begin
    Sleep(2000); I := I + 1;
  end;

  if PortListening(BRIDGE_PORT) then
    AppendLog('[OK] SysViewManager actif -- http://127.0.0.1:' + IntToStr(BRIDGE_PORT) + '/v1/status')
  else
    AppendLog('[AVERT] Bridge non detecte -- SysViewManager se lance peut-etre en arriere-plan.');

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
  gDest := PageDir.Values[0] + '\SysView V6';

  LogDir := gDest + '\logs';
  if not DirExists(gDest)   then CreateDir(gDest);
  if not DirExists(LogDir)  then CreateDir(LogDir);
  LogFilePath := LogDir + '\setup.log';
  SaveStringToFile(LogFilePath,
    '================================================' + #13#10 +
    'SysView V6 -- Setup  [' + GetDateTimeString('dd/mm/yyyy hh:nn:ss', #0, #0) + ']' + #13#10 +
    '================================================' + #13#10, False);

  AppendLog('Dossier cible : ' + gDest);

  SetStatus('[1/3] Telechargement du wallpaper...');
  if not StepDownload then begin
    MsgBox('Echec du telechargement.' + #13#10 +
           'Verifiez la connexion internet.', mbError, MB_OK);
    Exit;
  end;

  SetStatus('[2/3] Obtention de SysViewManager.exe...');
  if not StepGetExe then begin
    MsgBox('Impossible d''obtenir SysViewManager.exe.' + #13#10 +
           'Verifiez le log : ' + LogFilePath, mbError, MB_OK);
    Exit;
  end;

  SetStatus('[3/3] Demarrage...');
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
  PageDir := CreateInputDirPage(wpWelcome,
    CustomMessage('DirPageTitle'),
    CustomMessage('DirPageDesc'),
    'Le sous-dossier "SysView V6" sera cree automatiquement.',
    False, '');
  PageDir.Add('');
  PageDir.Values[0] := DEFAULT_MYPROJECTS;

  PageInstall := CreateCustomPage(PageDir.ID,
    CustomMessage('InstPageTitle'),
    CustomMessage('InstPageDesc'));

  InstStatus := TLabel.Create(WizardForm);
  InstStatus.Parent  := PageInstall.Surface;
  InstStatus.Left    := 0; InstStatus.Top := 0;
  InstStatus.Width   := PageInstall.SurfaceWidth;
  InstStatus.Height  := 20;
  InstStatus.Caption := 'Preparation...';
  InstStatus.Font.Style := [fsBold];

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
    if not InstallOK then Result := False;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result :=
    'Dossier wallpaper :' + NewLine +
    Space + PageDir.Values[0] + '\SysView V6' + NewLine + NewLine +
    'Donnees utilisateur :' + NewLine +
    Space + '%AppData%\SysViewManager\' + NewLine + NewLine +
    'Operations :' + NewLine +
    Space + '1. Telechargement wallpaper SysView V6 (GitHub)' + NewLine +
    Space + '2. SysViewManager.exe (pre-built GitHub Releases, fallback : compile)' + NewLine +
    Space + '3. Tache planifiee ONLOGON / admin + lancement' + NewLine + NewLine +
    'Aucune dependance Python requise.';
end;

procedure DeinitializeSetup;
begin
end;
