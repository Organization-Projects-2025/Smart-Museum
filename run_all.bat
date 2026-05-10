@echo off
REM Smart Museum - Complete Startup Script
REM Starts Python server and C# application together

setlocal enabledelayedexpansion

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0
cd /d "%SCRIPT_DIR%"

echo.
echo ========================================
echo Smart Museum - Complete Startup
echo ========================================
echo.

REM Start Python server in a new window
echo Starting Python Socket Server...
start "Python Server" cmd /k "python python_server.py"

REM Give Python server time to start
timeout /t 2 /nobreak

REM Start C# application
echo Starting C# Application...
cd /d "%SCRIPT_DIR%C#\bin\Debug"

if exist "TUIO_DEMO.exe" (
    start "Smart Museum App" TUIO_DEMO.exe
    echo.
    echo ========================================
    echo Both services started successfully!
    echo Python Server: Running in separate window
    echo C# App: Running in separate window
    echo ========================================
    echo.
) else (
    echo ERROR: TUIO_DEMO.exe not found in C#\bin\Debug
    echo Please build the C# project first.
    pause
)
