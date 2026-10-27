; Sheet Views Map v1.0.0 - Inno Setup Script
[Setup]
AppName=Sheet Views Map
AppVersion=1.0.0
AppPublisher=katyapolomar-dev
AppPublisherURL=https://github.com/katyapolomar-dev/SheetViewsMap
AppSupportURL=https://github.com/katyapolomar-dev/SheetViewsMap/releases/tag/v1.0.0
AppUpdatesURL=https://github.com/katyapolomar-dev/SheetViewsMap/releases
DefaultDirName={localappdata}\SheetViewsMap\Setup
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=D:\katyacoder\SheetViewsMap\release_setup
OutputBaseFilename=SheetViewsMap_Setup_v1.0.0
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно"; Flags: unchecked

[Files]
Source: "D:\katyacoder\SheetViewsMap\release_setup\install.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\katyacoder\SheetViewsMap\release_setup\addins\SheetViewsMap.addin.template"; DestDir: "{app}\addins"; Flags: ignoreversion
Source: "D:\katyacoder\SheetViewsMap\release_setup\out\2021\SheetViewsMap2021.dll"; DestDir: "{app}\out\2021"; Flags: ignoreversion
Source: "D:\katyacoder\SheetViewsMap\release_setup\out\2021\SheetViewsMap2021.pdb"; DestDir: "{app}\out\2021"; Flags: ignoreversion
Source: "D:\katyacoder\SheetViewsMap\release_setup\out\2025\SheetViewsMap2025.dll"; DestDir: "{app}\out\2025"; Flags: ignoreversion
Source: "D:\katyacoder\SheetViewsMap\release_setup\out\2025\SheetViewsMap2025.pdb"; DestDir: "{app}\out\2025"; Flags: ignoreversion

[Icons]
Name: "{group}\Sheet Views Map"; Filename: "{app}\install.cmd"
Name: "{userdesktop}\Sheet Views Map"; Filename: "{app}\install.cmd"; Tasks: desktopicon

[Run]
Filename: "{app}\install.cmd"; StatusMsg: "Установка плагина в Revit..."; Flags: runascurrentuser waituntilterminated postinstall skipifsilent

[Code]
procedure InitializeWizard();
begin
  WizardForm.Caption := 'Установка Sheet Views Map v1.0.0';
end;
