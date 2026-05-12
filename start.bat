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

REM Uncomment to disable individual services:
REM set DISABLE_GESTURE=1
REM set DISABLE_HAND=1
REM set DISABLE_OBJ_TRACK=1

REM ── Start ─────────────────────────────────────────────────────────────────
echo Using Python: %PYTHON%
echo Starting all services...
echo.
echo Starting Smart Museum Server...
%PYTHON% python\server\main.py
