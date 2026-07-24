#define AppName "DQOPR NetPulse"
#define AppExeName "DQOPR-NetPulse.exe"
#define AppPublisher "DQOPR"
#define AppCopyright "Copyright © 2026 DQOPR"
#define AppVersion GetEnv("DQOPR_NETPULSE_VERSION")
#if AppVersion == ""
#define AppVersion "0.2.1"
#endif
#define EnvSourceDir GetEnv("DQOPR_NETPULSE_SOURCE_DIR")
#if EnvSourceDir == ""
#define SourceDir "..\..\dist\DQOPR-NetPulse"
#else
#define SourceDir EnvSourceDir
#endif
#define EnvOutputDir GetEnv("DQOPR_NETPULSE_OUTPUT_DIR")
#if EnvOutputDir == ""
#define OutputDir "..\..\release_artifacts"
#else
#define OutputDir EnvOutputDir
#endif

[Setup]
AppId={{E5408F0A-71DF-4D9A-A273-D8F74FDCB923}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/joaolimappk/dqopr-netpulse
AppSupportURL=https://github.com/joaolimappk/dqopr-netpulse/issues
AppUpdatesURL=https://github.com/joaolimappk/dqopr-netpulse/releases
DefaultDirName={autopf}\DQOPR NetPulse
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=DQOPR-NetPulse-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Internet Quality Monitor and ISP Evidence Reporter
VersionInfoProductName={#AppName}
VersionInfoCopyright={#AppCopyright}
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\DQOPR NetPulse\logs"
