@echo off
REM Smart Museum — Server Startup
REM Works on Windows. Run this, then launch the C# app from Visual Studio.
REM =========================================================================

echo ========================================
echo  Smart Museum Server
echo ========================================
echo.

REM ── Change to project root ────────────────────────────────────────────────
cd /d "%~dp0"

REM ── Find Python (prefer venv) ─────────────────────────────────────────────
set "VENV_NAME=.venv"
if exist .env (
    for /f "usebackq tokens=1* delims==" %%a in (".env") do (
        set "item=%%a"
        if /i "%%a"=="venv_name" set "VENV_NAME=%%~b"
    )
)

set "PYTHON=%~dp0%VENV_NAME%\Scripts\python.exe"
if not exist "%PYTHON%" (
    set "PYTHON=python"
)

%PYTHON% --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python not found. Install Python or ensure your virtual environment is set correctly.
    pause
    exit /b 1
)

REM ── Optional overrides ────────────────────────────────────────────────────
REM Uncomment and set MUSEUM_CAMERA if auto-detect picks the wrong webcam:
REM set MUSEUM_CAMERA=1

REM Uncomment to use fake YOLO tracks (no GPU / ultralytics needed):
REM set YOLO_CONTEXT_MOCK=1

REM Uncomment to disable individual services:
REM set DISABLE_GESTURE=1
REM set DISABLE_HAND=1
REM set DISABLE_YOLO=1

REM ── Start ─────────────────────────────────────────────────────────────────
echo Using Python: %PYTHON%
echo Starting all services...
echo.
set PYTHONUNBUFFERED=1
%PYTHON% -u python\server\main.py

REM ── If server exits with an error, keep the window open ───────────────────
if errorlevel 1 (
    echo.
    echo Server exited with error code %errorlevel%
    pause
)
