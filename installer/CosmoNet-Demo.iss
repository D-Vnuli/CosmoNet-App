#define MyAppName "CosmoNet"
#define MyAppVersion "0.2.5"
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
OutputBaseFilename=CosmoNet-Setup-0.2.5
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin

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
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить CosmoNet"; Flags: nowait postinstall skipifsilent runascurrentuser
