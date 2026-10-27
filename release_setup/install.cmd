@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================================
echo   Установка Sheet Views Map v1.0.0
set "VERSIONS=2021 2023"
set "DEPLOY=%LOCALAPPDATA%\SheetViewsMap"
set "TMPL=%~dp0addins\SheetViewsMap.addin.template"
set "INSTALLED=0"

for %%V in (%VERSIONS%) do (
    if exist "%ProgramFiles%\Autodesk\Revit %%V\RevitAPI.dll" (
        echo [%%V] Установка для Revit %%V...
        if not exist "!DEPLOY!\%%V" mkdir "!DEPLOY!\%%V"
        copy /Y "%~dp0out\%%V\SheetViewsMap%%V.dll" "!DEPLOY!\%%V\" >nul
        if exist "%~dp0out\%%V\SheetViewsMap%%V.pdb" copy /Y "%~dp0out\%%V\SheetViewsMap%%V.pdb" "!DEPLOY!\%%V\" >nul
        
        set "ADDIN_DIR=%APPDATA%\Autodesk\Revit\Addins\%%V"
        if not exist "!ADDIN_DIR!" mkdir "!ADDIN_DIR!"
        set "DLL_PATH=!DEPLOY!\%%V\SheetViewsMap%%V.dll"
        set "ADDIN_FILE=!ADDIN_DIR!\SheetViewsMap.addin"
        set "ADDIN_DIR_FOR_PS=!ADDIN_DIR!"
        
        powershell -NoProfile -ExecutionPolicy Bypass -Command "^$
            $t=[IO.File]::ReadAllText('%TMPL%').Replace('__ASSEMBLY__','!DLL_PATH!');^
            [IO.File]::WriteAllText('!ADDIN_FILE!',$t,(New-Object Text.UTF8Encoding($false)))" >nul 2>&1
        
        if exist "!ADDIN_DIR!\SheetViewsMap.addin" (
            echo [%%V] Установлено успешно.
            set /a INSTALLED+=1
        ) else (
            echo [%%V] ОШИБКА создания .addin.
        )
    ) else (
        echo [%%V] Revit %%V не найден — пропускаю.
    )
)

echo ============================================================
if %INSTALLED%==0 (
    echo   Ни одна версия Revit не установлена или не найдена.
) else (
    echo   Установлено версий: %INSTALLED%
    echo   Перезапустите Revit для загрузки плагина.
echo ============================================================
