; Inno Setup script — produces installer\Output\SamuraiGBA-Setup.exe
#define AppName "Samurai GBA"
#define AppVersion "1.0.0"
#define AppExe "SamuraiGBA.exe"

[Setup]
AppId={{6B1C7E52-5A0D-4F0E-9C5B-5A31A0A1F001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Samurai GBA
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SamuraiGBA-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\logo.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#AppExe}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
