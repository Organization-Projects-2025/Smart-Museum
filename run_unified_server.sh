#!/bin/bash
# Unified Museum Server Startup Script for Linux/Mac
# This script starts all Python services in a single process with fault isolation

echo "========================================"
echo "Smart Museum - Unified Server"
echo "========================================"
echo ""

# Check if Python is available
if ! command -v python3 &> /dev/null; then
    echo "ERROR: Python 3 is not installed or not in PATH"
    exit 1
fi

# Change to script directory
cd "$(dirname "$0")"

# Optional: Set camera index if needed
# export MUSEUM_CAMERA=0

# Optional: Disable specific services if needed
# export DISABLE_HAND_TRACK=1
# export DISABLE_GESTURE=1

# Run the unified server
echo "Starting Unified Museum Server..."
echo ""
python3 python/server/unified_museum_server.py

# Exit with server's exit code
exit $?