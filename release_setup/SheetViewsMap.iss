; ============================================================
;  Sheet Views Map — установщик плагина для Autodesk Revit
;  Сборка: make_installer.cmd  (или вручную: ISCC SheetViewsMap.iss)
;  Поддерживаемые версии Revit: 2021, 2025
;
;  Что делает установщик:
;    1. Кладёт DLL плагина в %LOCALAPPDATA%\SheetViewsMap\<год>\
;    2. Для каждой УСТАНОВЛЕННОЙ версии Revit создаёт .addin-манифест
;       в %APPDATA%\Autodesk\Revit\Addins\<год>\
;    3. Деинсталлятор удаляет и DLL, и .addin-манифесты.
;
;  Права администратора не нужны (всё пишется в профиль пользователя).
;  Файл должен быть сохранён в UTF-8 с BOM (кириллица в [Messages]).
; ============================================================

[Setup]
AppName=Sheet Views Map
AppVersion=1.0.0
AppPublisher=katyapolomar-dev
AppPublisherURL=https://github.com/katyapolomar-dev/SheetViewsMap
AppSupportURL=https://github.com/katyapolomar-dev/SheetViewsMap
AppUpdatesURL=https://github.com/katyapolomar-dev/SheetViewsMap/releases
VersionInfoVersion=1.0.0
VersionInfoProductVersion=1.0.0

; Всегда ставим в %LOCALAPPDATA%\SheetViewsMap (не спрашиваем путь).
DefaultDirName={localappdata}\SheetViewsMap
; Не используем путь прошлой установки: старые версии ставили в ...\Setup.
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes

; Установка от имени обычного пользователя, без UAC.
PrivilegesRequired=lowest
; На 64-битной Windows {pf} = "C:\Program Files" (там живёт Revit).
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=.
OutputBaseFilename=SheetViewsMap_Setup_v1.0.0
SetupIconFile=setup_icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
; Относительные пути отсчитываются от папки с этим .iss (release_setup\out\...).
Source: "out\2021\SheetViewsMap2021.dll"; DestDir: "{app}\2021"; Flags: ignoreversion
Source: "out\2025\SheetViewsMap2025.dll"; DestDir: "{app}\2025"; Flags: ignoreversion

; Чистим хвосты старых вариантов установщика (папка Setup, шаблон .addin).
[InstallDelete]
Type: filesandordirs; Name: "{localappdata}\SheetViewsMap\Setup"
Type: filesandordirs; Name: "{localappdata}\SheetViewsMap\addins"

; Деинсталляция убирает .addin-манифесты (файла может не быть — это не ошибка).
[UninstallDelete]
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2021\SheetViewsMap.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\SheetViewsMap.addin"

[Messages]
FinishedLabelNoIcons=Плагин установлен!%n%nЗапустите Revit — на ленте появится вкладка «Sheet Views Map» с кнопкой открытия панели.

[Code]
const
  GENERIC_WRITE = $40000000;
  OPEN_EXISTING = 3;
  // FILE_ATTRIBUTE_NORMAL уже определён в Inno Setup.

var
  // Чтобы добавить версию Revit (например, 2023): допишите год сюда
  // и добавьте по одной строке в [Files] и [UninstallDelete] выше.
  RevitVersions: array of String;

procedure InitRevitVersions();
begin
  SetArrayLength(RevitVersions, 2);
  RevitVersions[0] := '2021';
  RevitVersions[1] := '2025';
end;

// Установлен ли Revit указанного года.
function RevitInstalled(const Year: String): Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\Autodesk\Revit ' + Year + '\RevitAPI.dll'));
end;

// Путь к DLL плагина для указанного года.
// Внимание: {app} нельзя разворачивать в InitializeSetup (ещё не инициализирован),
// поэтому используем {localappdata} — каталог установки фиксированный.
function PluginDllPath(const Year: String): String;
begin
  Result := ExpandConstant('{localappdata}\SheetViewsMap\') + Year + '\SheetViewsMap' + Year + '.dll';
end;

function CreateFileW(lpFileName: String; dwDesiredAccess: Cardinal; dwShareMode: Cardinal; lpSecurityAttributes: Cardinal; dwCreationDisposition: Cardinal; dwFlagsAndAttributes: Cardinal; hTemplateFile: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';

function CloseHandle(hObject: THandle): LongBool;
  external 'CloseHandle@kernel32.dll stdcall';

// Заблокирован ли файл для записи (запущенный Revit держит загруженную DLL).
function IsFileLocked(const FileName: String): Boolean;
var
  H: THandle;
begin
  Result := False;
  if not FileExists(FileName) then
    Exit;
  // Эксклюзивное открытие на запись: не вышло — файл занят другим процессом.
  H := CreateFileW(FileName, GENERIC_WRITE, 0, 0, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, 0);
  if Longint(H) = -1 then
    Result := True
  else
    CloseHandle(H);
end;

// Сколько файлов DLL сейчас заблокировано (среди установленных версий Revit).
function CountLockedDlls(): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to GetArrayLength(RevitVersions) - 1 do
    if RevitInstalled(RevitVersions[I]) then
      if IsFileLocked(PluginDllPath(RevitVersions[I])) then
        Result := Result + 1;
end;

function InitializeSetup(): Boolean;
begin
  InitRevitVersions();

  // Ни одной поддерживаемой версии Revit — устанавливать нечего.
  if not RevitInstalled('2021') and not RevitInstalled('2025') then
  begin
    MsgBox('Не найдено ни одной поддерживаемой версии Revit (2021, 2025).' #13#10 +
           'Установите Revit и запустите установщик ещё раз.',
           mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Просим закрыть Revit, пока он держит старые DLL.
  while CountLockedDlls() > 0 do
  begin
    if MsgBox('Revit сейчас запущен: он блокирует файлы плагина.' #13#10 +
              'Закройте Revit и нажмите «Повторить»,' #13#10 +
              'либо «Отмена», чтобы прервать установку.',
              mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;

  Result := True;
end;

// Создаёт .addin-манифест в папке аддинов Revit указанного года.
procedure WriteAddinManifest(const Year: String);
var
  AddinDir: String;
  AddinFile: String;
  Content: String;
begin
  AddinDir := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\') + Year;
  AddinFile := AddinDir + '\SheetViewsMap.addin';

  // Только ASCII: Revit парсит .addin как UTF-8, кириллица тут вызывала кракозябры.
  Content :=
    '<?xml version="1.0" encoding="utf-8"?>' #13#10 +
    '<RevitAddIns>' #13#10 +
    '  <AddIn Type="Application">' #13#10 +
    '    <Name>SheetViewsMap</Name>' #13#10 +
    '    <Assembly>' + PluginDllPath(Year) + '</Assembly>' #13#10 +
    '    <AddInId>9f2a7c41-58d6-4b0e-9a73-1c4e8d5f0b26</AddInId>' #13#10 +
    '    <FullClassName>SheetViewsMap.App</FullClassName>' #13#10 +
    '    <VendorId>SVMP</VendorId>' #13#10 +
    '    <VendorDescription>Sheet Views Map</VendorDescription>' #13#10 +
    '  </AddIn>' #13#10 +
    '</RevitAddIns>';

  ForceDirectories(AddinDir);
  // Содержимое ASCII, поэтому SaveStringToFile даёт корректный UTF-8 без BOM.
  SaveStringToFile(AddinFile, Content, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  I: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    InitRevitVersions();
    for I := 0 to GetArrayLength(RevitVersions) - 1 do
      if RevitInstalled(RevitVersions[I]) then
        WriteAddinManifest(RevitVersions[I]);
  end;
end;
