@echo off
setlocal enabledelayedexpansion
title AV Orch - Installer for AV Tools Ribbon (pyRevit, Revit 2023+)

echo ======================================================================
echo                     AV Orch (AV Tools)
echo       Revit pyRevit Ribbon Installation (Revit 2023 Forward)
echo ======================================================================
echo.

set "TARGET_BASE=%APPDATA%\pyRevit\Extensions"
set "AV_EXT=%TARGET_BASE%\AV Tools.extension"
set "AV_TAB=%AV_EXT%\AV Tools.tab"
set "AI_PANEL=%AV_TAB%\AI.panel"
set "BUTTON_TARGET=%AI_PANEL%\AV Orch.pushbutton"

set "SRC_BUTTON=%~dp0AV Tools.extension\AV Tools.tab\AI.panel\AV Orch.pushbutton"
set "SRC_EXT_JSON=%~dp0AV Tools.extension\extension.json"

if not exist "!SRC_BUTTON!" (
    echo [ERROR] Could not find the source "AV Orch.pushbutton" folder!
    pause
    exit /b 1
)

echo [1/3] Preparing shared AV Tools ribbon directory...
if not exist "!TARGET_BASE!" mkdir "!TARGET_BASE!" 2>nul
if not exist "!AV_EXT!" mkdir "!AV_EXT!" 2>nul
if not exist "!AV_TAB!" mkdir "!AV_TAB!" 2>nul
if not exist "!AI_PANEL!" mkdir "!AI_PANEL!" 2>nul

:: Preserve existing AV Tools suite metadata (e.g. from NX Xplode)
if not exist "!AV_EXT!\extension.json" (
    copy /Y "!SRC_EXT_JSON!" "!AV_EXT!\extension.json" >nul 2>nul
)

echo [2/3] Installing AV Orch button into shared [ AV Tools ] ribbon...
if exist "!BUTTON_TARGET!" (
    rd /s /q "!BUTTON_TARGET!" 2>nul
)
xcopy /E /I /Y "!SRC_BUTTON!" "!BUTTON_TARGET!" >nul

echo [3/3] Registering pyRevit extensions search path...
where pyrevit >nul 2>nul
if %errorlevel% equ 0 (
    pyrevit extensions paths add "!TARGET_BASE!" >nul 2>nul
)

echo.
echo [SUCCESS] AV Orch has been added to the shared [ AV Tools ] ribbon!
echo Existing AV Tools plugins (such as NX Xplode) have been preserved.
echo Reload pyRevit or restart Revit (2023+) to see [ AV Tools ] ^> [ AI ] ^> [ AV Orch ].
pause
