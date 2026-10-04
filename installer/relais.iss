; Installateur Relais (Inno Setup 6) — installation par utilisateur, sans droits administrateur.
#define AppExe AddBackslash(SourcePath) + "..\build\Relais.exe"
#define AppVersion GetVersionNumbersString(AppExe)

[Setup]
AppId={{6C1E7A52-3F4B-4E0B-9A3D-52E1A1D2C0F7}
AppName=Relais
AppVersion={#AppVersion}
AppPublisher=Loïc
DefaultDirName={localappdata}\Programs\Relais
DefaultGroupName=Relais
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\build
OutputBaseFilename=Relais-Setup
SetupIconFile=..\relais.ico
UninstallDisplayIcon={app}\Relais.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Files]
Source: "..\build\Relais.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Relais"; Filename: "{app}\Relais.exe"
Name: "{userdesktop}\Relais"; Filename: "{app}\Relais.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Relais.exe"; Description: "Lancer Relais"; Flags: nowait postinstall skipifsilent
