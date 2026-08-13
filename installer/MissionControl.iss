; Inno Setup script for Mission Control.
;
; Built by build\build-installer.ps1, which publishes a self-contained single-file exe
; and passes the paths in. To compile by hand:
;
;   ISCC.exe /DSourceDir="<publish folder>" /DOutputDir="<dist folder>" installer\MissionControl.iss
;
; The install is per-user by default (PrivilegesRequired=lowest): it lands in
; %LOCALAPPDATA%\Programs, writes only HKCU, and never shows a UAC prompt — so people
; without local admin rights can install it. An admin can still pass /ALLUSERS.

#define AppName        "Mission Control"
#define AppPublisher   "Braden Roper"
#define AppExeName     "MissionControl.exe"
#define AppUrl         "https://github.com/bradenroper/mission-control"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\src\MissionControl\bin\Release\net9.0-windows\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; Never change AppId: it is how Windows recognises an upgrade of an existing install.
AppId={{722A5CB4-71A3-4698-A850-FE34E1155F2E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\Mission Control
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=no

; Per-user by default; /ALLUSERS on the command line still allows a machine-wide install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline

; Mission Control targets Windows 10/11 (DWM live thumbnails + PerMonitorV2 DPI).
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Setup refuses to overwrite a running copy, so ask the user to close it first. The name
; matches the mutex App.xaml.cs creates.
AppMutex=MissionControlSingleInstance
CloseApplications=yes
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename=MissionControl-Setup-{#AppVersion}
SetupIconFile=..\src\MissionControl\Resources\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Mission Control when I sign in"; GroupDescription: "Additional options:"

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "Show all your windows"
Name: "{autoprograms}\{#AppName} Settings"; Filename: "{app}\{#AppExeName}"; Parameters: "--settings"; Comment: "Hotkey, animation and startup options"

[Registry]
; The app manages this key itself from its settings; this seeds it when the task is ticked.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "MissionControl"; ValueData: """{app}\{#AppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start Mission Control now"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // The user may have switched "Start with Windows" on after install, in which case the
  // Run value was written by the app rather than by [Registry] and would be left behind.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'MissionControl');
end;
