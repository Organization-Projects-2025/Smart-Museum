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
if exist ".venv\Scripts\python.exe" (
    set PYTHON=.venv\Scripts\python.exe
) else (
    set PYTHON=python
)

%PYTHON% --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python not found. Install Python or create a .venv first.
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
%PYTHON% python\server\main.py

REM ── If server exits with an error, keep the window open ───────────────────
if errorlevel 1 (
    echo.
    echo Server exited with error code %errorlevel%
    pause
)
