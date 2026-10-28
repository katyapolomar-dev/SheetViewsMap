@echo off
rem =============================================================
rem   Sheet Views Map - one-click installer build.
rem
rem   Run: double-click this file.
rem   Steps:
rem     1. Rebuilds plugin DLLs for every installed Revit version.
rem     2. Stages DLLs into release_setup\out\<year>\.
rem     3. Compiles SheetViewsMap_Setup_v1.0.0.exe via Inno Setup.
rem
rem   Result: release_setup\SheetViewsMap_Setup_v1.0.0.exe
rem   NOTE: keep this file ASCII-only. Cyrillic text in a .cmd
rem   with "chcp 65001" corrupts cmd.exe parsing.
rem =============================================================
setlocal enabledelayedexpansion

set "SETUP_DIR=%~dp0"
for %%I in ("%SETUP_DIR%..") do set "ROOT=%%~fI"
set "STAGE=%SETUP_DIR%out"
set "VERSIONS=2021 2025"
set "OUTPUT_NAME=SheetViewsMap_Setup_v1.0.0.exe"

echo ============================================================
echo   Sheet Views Map - building installer
echo ============================================================
echo.

rem --- 1. Locate Inno Setup compiler (ISCC.exe) ---
set "ISCC="
where iscc >nul 2>&1 && set "ISCC=iscc"
if not defined ISCC for %%P in ("D:\InnoSetup7\ISCC.exe" "C:\Program Files\Inno Setup 7\ISCC.exe" "C:\Program Files (x86)\Inno Setup 7\ISCC.exe" "C:\Program Files\Inno Setup 6\ISCC.exe" "C:\Program Files (x86)\Inno Setup 6\ISCC.exe") do (
    if not defined ISCC if exist %%P set "ISCC=%%~P"
)
if not defined ISCC (
    echo [ERROR] Inno Setup compiler ^(ISCC.exe^) not found.
    echo Install Inno Setup: https://jrsoftware.org/isdl.php
    exit /b 1
)
echo Compiler: !ISCC!
echo.

rem --- 1a. Build the external updater (no Revit API needed) ---
echo --- Building updater (SheetViewsMap.Updater.exe) ---
dotnet build "%ROOT%\src\SheetViewsMap.Updater\SheetViewsMap.Updater.csproj" -c Release
if errorlevel 1 (
    echo [ERROR] Updater build failed. See messages above.
    exit /b 1
)
echo.

rem --- 2. Build DLLs for versions with Revit installed ---
for %%V in (%VERSIONS%) do (
    if exist "%ProgramFiles%\Autodesk\Revit %%V\RevitAPI.dll" (
        echo --- Building plugin for Revit %%V ---
        dotnet build "%ROOT%\src\Revit%%V\SheetViewsMap%%V.csproj" -c Release -p:RevitVersion=%%V
        if errorlevel 1 (
            echo [%%V] BUILD FAILED. See messages above.
            exit /b 1
        )
    ) else (
        echo --- Revit %%V not installed, reusing existing DLL from out\%%V ---
    )
)

rem --- 3. Stage DLLs for the installer ---
for %%V in (%VERSIONS%) do (
    if exist "%ROOT%\out\%%V\SheetViewsMap%%V.dll" (
        if not exist "%STAGE%\%%V" mkdir "%STAGE%\%%V"
        copy /Y "%ROOT%\out\%%V\*.dll" "%STAGE%\%%V\" >nul
        if errorlevel 1 (
            echo [%%V] Failed to copy into "%STAGE%\%%V".
            exit /b 1
        )
    ) else (
        echo [ERROR] Missing built DLL for Revit %%V:
        echo         "%ROOT%\out\%%V\SheetViewsMap%%V.dll"
        echo         Install Revit %%V or put the DLL there manually.
        exit /b 1
    )
)

rem --- 3a. Stage the updater ---
if not exist "%STAGE%\Updater" mkdir "%STAGE%\Updater"
copy /Y "%ROOT%\out\Updater\SheetViewsMap.Updater.exe" "%STAGE%\Updater\" >nul
if errorlevel 1 (
    echo [ERROR] Failed to copy SheetViewsMap.Updater.exe.
    exit /b 1
)

rem --- 3b. Pack per-year zip assets for the in-app update check ---
echo.
echo --- Packing update zips ---
for %%V in (%VERSIONS%) do (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%STAGE%\%%V\*' -DestinationPath '%SETUP_DIR%SheetViewsMap-%%V.zip' -Force"
    if errorlevel 1 (
        echo [ERROR] Failed to pack SheetViewsMap-%%V.zip
        exit /b 1
    )
    echo SheetViewsMap-%%V.zip - OK
)

rem --- 4. Compile the installer ---
echo.
echo --- Compiling installer ---
"!ISCC!" "%SETUP_DIR%SheetViewsMap.iss"
if errorlevel 1 (
    echo [ERROR] Inno Setup compilation failed. See messages above.
    exit /b 1
)

echo.
echo ============================================================
echo   Done: "%SETUP_DIR%!OUTPUT_NAME!"
echo ============================================================
exit /b 0
