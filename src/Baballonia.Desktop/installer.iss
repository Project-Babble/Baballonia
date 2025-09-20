[Setup]
; Application information
AppName=Baballonia
AppVersion=1.1.0.4
AppVerName=Baballonia v1.1.0.4
AppPublisher=Paradigm Reality Enhancement Laboratories
DefaultDirName={localappdata}\Baballonia
DefaultGroupName=Baballonia
UninstallDisplayName=Baballonia
OutputBaseFilename=Baballonia Setup
;Compression=lzma2/ultra64
;SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64

; UI Settings
SetupIconFile=assets\IconOpaque.ico
WizardImageFile=assets\MUI_WELCOMEFINISHPAGE_BITMAP.bmp
WizardSmallImageFile=assets\MUI_HEADERIMAGE_BITMAP.bmp
DisableProgramGroupPage=yes
DisableWelcomePage=no

; Version information
VersionInfoVersion=1.1.0.4
VersionInfoCompany=Paradigm Reality Enhancement Laboratories
VersionInfoDescription=Baballonia Setup
VersionInfoCopyright=Copyright (C) 2024

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"

[Files]
; Copy all files except Calibration folders
Source: "bin\Release\net8.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "Calibration"
; Copy only Windows calibration files
Source: "bin\Release\net8.0\win-x64\Calibration\Windows\*"; DestDir: "{app}\win-x64\Calibration\Windows"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Baballonia"; Filename: "{app}\win-x64\Baballonia.Desktop.exe"
Name: "{autodesktop}\Baballonia"; Filename: "{app}\win-x64\Baballonia.Desktop.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Baballonia"; ValueType: string; ValueName: ""; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
; Uncomment the following line if you want to run the application after installation
; Filename: "{app}\win-x64\Baballonia.Desktop.exe"; Description: "{cm:LaunchProgram,Baballonia}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Custom code can be added here if needed
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clean up empty parent directories
    RemoveDir(ExpandConstant('{localappdata}\Baballonia'));
  end;
end;