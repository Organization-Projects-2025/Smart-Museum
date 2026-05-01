@echo off
REM Unified Museum Server Startup Script for Windows
REM This script starts all Python services in a single process with fault isolation

echo ========================================
echo Smart Museum - Unified Server
echo ========================================
echo.

REM Check if Python is available
python --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python is not installed or not in PATH
    pause
    exit /b 1
)

REM Change to project directory
cd /d "%~dp0"

REM Optional: Set camera index if needed
REM set MUSEUM_CAMERA=0

REM Optional: Disable specific services if needed
REM set DISABLE_HAND_TRACK=1
REM set DISABLE_GESTURE=1

REM Run the unified server
echo Starting Unified Museum Server...
echo.
python python\server\unified_museum_server.py

REM If server exits, pause to see any error messages
if errorlevel 1 (
    echo.
    echo Server exited with error code %errorlevel%
    pause
)