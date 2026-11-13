#ifndef AppVersion
  #define AppVersion "1.0.1-preview"
#endif
[Setup]
AppId={{D17C8746-C637-4EA0-935E-E61BC1C33C04}
AppName=Dedsec Monitor
AppVersion={#AppVersion}
VersionInfoVersion=1.0.1.0
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
WizardStyle=modern dark
WizardBackColor=#101310
WizardImageFile=wizard.bmp
WizardImageStretch=yes
WizardImageBackColor=#101310
WizardSmallImageFile=..\src\Dedsec.Monitor\Assets\dedsec.png
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
VersionInfoDescription=Dedsec Monitor Setup

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DesktopShortcut=Создать ярлык на рабочем столе
english.DesktopShortcut=Create a desktop shortcut
russian.WelcomeLabel2=Каждый кадр под контролем.%n%nFPS, процессор, видеокарта и память — в одном приложении. Оверлей, история и запись сессий.%n%nУстановка включает все компоненты. Дополнительно устанавливать .NET или PresentMon не нужно.
english.WelcomeLabel2=Every frame. Under control.%n%nFPS, CPU, GPU and memory in one app. An overlay, live timeline and session recording.%n%nAll components are included. No separate .NET or PresentMon installation is needed.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Dedsec Monitor"; Filename: "{app}\Dedsec.Monitor.exe"
Name: "{autodesktop}\Dedsec Monitor"; Filename: "{app}\Dedsec.Monitor.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "DedsecMonitor"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Dedsec.Monitor.exe"; Description: "Dedsec Monitor"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Font.Color := $70F9C2;
  WizardForm.FinishedHeadingLabel.Font.Color := $70F9C2;
  WizardForm.PageNameLabel.Font.Color := $70F9C2;
  WizardForm.WelcomeLabel2.Caption := CustomMessage('WelcomeLabel2');
end;
