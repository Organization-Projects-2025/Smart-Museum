$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

# -----------------------------
# Configuration (optional)
# -----------------------------
$env:PYTHONUNBUFFERED = "1"
$env:MUSEUM_CAMERA = $env:MUSEUM_CAMERA ?? "0"
$env:YOLO_CONTEXT_MOCK = $env:YOLO_CONTEXT_MOCK ?? "1"   # 1=mock, 0=real (pip install ultralytics)
$env:HAND_TRACK_PORT = $env:HAND_TRACK_PORT ?? "5004"    # 3D hand tracker port

# Choose server mode:
# $env:USE_UNIFIED_SERVER = "1"  # Set to "1" to use unified server (recommended)
$useUnified = $env:USE_UNIFIED_SERVER -eq "1"

Write-Host "Starting Smart-Museum Python servers…" -ForegroundColor Cyan
Write-Host "  - camera index: $($env:MUSEUM_CAMERA)"
Write-Host "  - YOLO_CONTEXT_MOCK: $($env:YOLO_CONTEXT_MOCK)"
Write-Host "  - HAND_TRACK_PORT: $($env:HAND_TRACK_PORT)"
Write-Host "  - Server mode: $(if ($useUnified) { 'UNIFIED (single process)' } else { 'LEGACY (multiple processes)' })"
Write-Host ""

if ($useUnified) {
    # NEW: Unified server (single process with fault isolation)
    Write-Host "Using Unified Server (recommended)" -ForegroundColor Green
    Write-Host "All services run in one process with fault isolation" -ForegroundColor Gray
    Write-Host ""

    $unified = Start-Process -FilePath "python" -ArgumentList "python\server\unified_museum_server.py" -PassThru

    Write-Host "Running:" -ForegroundColor Green
    Write-Host "  unified_museum_server.py pid=$($unified.Id) (ports 5000-5004)"
    Write-Host ""
    Write-Host "Now run the C# app from Visual Studio."
    Write-Host "Press Ctrl+C to stop the unified server."
    Write-Host ""

    # Keep this PowerShell session alive while server runs
    Wait-Process -Id $unified.Id
} else {
    # LEGACY: Multiple separate processes
    Write-Host "Using Legacy Multi-Process Mode" -ForegroundColor Yellow
    Write-Host "Consider setting USE_UNIFIED_SERVER=1 for better fault isolation" -ForegroundColor Gray
    Write-Host ""

    # museum_vision_server.py exposes:
    #   5000 face/bluetooth
    #   5001 gesture
    #   5002 gaze/emotion
    #   5003 yolo context
    $vision = Start-Process -FilePath "python" -ArgumentList "python\server\museum_vision_server.py" -PassThru

    # hand_tracker_service.py uses HAND_TRACK_PORT (default 5004)
    $hand = Start-Process -FilePath "python" -ArgumentList "python\server\hand_tracker_service.py" -PassThru

    Write-Host "Running:" -ForegroundColor Green
    Write-Host "  museum_vision_server.py pid=$($vision.Id) (5000/5001/5002/5003)"
    Write-Host "  hand_tracker_service.py pid=$($hand.Id) (HAND_TRACK_PORT=$($env:HAND_TRACK_PORT))"
    Write-Host ""
    Write-Host "Now run the C# app from Visual Studio."
    Write-Host "Press Ctrl+C here, then close the Python windows, or stop the PIDs above."

    # Keep this PowerShell session alive while servers run.
    Wait-Process -Id @($vision.Id, $hand.Id)
}

