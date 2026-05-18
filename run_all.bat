@echo off
REM Smart Museum - Start Python server + C# app (use start.bat for server only)
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo.
echo ========================================
echo Smart Museum - Complete Startup
echo ========================================
echo.

REM Python server (same venv resolution as start.bat)
set "VENV_NAME=.venv"
if exist .env (
    for /f "usebackq tokens=1* delims==" %%a in (".env") do (
        if /i "%%a"=="venv_name" set "VENV_NAME=%%~b"
    )
)
set "PYTHON=%~dp0%VENV_NAME%\Scripts\python.exe"
if not exist "%PYTHON%" set "PYTHON=python"

echo Starting Python server...
start "Smart Museum Server" cmd /k "cd /d "%~dp0" && "%PYTHON%" -u python\server\main.py"
timeout /t 3 /nobreak >nul

REM Find TUIO_DEMO.exe (Debug / x86 / x64)
set "EXE="
for %%D in ("%~dp0C#\bin\Debug" "%~dp0C#\bin\x64\Debug" "%~dp0C#\bin\x86\Debug") do (
    if exist "%%~D\TUIO_DEMO.exe" set "EXE=%%~D\TUIO_DEMO.exe"
)

if defined EXE (
    echo Starting C# app: !EXE!
    start "Smart Museum App" "!EXE!"
    echo Both services started.
) else (
    echo ERROR: TUIO_DEMO.exe not found. Build C#\TUIO_CSHARP.sln in Visual Studio first.
    pause
)
