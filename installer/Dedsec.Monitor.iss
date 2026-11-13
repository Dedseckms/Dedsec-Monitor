#ifndef AppVersion
  #define AppVersion "1.0.2"
#endif
#ifndef PublishDirectory
  #define PublishDirectory "..\artifacts\publish"
#endif
[Setup]
AppId={{D17C8746-C637-4EA0-935E-E61BC1C33C04}
AppName=Dedsec Monitor
AppVersion={#AppVersion}
VersionInfoVersion=1.0.2.0
AppPublisher=Dedseckms
AppPublisherURL=https://github.com/Dedseckms/Dedsec-Monitor
AppSupportURL=https://github.com/Dedseckms/Dedsec-Monitor/issues
DefaultDirName={localappdata}\Programs\Dedsec Monitor
DefaultGroupName=Dedsec Monitor
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=Dedsec-Monitor-{#AppVersion}-Setup-x64
SetupIconFile=..\src\Dedsec.Monitor\Assets\dedsec.ico
UninstallDisplayIcon={app}\Dedsec.Monitor.exe
WizardStyle=modern dark includetitlebar hidebevels
WizardSizePercent=110
WizardBackColor=#101310
WizardImageFile=wizard.bmp
WizardImageStretch=yes
WizardImageBackColor=#101310
WizardSmallImageFile=..\src\Dedsec.Monitor\Assets\dedsec.png
WizardSmallImageBackColor=#101310
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableDirPage=auto
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Dedsec Monitor
VersionInfoDescription=Dedsec Monitor Dark Setup {#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DesktopShortcut=Создать ярлык на рабочем столе
english.DesktopShortcut=Create a desktop shortcut
russian.WelcomeLabel2=Каждый кадр под контролем.%n%nFPS, процессор, видеокарта и память — в одном приложении.%n%nВ конце установки автоматически подготовим игровой оверлей и датчики. Нужен интернет; Windows может запросить права администратора. Искать и скачивать компоненты вручную не нужно.
english.WelcomeLabel2=Every frame. Under control.%n%nFPS, CPU, GPU and memory in one app.%n%nSetup automatically prepares the game overlay and sensors. Internet is required; Windows may request administrator permission. No manual component downloads are needed.
russian.ComponentsPreparing=Подготавливаем игровой оверлей и датчики…
english.ComponentsPreparing=Preparing game overlay and sensors…
russian.ComponentsFailed=Подготовка компонентов не завершена. Запустите установщик ещё раз или нажмите «Восстановить компоненты» в приложении.
english.ComponentsFailed=Component setup did not complete. Run setup again or click Restore components in the app.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Dedsec Monitor"; Filename: "{app}\Dedsec.Monitor.exe"
Name: "{autodesktop}\Dedsec Monitor"; Filename: "{app}\Dedsec.Monitor.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "DedsecMonitor"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Dedsec.Monitor.exe"; Description: "Dedsec Monitor"; Flags: nowait postinstall skipifsilent

[Code]
var
  ComponentsNeedRestart: Boolean;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Params: String;
  ExitCode: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  { Only the disposable packaging smoke test uses this explicit opt-out. }
  if ExpandConstant('{param:QA_SKIPCOMPONENTS|0}') = '1' then begin
    Log('QA packaging test: shared system components left unchanged.');
    Exit;
  end;
  WizardForm.StatusLabel.Caption := CustomMessage('ComponentsPreparing');
  Params := '--prepare-components';
  if ActiveLanguage = 'english' then Params := Params + ' --english';
  if WizardSilent then Params := Params + ' --silent-components';
  if not Exec(ExpandConstant('{app}\Dedsec.Monitor.exe'), Params, '', SW_SHOWNORMAL,
    ewWaitUntilTerminated, ExitCode) then
    RaiseException(CustomMessage('ComponentsFailed'));
  if ExitCode = 3010 then ComponentsNeedRestart := True
  else if ExitCode <> 0 then RaiseException(CustomMessage('ComponentsFailed'));
end;

function NeedRestart: Boolean;
begin
  Result := ComponentsNeedRestart;
end;

procedure InitializeWizard;
begin
  WizardForm.Color := $101310;
  WizardForm.MainPanel.Color := $101310;
  WizardForm.Font.Name := 'Segoe UI';
  WizardForm.Font.Color := $E7F0EB;
  WizardForm.WelcomeLabel1.Font.Color := $70F9C2;
  WizardForm.FinishedHeadingLabel.Font.Color := $70F9C2;
  WizardForm.PageNameLabel.Font.Color := $70F9C2;
  WizardForm.WelcomeLabel2.Caption := CustomMessage('WelcomeLabel2');
end;
