@echo off
chcp 65001 >nul
rem =============================================================
rem   Sheet Views Map - сборка плагина и установка в Revit
rem
rem   Запуск: двойной щелчок по этому файлу.
rem   Что делает:
rem     1. Собирает плагин под каждую установленную версию Revit.
rem     2. Копирует готовую DLL в %LOCALAPPDATA%\SheetViewsMap\ГОД\
rem     3. Кладёт файл .addin в папку аддинов Revit.
rem
rem   ВАЖНО: перед сборкой закройте Revit. Открытый Revit держит
rem   загруженную DLL и не даёт её обновить.
rem =============================================================
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
set "VERSIONS=2021 2025"
set "DEPLOY=%LOCALAPPDATA%\SheetViewsMap"
set "TMPL=%ROOT%addins\SheetViewsMap.addin.template"
set "BUILT=0"
set "FAILED=0"

echo ============================================================
echo   Sheet Views Map - сборка и установка
echo ============================================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ОШИБКА] Не найден dotnet.
    echo Установите .NET SDK 8: https://dotnet.microsoft.com/download/dotnet/8.0
    exit /b 1
)

tasklist /FI "IMAGENAME eq Revit.exe" 2>nul | find /I "Revit.exe" >nul
if not errorlevel 1 (
    echo [ВНИМАНИЕ] Сейчас запущен Revit.
    echo   Плагин загружается только при старте Revit, а открытая программа
    echo   ещё держит старую DLL. Закройте Revit и запустите build.cmd
    echo   ещё раз, иначе изменения не подхватятся.
    echo.
)

for %%V in (%VERSIONS%) do call :build_one %%V

echo.
if "!BUILT!"=="0" (
    echo ОШИБКА: ничего не собрано.
    echo Проверьте, что установлен хотя бы один Revit из списка: %VERSIONS%
    exit /b 1
)

if not "!FAILED!"=="0" (
    echo ВНИМАНИЕ: версий с ошибками - !FAILED!. Список ошибок - выше.
    exit /b 1
)

echo ============================================================
echo   Готово. Собрано версий: !BUILT!
echo.
echo   DLL:         %DEPLOY%\ГОД\
echo   Файлы .addin: %APPDATA%\Autodesk\Revit\Addins\ГОД\
echo.
echo   Теперь запустите Revit заново. На ленте появится вкладка
echo   "Sheet Views Map" с кнопкой открытия панели.
echo ============================================================
exit /b 0


rem -------------------------------------------------------------
rem  Сборка одной версии Revit
rem -------------------------------------------------------------
:build_one
set "V=%~1"

if not exist "%ProgramFiles%\Autodesk\Revit !V!\RevitAPI.dll" (
    echo [!V!] Revit !V! не найден в "%ProgramFiles%\Autodesk\Revit !V!" - пропускаю.
    exit /b 0
)

echo --- Сборка для Revit !V! ---
dotnet build "%ROOT%src\Revit!V!\SheetViewsMap!V!.csproj" -c Release -p:RevitVersion=!V!
if errorlevel 1 (
    echo [!V!] ОШИБКА СБОРКИ. Смотрите сообщения выше.
    set /a FAILED+=1
    exit /b 1
)

set "DLL_SRC=%ROOT%out\!V!\SheetViewsMap!V!.dll"
if not exist "!DLL_SRC!" (
    echo [!V!] ОШИБКА: ожидался файл "!DLL_SRC!", но его нет.
    exit /b 1
)

if not exist "!DEPLOY!\!V!" mkdir "!DEPLOY!\!V!"
set "DLL_DST=!DEPLOY!\!V!\SheetViewsMap!V!.dll"

rem Копия может не пройти, если Revit открыт и держит файл.
rem Поэтому проверяем не только код возврата, но и результат побайтового сравнения.
copy /Y "!DLL_SRC!" "!DLL_DST!" >nul 2>&1
if errorlevel 1 goto :copy_failed
fc /B "!DLL_SRC!" "!DLL_DST!" >nul 2>&1
if errorlevel 1 goto :copy_failed
goto :copy_ok

:copy_failed
echo.
echo [!V!] ОШИБКА: не удалось обновить файл
echo        "!DLL_DST!"
echo.
echo   Почти наверняка Revit !V! сейчас открыт: он держит загруженную
echo   DLL, и Windows не даёт её перезаписать.
echo.
echo   Закройте Revit !V! (сохранив проект!) и запустите build.cmd заново.
set /a FAILED+=1
exit /b 1

:copy_ok
if exist "%ROOT%out\!V!\SheetViewsMap!V!.pdb" copy /Y "%ROOT%out\!V!\SheetViewsMap!V!.pdb" "!DEPLOY!\!V!\" >nul 2>&1

set "ADDIN_DIR=%APPDATA%\Autodesk\Revit\Addins\!V!"
if not exist "!ADDIN_DIR!" mkdir "!ADDIN_DIR!"
set "ADDIN_FILE=!ADDIN_DIR!\SheetViewsMap.addin"
set "DLL_PATH=!DLL_DST!"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$t=[IO.File]::ReadAllText($env:TMPL).Replace('__ASSEMBLY__',$env:DLL_PATH); [IO.File]::WriteAllText($env:ADDIN_FILE,$t,(New-Object Text.UTF8Encoding($false)))" >nul 2>&1

if not exist "!ADDIN_FILE!" (
    echo [!V!] ОШИБКА: не удалось создать файл "!ADDIN_FILE!".
    set /a FAILED+=1
    exit /b 1
)

set /a BUILT+=1
echo [!V!] OK - DLL и .addin установлены.
exit /b 0