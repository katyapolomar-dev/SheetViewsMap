; ============================================================
;  PallMall — установщик плагина для Autodesk Revit
;  Сборка: make_installer.cmd  (или вручную: ISCC PallMall.iss)
;  Поддерживаемые версии Revit: 2021, 2025
;
;  Что делает установщик:
;    1. Кладёт DLL плагина в %LOCALAPPDATA%\PallMall\<год>\
;    2. Для каждой УСТАНОВЛЕННОЙ версии Revit создаёт .addin-манифест
;       в %APPDATA%\Autodesk\Revit\Addins\<год>\
;    3. Деинсталлятор удаляет и DLL, и .addin-манифесты.
;    4. Убирает установку прежнего имени (Sheet Views Map) и переносит
;       настройки пользователя — иначе Revit загрузил бы два плагина.
;
;  Права администратора не нужны (всё пишется в профиль пользователя).
;  Файл должен быть сохранён в UTF-8 с BOM (кириллица в [Messages]).
; ============================================================

; Версия генерируется make_installer.cmd в файл version.iss
; из Version.txt (единственный источник версии в репозитории).
#include "version.iss"

[Setup]
AppName=PallMall
; Постоянный идентификатор приложения. Без него Inno выводит AppId из AppName,
; и после переименования старую установку пришлось бы чистить вручную.
AppId={{8C3F2A64-7B1D-4E59-9A0C-5D2B6F81E4A7}
; Версия приходит из release_setup\version.iss, который генерирует make_installer.cmd
; из Version.txt (единственный источник версии).
AppVersion={#MyAppVersion}
AppPublisher=katyapolomar-dev
AppPublisherURL=https://github.com/katyapolomar-dev/PallMall
AppSupportURL=https://github.com/katyapolomar-dev/PallMall
AppUpdatesURL=https://github.com/katyapolomar-dev/PallMall/releases
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}

; Всегда ставим в %LOCALAPPDATA%\PallMall (не спрашиваем путь).
DefaultDirName={localappdata}\PallMall
; Не используем путь прошлой установки: старые версии ставили в ...\Setup.
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes

; Установка от имени обычного пользователя, без UAC.
PrivilegesRequired=lowest
; На 64-битной Windows {pf} = "C:\Program Files" (там живёт Revit).
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=.
OutputBaseFilename=PallMall_Setup_v{#MyAppVersion}
SetupIconFile=setup_icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
; Относительные пути отсчитываются от папки с этим .iss (release_setup\out\...).
Source: "out\2021\PallMall2021.dll"; DestDir: "{app}\2021"; Flags: ignoreversion
Source: "out\2025\PallMall2025.dll"; DestDir: "{app}\2025"; Flags: ignoreversion

; Внешний апдейтер: применяет обновление после закрытия Revit.
Source: "out\Updater\PallMall.Updater.exe"; DestDir: "{app}"; Flags: ignoreversion

; Чистим хвосты старых вариантов установщика (папка Setup, шаблон .addin).
[InstallDelete]
Type: filesandordirs; Name: "{localappdata}\PallMall\Setup"
Type: filesandordirs; Name: "{localappdata}\PallMall\addins"
; До переименования плагин назывался Sheet Views Map: удаляем его файлы,
; иначе Revit загрузит старую и новую версию одновременно (один AddInId).
Type: filesandordirs; Name: "{localappdata}\SheetViewsMap"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2021\SheetViewsMap.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\SheetViewsMap.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\SheetViewsMap.addin"

; Деинсталляция убирает .addin-манифесты (файла может не быть — это не ошибка).
[UninstallDelete]
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2021\PallMall.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\PallMall.addin"

; Запись деинсталлятора прежнего имени (AppId выводился из «Sheet Views Map»).
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\Sheet Views Map_is1"; Flags: dontcreatekey deletekey

[Messages]
FinishedLabelNoIcons=Плагин установлен!%n%nЗапустите Revit — на ленте появится вкладка «PallMall» с кнопкой открытия панели.

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
  Result := ExpandConstant('{localappdata}\PallMall\') + Year + '\PallMall' + Year + '.dll';
end;

// Путь к DLL прежнего имени плагина (Sheet Views Map) для указанного года.
function LegacyPluginDllPath(const Year: String): String;
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
// Проверяем и новое имя, и прежнее: старую папку установщик удаляет целиком.
function CountLockedDlls(): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to GetArrayLength(RevitVersions) - 1 do
    if RevitInstalled(RevitVersions[I]) then
    begin
      if IsFileLocked(PluginDllPath(RevitVersions[I])) then
        Result := Result + 1;
      if IsFileLocked(LegacyPluginDllPath(RevitVersions[I])) then
        Result := Result + 1;
    end;

  // Revit 2023 установщик не обслуживает, но старый плагин мог быть поставлен
  // для него через build.cmd — его DLL тоже может держать открытый Revit.
  if RevitInstalled('2023') and IsFileLocked(LegacyPluginDllPath('2023')) then
    Result := Result + 1;
end;

// Копирует файл, только если копии ещё нет (настройки новой версии не затираем).
procedure CopyFileIfMissing(const Source, Dest: String);
begin
  if FileExists(Source) and not FileExists(Dest) then
    CopyFile(Source, Dest, False);
end;

// Переносит пользовательские настройки прежнего имени в новую папку.
// Вызывается до удаления старой папки из [InstallDelete].
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  OldDir: String;
  NewDir: String;
begin
  Result := '';
  OldDir := ExpandConstant('{localappdata}\SheetViewsMap');
  NewDir := ExpandConstant('{localappdata}\PallMall');

  if not DirExists(OldDir) then
    Exit;

  ForceDirectories(NewDir);
  CopyFileIfMissing(OldDir + '\settings.ini', NewDir + '\settings.ini');
  CopyFileIfMissing(OldDir + '\window.ini', NewDir + '\window.ini');
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
  AddinFile := AddinDir + '\PallMall.addin';

  // Только ASCII: Revit парсит .addin как UTF-8, кириллица тут вызывала кракозябры.
  Content :=
    '<?xml version="1.0" encoding="utf-8"?>' #13#10 +
    '<RevitAddIns>' #13#10 +
    '  <AddIn Type="Application">' #13#10 +
    '    <Name>PallMall</Name>' #13#10 +
    '    <Assembly>' + PluginDllPath(Year) + '</Assembly>' #13#10 +
    '    <AddInId>9f2a7c41-58d6-4b0e-9a73-1c4e8d5f0b26</AddInId>' #13#10 +
    '    <FullClassName>PallMall.App</FullClassName>' #13#10 +
    '    <VendorId>PLML</VendorId>' #13#10 +
    '    <VendorDescription>PallMall</VendorDescription>' #13#10 +
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
