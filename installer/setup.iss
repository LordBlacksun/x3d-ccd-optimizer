; X3D CCD Optimizer — Inno Setup Script
; Requires Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
;
; Build prerequisites:
;   1. Publish the app: dotnet publish src/X3DCcdOptimizer -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
;   2. Run Inno Setup Compiler on this script (or: iscc installer/setup.iss)
;
; The script expects files relative to the repo root. Run from the repo root
; or set the /D flag: iscc /DSourceRoot=F:\x3d-ccd-optimizer installer\setup.iss

#ifndef SourceRoot
  #define SourceRoot ".."
#endif

#define MyAppName "X3D CCD Optimizer"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "LordBlacksun"
#define MyAppURL "https://github.com/LordBlacksun/x3d-ccd-optimizer"
#define MyAppExeName "X3DCcdOptimizer.exe"

[Setup]
AppId={{B7F3A2E1-5D4C-4E8B-9F1A-3C6D8E2B7A50}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\X3DCcdOptimizer
DefaultGroupName={#MyAppName}
LicenseFile={#SourceRoot}\LICENSE
InfoBeforeFile={#SourceRoot}\installer\DISCLAIMER.txt
OutputDir={#SourceRoot}\installer\output
OutputBaseFilename=X3DCcdOptimizer-Setup-{#MyAppVersion}
SetupIconFile={#SourceRoot}\src\X3DCcdOptimizer\Resources\app.ico
UninstallDisplayIcon={app}\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; Main executable (from publish output)
Source: "{#SourceRoot}\publish\X3DCcdOptimizer.exe"; DestDir: "{app}"; Flags: ignoreversion

; Data files
Source: "{#SourceRoot}\src\X3DCcdOptimizer\Data\known_games.json"; DestDir: "{app}\Data"; Flags: ignoreversion

; Resources
Source: "{#SourceRoot}\src\X3DCcdOptimizer\Resources\app.ico"; DestDir: "{app}"; Flags: ignoreversion

; Documentation
Source: "{#SourceRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\UserManual.pdf"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
; Start Menu shortcut — run as administrator
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

; Desktop shortcut (optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Registry]
; Set "Run as administrator" compatibility flag on the exe
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; \
  ValueType: string; ValueName: "{app}\{#MyAppExeName}"; ValueData: "RUNASADMIN"; \
  Flags: uninsdeletevalue

[UninstallDelete]
; Clean up any files created at runtime in the install directory
Type: filesandordirs; Name: "{app}\Data"
Type: files; Name: "{app}\app.ico"

[Code]
// Ask user whether to remove settings and data on uninstall
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir: String;
  RunKey: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataDir := ExpandConstant('{userappdata}\X3DCCDOptimizer');
    RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';

    // Remove Start with Windows registry entry if present
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'X3DCCDOptimizer');

    // Ask about user data
    if DirExists(AppDataDir) then
    begin
      if MsgBox('Do you want to remove your settings and data?' + #13#10 + #13#10 +
                'This will delete your config, logs, and recovery data at:' + #13#10 +
                AppDataDir, mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(AppDataDir, True, True, True);
      end;
    end;
  end;
end;
