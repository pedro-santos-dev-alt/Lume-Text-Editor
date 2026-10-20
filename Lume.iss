#define AppName "Lume"
#define AppVersion "1.2.0"
#define AppPublisher "Lume"
#define AppExeName "Lume.exe"

[Setup]
AppId={{3B30D168-7FC6-4A5C-B581-67FF7DDB1090}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Lume
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=Lume-Setup-{#AppVersion}-x64
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=TxTEditor\Assets\Lume.ico
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho no ambiente de trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked
Name: "txtassociation"; Description: "Disponibilizar o Lume para abrir ficheiros .txt"; GroupDescription: "Associações de ficheiros:"; Flags: unchecked

[Files]
Source: "TxTEditor\publish\win-x64\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Lume"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Lume"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: none; ValueName: "Lume.Text"; Flags: uninsdeletevalue; Tasks: txtassociation
Root: HKCU; Subkey: "Software\Classes\Lume.Text"; ValueType: string; ValueName: ""; ValueData: "Documento de texto Lume"; Flags: uninsdeletekey; Tasks: txtassociation
Root: HKCU; Subkey: "Software\Classes\Lume.Text\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: txtassociation
Root: HKCU; Subkey: "Software\Classes\Lume.Text\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: txtassociation
Root: HKCU; Subkey: "Software\Lume\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "Lume"; Flags: uninsdeletekey; Tasks: txtassociation
Root: HKCU; Subkey: "Software\Lume\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Editor de texto Lume"; Tasks: txtassociation
Root: HKCU; Subkey: "Software\Lume\Capabilities\FileAssociations"; ValueType: string; ValueName: ".txt"; ValueData: "Lume.Text"; Tasks: txtassociation
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Lume"; ValueData: "Software\Lume\Capabilities"; Flags: uninsdeletevalue; Tasks: txtassociation

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Abrir o Lume"; Flags: postinstall nowait
