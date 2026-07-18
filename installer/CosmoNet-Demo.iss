#define MyAppName "CosmoNet"
#define MyAppVersion "0.1.0-demo"
#define MyAppPublisher "CosmoNet"
#define MyAppExeName "CosmoNet.App.exe"

[Setup]
AppId={{4B4775E3-6CB4-49F5-A22B-4D638EF9E8D4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\CosmoNet
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=CosmoNet-Demo-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительные значки:"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CosmoNet"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\CosmoNet"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить CosmoNet"; Flags: nowait postinstall skipifsilent
