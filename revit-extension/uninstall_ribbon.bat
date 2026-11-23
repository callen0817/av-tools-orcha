@echo off
setlocal enabledelayedexpansion
title AV Orch - Uninstall from AV Tools Ribbon

set "TARGET_BASE=%APPDATA%\pyRevit\Extensions"
set "AV_EXT=%TARGET_BASE%\AV Tools.extension"
set "AV_TAB=%AV_EXT%\AV Tools.tab"
set "AI_PANEL=%AV_TAB%\AI.panel"
set "BUTTON_TARGET=%AI_PANEL%\AV Orch.pushbutton"

if exist "!BUTTON_TARGET!" (
    rd /s /q "!BUTTON_TARGET!"
    echo Removed AV Orch button from [ AV Tools ] ribbon.
) else (
    echo AV Orch button was not found in [ AV Tools ] ribbon.
)

:: Remove AI.panel only if empty
rd "!AI_PANEL!" 2>nul
:: Remove AV Tools.tab and AV Tools.extension only if no other AV Tools (e.g. NX Xplode) remain
rd "!AV_TAB!" 2>nul
if not exist "!AV_TAB!" (
    del /Q "!AV_EXT!\extension.json" 2>nul
    rd "!AV_EXT!" 2>nul
)

echo Existing AV Tools plugins (such as NX Xplode) were preserved.
pause
