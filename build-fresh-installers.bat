@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "VERSION=2.6.7"
set "NO_PAUSE=false"
set "VERSION_ARG="
set "EXPECT_VERSION_VALUE=false"
set "VERSION_PROMPT=Bitte Version fuer alle Installer eingeben (Default 2.6.7): "

for %%A in (%*) do (
    set "ARG=%%~A"

    if /I "!EXPECT_VERSION_VALUE!"=="true" (
        set "VERSION_ARG=version=%%~A"
        set "EXPECT_VERSION_VALUE=false"
    )

    if /I "%%~A"=="no-pause" set "NO_PAUSE=true"
    if /I "!ARG:~0,8!"=="version=" set "VERSION_ARG=%%~A"
    if /I "!ARG!"=="version" set "EXPECT_VERSION_VALUE=true"
)

if defined VERSION_ARG (
    for /f "tokens=1,* delims==" %%K in ("%VERSION_ARG%") do set "VERSION=%%L"
) else (
    set /p "VERSION=!VERSION_PROMPT!"
)

if not defined VERSION set "VERSION=2.6.7"

for /f "tokens=* delims= " %%V in ("%VERSION%") do set "VERSION=%%V"
if /I "%VERSION:~0,1%"=="v" set "VERSION=%VERSION:~1%"
if "%VERSION:~0,1%"=="." set "VERSION=%VERSION:~1%"

echo %VERSION%| findstr /R /C:"^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" /C:"^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
if errorlevel 1 (
    echo.
    echo FEHLER: Ungueltige Versionszeichenfolge '%VERSION%'. Erwartet wird z.B. 2.6.7 oder 2.6.7.9
    if /I "%NO_PAUSE%"=="false" pause
    exit /b 1
)

echo ==========================================
echo HyperTool Fresh Installer Bundle
echo ROOT: %ROOT%
echo VERSION: %VERSION%
echo ==========================================
echo.

echo [1/2] Erzeuge frischen WinUI Host Installer...
call "%ROOT%build-installer-host.bat" "version=%VERSION%" no-version-prompt no-pause
if errorlevel 1 goto :fail

echo [2/2] Erzeuge frischen Guest Installer...
call "%ROOT%build-installer-guest.bat" "version=%VERSION%" no-version-prompt no-pause
if errorlevel 1 goto :fail

echo.
echo SUCCESS: Alle Installer wurden frisch erzeugt.
echo Host:     %ROOT%dist\installer-winui
echo Guest:    %ROOT%dist\installer-guest
if /I "%NO_PAUSE%"=="false" pause
exit /b 0

:fail
echo.
echo FEHLER: Mindestens ein Installer-Build ist fehlgeschlagen.
if /I "%NO_PAUSE%"=="false" pause
exit /b 1
