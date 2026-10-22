; setup.iss — Lumo Engine beta installer (built by tools\mkesetup.ps1)
; Requires defines: Stage (path to dist staging root), Version, OutDir

#ifndef Stage
  #define Stage "dist"
#endif
#ifndef Version
  #define Version "0.1.0"
#endif
#ifndef OutDir
  #define OutDir "dist"
#endif
#ifndef RepoRoot
  #define RepoRoot "."
#endif

[Setup]
AppId={{8E7C0C6F-3A5D-4B7E-9F21-8B0A5C1D0E02}
AppName=Lumo Engine
AppVersion={#Version}
AppPublisher=Lumo Engine
AppPublisherURL=https://github.com/lumoengineofficial/lumo
DefaultDirName={autopf}\Lumo Engine
DefaultGroupName=Lumo Engine
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutDir}
OutputBaseFilename=setuplumo
SetupIconFile={#RepoRoot}\src\Lumo.Editor\Assets\lumo_icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Editor\Lumo.Editor.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "playgameicon"; Description: "Add 'Play Wildlands' desktop shortcut"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Editor app (includes bundled Projects\Wildlands, plugins, README)
Source: "{#Stage}\_editor\*"; DestDir: "{app}\Editor"; Flags: ignoreversion recursesubdirs createallsubdirs
; Standalone game player (optional component)
Source: "{#Stage}\_game\*"; DestDir: "{app}\Wildlands"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: game

[Components]
Name: "editor"; Description: "Lumo Engine editor"; Types: full compact; Flags: fixed
Name: "game"; Description: "Wildlands game (standalone player)"; Types: full

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Editor only"; Flags: iscustom

[Icons]
Name: "{group}\Lumo Engine Editor"; Filename: "{app}\Editor\Lumo.Editor.exe"
Name: "{group}\Play Wildlands"; Filename: "{app}\Wildlands\Lumo.Runtime.exe"; Components: game
Name: "{group}\Uninstall Lumo Engine"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Lumo Engine Editor"; Filename: "{app}\Editor\Lumo.Editor.exe"; Tasks: desktopicon
Name: "{autodesktop}\Play Wildlands"; Filename: "{app}\Wildlands\Lumo.Runtime.exe"; Components: game; Tasks: playgameicon

[Run]
Filename: "{app}\Editor\Lumo.Editor.exe"; Description: "{cm:LaunchProgram,Lumo Engine Editor}"; Flags: nowait postinstall skipifsilent
