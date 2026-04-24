# Installs ALL Python dependencies into this repo's .venv (creates .venv if missing).
# Run from repo root (double-click or: powershell -ExecutionPolicy Bypass -File .\install_python_deps.ps1)
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
Set-Location $Root

$venvPy = Join-Path $Root ".venv\Scripts\python.exe"
if (-not (Test-Path $venvPy)) {
    Write-Host "Creating .venv ..."
    py -3 -m venv (Join-Path $Root ".venv")
    if (-not (Test-Path $venvPy)) {
        python -m venv (Join-Path $Root ".venv")
    }
    $venvPy = Join-Path $Root ".venv\Scripts\python.exe"
    if (-not (Test-Path $venvPy)) { throw "Could not create .venv or find $venvPy" }
}

Write-Host "Using: $venvPy"
& $venvPy -m pip install -U pip
& $venvPy -m pip install -r (Join-Path $Root "requirements.txt")
# face_recognition would pull source dlib; dlib-bin from requirements.txt satisfies runtime
& $venvPy -m pip install "face_recognition>=1.3.0" --no-deps

Write-Host ""
Write-Host "Done. Activate then run servers:"
Write-Host "  .\.venv\Scripts\Activate.ps1"
Write-Host "  python python\server\python_server.py"
Write-Host "  python dollarpy-service\gesture_service.py"
Write-Host ""
